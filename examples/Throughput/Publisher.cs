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
using ThroughputModule;

namespace Org.Eclipse.CycloneDDS.Examples.Throughput;

internal static class Publisher
{
    internal static int Run(string[] args, CancellationToken cancellation)
    {
        if (args.Length > 5) throw new ArgumentException("Too many publisher parameters; use Throughput pub --help.");
        int payloadSize = ExampleSupport.Number(args, 0, 8192, "payloadSize", maximum: 100 * 1024 * 1024);
        int burstInterval = ExampleSupport.Number(args, 1, 0, "burstInterval");
        int burstSize = ExampleSupport.Number(args, 2, 1, "burstSize", minimum: 1);
        int timeout = ExampleSupport.Number(args, 3, 0, "timeOut");
        string partition = args.Length > 4 ? args[4] : "Throughput example";
        Console.WriteLine($"Payload: {payloadSize} bytes | Burst interval: {burstInterval} ms | Burst size: {burstSize} | Timeout: {timeout} seconds | Partition: {partition}");
        using var participant = ExampleSupport.CreateParticipant();
        var qos = new Qos
        {
            Reliability = new(ReliabilityKind.Reliable, Duration.FromSeconds(10)),
            History = new(HistoryKind.KeepAll), ResourceLimits = new(100)
        };
        using var topic = participant.CreateTopic("Throughput", DataType.TypeSupport, qos);
        using var publisher = participant.CreatePublisher(new Qos { Partitions = [partition] });
        using var writer = publisher.CreateWriter(topic, qos with { WriterBatching = true });
        ExampleSupport.WaitForMatch(participant, writer, null, Duration.FromSeconds(30), cancellation);
        var sample = new DataType();
        sample.payload.AddRange(Enumerable.Repeat((byte)'a', payloadSize));
        long started = Stopwatch.GetTimestamp();
        bool timedOut = false;
        Console.WriteLine("Writing samples...");
        try
        {
            while (!cancellation.IsCancellationRequested && !timedOut)
            {
                if (timeout > 0 && Stopwatch.GetElapsedTime(started).TotalSeconds >= timeout) { timedOut = true; break; }
                long burstStarted = Stopwatch.GetTimestamp();
                for (int i = 0; i < burstSize && !cancellation.IsCancellationRequested; i++)
                {
                    writer.Write(sample);
                    sample.count++;
                    if (timeout > 0 && Stopwatch.GetElapsedTime(started).TotalSeconds >= timeout) { timedOut = true; break; }
                }
                if (burstInterval > 0)
                {
                    writer.Flush();
                    if (!timedOut)
                    {
                        var interval = TimeSpan.FromMilliseconds(burstInterval);
                        if (timeout > 0)
                        {
                            var remaining = TimeSpan.FromSeconds(timeout) - Stopwatch.GetElapsedTime(started);
                            if (remaining <= TimeSpan.Zero) { timedOut = true; break; }
                            var sinceBurst = Stopwatch.GetElapsedTime(burstStarted);
                            if (interval > sinceBurst + remaining) interval = sinceBurst + remaining;
                        }
                        ExampleSupport.SleepUntil(burstStarted, interval, cancellation);
                    }
                }
                if (writer.MatchedSubscriptions.Count == 0) { Console.WriteLine("Writer has no reader to communicate with."); break; }
            }
        }
        catch (DdsException error) when (error.Code == ReturnCode.Timeout) { timedOut = true; Console.WriteLine("Writer timed out while waiting for resources."); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { writer.Flush(); }
        writer.WaitForAcknowledgments(Duration.FromSeconds(5));
        writer.DisposeInstance(sample);
        writer.Flush();
        Console.WriteLine($"{(timedOut ? "Timed out" : "Terminated")}, {sample.count} samples written.");
        return 0;
    }
}
