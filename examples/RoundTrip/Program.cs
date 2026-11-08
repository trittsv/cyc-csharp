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
using Org.Eclipse.CycloneDDS.Examples;
using Org.Eclipse.CycloneDDS.Examples.RoundTrip;

return ExampleSupport.Run(() =>
{
    if (args.Length == 0) throw new ArgumentException("Usage: RoundTrip ping|pong [parameters]; use --help for details.");
    if (args[0] is "-h" or "--help") { PrintUsage(); return 0; }
    if (args.Skip(1).Any(a => a is "-h" or "--help"))
    {
        if (args[0] is not ("ping" or "pong")) throw new ArgumentException("Unknown role.");
        PrintUsage(); return 0;
    }
    using var cancellation = new ExampleCancellation();
    return args[0] switch
    {
        "ping" => Ping.Run(args[1..], cancellation.Token),
        "pong" => Pong.Run(args[1..], cancellation.Token),
        _ => throw new ArgumentException("Unknown role; expected ping or pong.")
    };
});

static void PrintUsage()
{
    Console.WriteLine("RoundTrip ping [-l] [payloadSize bytes, 0..104857600] [numSamples, 0=infinite] [timeOut measurement seconds, 0=infinite]");
    Console.WriteLine("Defaults: RoundTrip ping 0 0 0; -l uses a listener instead of a waitset.");
    Console.WriteLine("RoundTrip ping quit: send a DDS dispose notification to terminate Pong.");
    Console.WriteLine("RoundTrip pong [-l] [timeOut inactivity seconds, 0=infinite]");
    Console.WriteLine("Defaults: RoundTrip pong 0; Ping warms up for 5 seconds before measuring.");
}
