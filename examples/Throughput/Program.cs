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
using Org.Eclipse.CycloneDDS.Examples.Throughput;

return ExampleSupport.Run(() =>
{
    if (args.Length == 0) throw new ArgumentException("Usage: Throughput pub|sub [parameters]; use --help for details.");
    if (args[0] is "-h" or "--help") { PrintUsage(); return 0; }
    if (args.Length == 2 && args[1] is "-h" or "--help")
    {
        if (args[0] is not ("pub" or "sub" or "publisher" or "subscriber")) throw new ArgumentException("Unknown role.");
        PrintUsage(); return 0;
    }
    using var cancellation = new ExampleCancellation();
    return args[0] switch
    {
        "pub" or "publisher" => Publisher.Run(args[1..], cancellation.Token),
        "sub" or "subscriber" => Subscriber.Run(args[1..], cancellation.Token),
        _ => throw new ArgumentException("Unknown role; expected pub or sub.")
    };
});

static void PrintUsage()
{
    Console.WriteLine("Throughput pub [payloadSize bytes] [burstInterval ms] [burstSize samples] [timeOut seconds, 0=infinite] [partitionName]");
    Console.WriteLine("Defaults: Throughput pub 8192 0 1 0 \"Throughput example\"");
    Console.WriteLine("Throughput sub [maxCycles, 0=infinite] [pollingDelay ms: -1=listener, 0=waitset, >0=polling] [partitionName]");
    Console.WriteLine("Defaults: Throughput sub 0 -1 \"Throughput example\"");
}
