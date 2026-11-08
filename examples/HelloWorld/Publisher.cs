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
using HelloWorldData;

namespace Org.Eclipse.CycloneDDS.Examples.HelloWorld;

internal static class Publisher
{
    internal static int Run(CancellationToken cancellation)
    {
        using var participant = ExampleSupport.CreateParticipant();
        using var topic = participant.CreateTopic("HelloWorldData_Msg", Msg.TypeSupport);
        using var publisher = participant.CreatePublisher();
        using var writer = publisher.CreateWriter(topic,
            new Qos { Reliability = new(ReliabilityKind.Reliable, Duration.FromSeconds(5)) });
        ExampleSupport.WaitForMatch(participant, writer, null, Duration.FromSeconds(30), cancellation);
        var sample = new Msg { userID = 1, message = "Hello World" };
        writer.Write(sample);
        writer.WaitForAcknowledgments(Duration.FromSeconds(5));
        Console.WriteLine($"Sent {sample.userID}: {sample.message}");
        // The upstream subscriber may be best effort. A reliable acknowledgment
        // alone does not establish that it has consumed the sample. Keep the
        // instance alive until the one-shot subscriber exits, as the C++ example does.
        ExampleSupport.WaitForReaderDisconnect(participant, writer, Duration.FromSeconds(30), cancellation);
        return 0;
    }
}
