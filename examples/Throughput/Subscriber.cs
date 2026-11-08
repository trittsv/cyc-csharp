// Copyright(c) 2022 ZettaScale Technology and others
// Copyright(c) 2026 Cyclone DDS C# contributors
//
// This program and the accompanying materials are made available under the
// terms of the Eclipse Public License v. 2.0 which is available at
// http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
// v. 1.0 which is available at
// http://www.eclipse.org/org/documents/edl-v10.php.
//
// SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
using System.Diagnostics;
using System.Globalization;
using ThroughputModule;

namespace Org.Eclipse.CycloneDDS.Examples.Throughput;

internal sealed record TransferSnapshot(ulong Samples, ulong Bytes, ulong OutOfOrder, int PayloadSize);

internal sealed class TransferStatistics
{
    private readonly object gate = new();
    private readonly Dictionary<ulong, ulong> expected = [];
    private ulong samples, bytes, outOfOrder;
    private int payloadSize;
    private bool stopped;
    internal void Drain(DataReader<DataType> reader)
    {
        lock (gate)
        {
            if (stopped) return;
            foreach (var sample in reader.Take(1000))
            {
                if (!sample.Info.ValidData) continue;
                ulong publisher = sample.Info.PublicationHandle;
                if (expected.TryGetValue(publisher, out var next) && next != sample.Data.count) outOfOrder++;
                expected[publisher] = unchecked(sample.Data.count + 1);
                samples++;
                payloadSize = sample.Data.payload.Count;
                bytes = checked(bytes + (ulong)payloadSize);
            }
        }
    }
    internal TransferSnapshot Snapshot(bool stop = false)
    {
        lock (gate) { if (stop) stopped = true; return new(samples, bytes, outOfOrder, payloadSize); }
    }
}

internal static class Subscriber
{
    internal static int Run(string[] args, CancellationToken cancellation)
    {
        if (args.Length > 3) throw new ArgumentException("Too many subscriber parameters; use Throughput sub --help.");
        int maxCycles = ExampleSupport.Number(args, 0, 0, "maxCycles");
        int pollingDelay = ExampleSupport.Number(args, 1, -1, "pollingDelay", minimum: -1);
        string partition = args.Length > 2 ? args[2] : "Throughput example";
        Console.WriteLine($"Cycles: {maxCycles} | Polling delay: {pollingDelay} ms | Partition: {partition}");
        Console.WriteLine($"Using {(pollingDelay < 0 ? "listener" : pollingDelay == 0 ? "waitset" : "polling")} approach.");
        using var participant = ExampleSupport.CreateParticipant();
        var qos = new Qos
        {
            Reliability = new(ReliabilityKind.Reliable, Duration.FromSeconds(10)),
            History = new(HistoryKind.KeepAll), ResourceLimits = new(1000)
        };
        using var topic = participant.CreateTopic("Throughput", DataType.TypeSupport, qos);
        using var subscriber = participant.CreateSubscriber(new Qos { Partitions = [partition] });
        using var reader = subscriber.CreateReader(topic, qos);
        ExampleSupport.WaitForMatch(participant, null, reader, Duration.FromSeconds(30), cancellation);
        var statistics = new TransferStatistics();
        using var condition = reader.CreateReadCondition(DataState.Any);
        using var waitset = participant.CreateWaitSet();
        waitset.Attach(condition);
        using var listener = pollingDelay < 0 ? reader.Listen(statistics.Drain) : null;
        long started = Stopwatch.GetTimestamp(), previous = started;
        var last = statistics.Snapshot();
        int cycles = 0;
        Console.WriteLine("Waiting for samples...");
        if (pollingDelay < 0) statistics.Drain(reader);
        try
        {
            while (!cancellation.IsCancellationRequested && (maxCycles == 0 || cycles < maxCycles))
            {
                if (pollingDelay == 0) _ = waitset.Wait(Duration.FromMilliseconds(100), cancellation);
                else ExampleSupport.SleepUntil(Stopwatch.GetTimestamp(), TimeSpan.FromMilliseconds(pollingDelay > 0 ? pollingDelay : 100), cancellation);
                if (listener?.LastException is { } error) throw new InvalidOperationException("Subscriber listener failed.", error);
                if (pollingDelay >= 0) statistics.Drain(reader);
                var current = statistics.Snapshot();
                long now = Stopwatch.GetTimestamp();
                double seconds = (now - previous) / (double)Stopwatch.Frequency;
                if (seconds >= 1 && current.Samples != last.Samples)
                {
                    Console.WriteLine(FormattableString.Invariant($"Payload size: {current.PayloadSize} | Total received: {current.Samples} samples, {current.Bytes} bytes | Out of order: {current.OutOfOrder} samples | Transfer rate: {(current.Samples - last.Samples) / seconds:F2} samples/s, {(current.Bytes - last.Bytes) / seconds / 125000:F2} Mbit/s"));
                    cycles++; previous = now; last = current;
                }
                if (reader.MatchedPublications.Count == 0) break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { listener?.Dispose(); }
        statistics.Drain(reader);
        var total = statistics.Snapshot(stop: true);
        double elapsed = Math.Max(Stopwatch.GetElapsedTime(started).TotalSeconds, double.Epsilon);
        Console.WriteLine($"Total received: {total.Samples} samples, {total.Bytes} bytes");
        Console.WriteLine($"Out of order: {total.OutOfOrder} samples");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Average transfer rate: {total.Samples / elapsed:F2} samples/s, {total.Bytes / elapsed / 125000:F2} Mbit/s"));
        return 0;
    }
}
