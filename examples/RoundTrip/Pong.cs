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

namespace Org.Eclipse.CycloneDDS.Examples.RoundTrip;

internal static class Pong
{
    internal static int Run(string[] args, CancellationToken cancellation)
    {
        bool useListener = args.Length > 0 && args[0] == "-l";
        if (useListener) args = args[1..];
        if (args.Length > 1) throw new ArgumentException("Too many Pong parameters; use RoundTrip pong --help.");
        int timeout = ExampleSupport.Number(args, 0, 0, "timeOut");
        Console.WriteLine($"Timeout: {timeout} seconds | Using {(useListener ? "listener" : "waitset")} method");
        using var endpoints = new RoundTripEndpoints(ping: false);
        ExampleSupport.WaitForMatch(endpoints.Participant, endpoints.Writer, endpoints.Reader,
            timeout == 0 ? Duration.Infinite : Duration.FromSeconds(timeout), cancellation);
        using var receiver = new ReceivePump(endpoints, useListener);
        long lastActive = Stopwatch.GetTimestamp();
        ulong echoed = 0;
        Console.WriteLine("Waiting for Ping samples...");
        while (!cancellation.IsCancellationRequested)
        {
            var batch = receiver.Wait(Duration.FromMilliseconds(100), cancellation);
            if (batch is null)
            {
                if (timeout > 0 && Stopwatch.GetElapsedTime(lastActive).TotalSeconds >= timeout)
                    throw new TimeoutException("Pong inactivity timeout expired.");
                continue;
            }
            foreach (var sample in batch.Samples)
            {
                if (sample.Info.InstanceState == InstanceState.NotAliveDisposed)
                {
                    Console.WriteLine($"Quitting Pong. Echoed {echoed} samples.");
                    return 0;
                }
                if (!sample.Info.ValidData) continue;
                endpoints.Writer.Write(sample.Data, sample.Info.SourceTimestamp);
                echoed++;
                lastActive = Stopwatch.GetTimestamp();
            }
        }
        return 0;
    }
}
