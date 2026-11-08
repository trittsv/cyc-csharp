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
using RoundTripModule;

namespace Org.Eclipse.CycloneDDS.Examples.RoundTrip;

internal static class Ping
{
    internal static int Run(string[] args, CancellationToken cancellation)
    {
        bool quit = args.Length == 1 && args[0] == "quit";
        bool useListener = args.Length > 0 && args[0] == "-l";
        if (useListener) args = args[1..];
        if (args.Length > 3) throw new ArgumentException("Too many Ping parameters; use RoundTrip ping --help.");
        int payloadSize = quit ? 0 : ExampleSupport.Number(args, 0, 0, "payloadSize", maximum: 100 * 1024 * 1024);
        int numSamples = quit ? 0 : ExampleSupport.Number(args, 1, 0, "numSamples");
        int timeout = quit ? 0 : ExampleSupport.Number(args, 2, 0, "timeOut");
        Console.WriteLine($"Payload size: {payloadSize} | Samples: {numSamples} | Timeout: {timeout} seconds | Using {(useListener ? "listener" : "waitset")} method");
        using var endpoints = new RoundTripEndpoints(ping: true);
        ExampleSupport.WaitForMatch(endpoints.Participant, endpoints.Writer, endpoints.Reader,
            timeout == 0 ? Duration.Infinite : Duration.FromSeconds(timeout), cancellation);
        var message = new DataType();
        message.payload.AddRange(Enumerable.Repeat((byte)'a', payloadSize));
        if (quit)
        {
            long quitStarted = Stopwatch.GetTimestamp();
            while (true)
            {
                // A legacy Pong can attach its WaitSet after initial discovery.
                // Repeat the command until its reader exits, rather than sleeping
                // for an arbitrary interval and assuming it processed the dispose.
                endpoints.Writer.Write(message);
                endpoints.Writer.DisposeInstance(message);
                endpoints.Writer.Flush();
                endpoints.Writer.WaitForAcknowledgments(ExampleSupport.Remaining(Duration.FromSeconds(5), quitStarted));
                try
                {
                    var remaining = ExampleSupport.Remaining(Duration.FromSeconds(5), quitStarted);
                    ExampleSupport.WaitForReaderDisconnect(endpoints.Participant, endpoints.Writer,
                        new Duration(Math.Min(remaining.Nanoseconds, Duration.FromSeconds(1).Nanoseconds)), cancellation);
                    break;
                }
                catch (TimeoutException) when (Stopwatch.GetElapsedTime(quitStarted).TotalSeconds < 5) { }
            }
            Console.WriteLine("Sent termination command to Pong.");
            return 0;
        }
        using var receiver = new ReceivePump(endpoints, useListener);
        var overall = new Statistics(); var window = new Statistics();
        long warmupStarted = Stopwatch.GetTimestamp(), measuredStarted = 0, windowStarted = 0;
        long beforeWrite = 0, afterWrite = 0;
        Timestamp sentTimestamp = default;
        Timestamp lastCompletedTimestamp = default;
        bool firstEcho = true;
        void Send()
        {
            beforeWrite = Stopwatch.GetTimestamp();
            sentTimestamp = Timestamp.Now;
            endpoints.Writer.Write(message, sentTimestamp);
            afterWrite = Stopwatch.GetTimestamp();
        }
        Console.WriteLine("Waiting for startup jitter to stabilise (5 seconds)...");
        Send();
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (measuredStarted != 0 && timeout > 0 && Stopwatch.GetElapsedTime(measuredStarted).TotalSeconds >= timeout) break;
                var batch = receiver.Wait(Duration.FromMilliseconds(firstEcho ? 100 : 1000), cancellation);
                if (batch is null)
                {
                    if (endpoints.Reader.MatchedPublications.Count == 0) throw new IOException("Pong disconnected.");
                    if (firstEcho && (timeout == 0 || Stopwatch.GetElapsedTime(warmupStarted).TotalSeconds < timeout))
                    {
                        // Preserve the transaction timestamp when retrying the
                        // initial request. Only startup is retried; measurements
                        // start after the peer is responsive and warmed up.
                        Console.WriteLine("Retrying the initial request while Pong starts...");
                        beforeWrite = Stopwatch.GetTimestamp();
                        endpoints.Writer.Write(message, sentTimestamp);
                        afterWrite = Stopwatch.GetTimestamp();
                        continue;
                    }
                    if (timeout > 0 && (firstEcho || Stopwatch.GetElapsedTime(beforeWrite).TotalSeconds >= timeout))
                        throw new TimeoutException("No echo received from Pong.");
                    continue;
                }
                var echo = batch.Samples.FirstOrDefault(s => s.Info.ValidData && s.Info.SourceTimestamp == sentTimestamp);
                if (batch.Samples.Any(s => s.Info.ValidData && s.Info.SourceTimestamp != sentTimestamp && s.Info.SourceTimestamp != lastCompletedTimestamp))
                    throw new InvalidDataException("Pong did not preserve the source timestamp; run only one Ping and Pong pair.");
                if (echo is null) continue;
                if (echo.Data.payload.Count != payloadSize || echo.Data.payload.Any(value => value != (byte)'a'))
                    throw new InvalidDataException("Pong returned a different payload.");
                lastCompletedTimestamp = sentTimestamp;
                if (firstEcho) { firstEcho = false; warmupStarted = batch.AfterTake; }
                if (measuredStarted == 0 && Stopwatch.GetElapsedTime(warmupStarted).TotalSeconds >= 5)
                {
                    measuredStarted = windowStarted = batch.AfterTake;
                    Console.WriteLine("Warm up complete. Statistics: median, minimum, p99, maximum (microseconds).");
                }
                if (measuredStarted != 0)
                {
                    double halfRoundTrip = ExampleSupport.Microseconds(beforeWrite, batch.AfterTake) / 2;
                    double writeAccess = ExampleSupport.Microseconds(beforeWrite, afterWrite);
                    double readAccess = ExampleSupport.Microseconds(batch.BeforeTake, batch.AfterTake);
                    overall.Add(halfRoundTrip, writeAccess, readAccess); window.Add(halfRoundTrip, writeAccess, readAccess);
                    if (Stopwatch.GetElapsedTime(windowStarted).TotalSeconds >= 1) { window.Print("Interval"); window.Clear(); windowStarted = batch.AfterTake; }
                    if (numSamples > 0 && overall.Count >= numSamples) break;
                }
                Send();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        overall.Print("Overall");
        return 0;
    }
}
