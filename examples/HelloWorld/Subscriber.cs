// Copyright(c) 2006 to 2022 ZettaScale Technology and others
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
using HelloWorldData;

namespace Org.Eclipse.CycloneDDS.Examples.HelloWorld;

internal static class Subscriber
{
    internal static int Run(CancellationToken cancellation)
    {
        using var participant = ExampleSupport.CreateParticipant();
        using var topic = participant.CreateTopic("HelloWorldData_Msg", Msg.TypeSupport);
        using var subscriber = participant.CreateSubscriber();
        using var reader = subscriber.CreateReader(topic,
            new Qos { Reliability = new(ReliabilityKind.Reliable, Duration.FromSeconds(5)) });
        using var condition = reader.CreateReadCondition();
        using var waitset = participant.CreateWaitSet();
        waitset.Attach(condition);
        Console.WriteLine("Waiting for a message...");
        var started = Stopwatch.GetTimestamp();
        while (waitset.Wait(ExampleSupport.Remaining(Duration.FromSeconds(30), started), cancellation).Count > 0)
        {
            foreach (var sample in reader.Take().Where(s => s.Info.ValidData))
            {
                Console.WriteLine($"Received {sample.Data.userID}: {sample.Data.message}");
                return 0;
            }
        }
        throw new TimeoutException("No HelloWorld sample received.");
    }
}
