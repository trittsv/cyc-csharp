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
using RoundTripModule;

namespace Org.Eclipse.CycloneDDS.Examples.RoundTrip;

internal sealed class RoundTripEndpoints : IDisposable
{
    internal DomainParticipant Participant { get; }
    internal DataReader<DataType> Reader { get; }
    internal DataWriter<DataType> Writer { get; }
    internal RoundTripEndpoints(bool ping)
    {
        Participant = ExampleSupport.CreateParticipant();
        try
        {
            var qos = new Qos { Reliability = new(ReliabilityKind.Reliable, Duration.FromSeconds(10)), History = new(HistoryKind.KeepAll) };
            var topic = Participant.CreateTopic("RoundTrip", DataType.TypeSupport, qos);
            var publisher = Participant.CreatePublisher(new Qos { Partitions = [ping ? "ping" : "pong"] });
            var subscriber = Participant.CreateSubscriber(new Qos { Partitions = [ping ? "pong" : "ping"] });
            Writer = publisher.CreateWriter(topic, qos with { AutodisposeUnregisteredInstances = false });
            Reader = subscriber.CreateReader(topic, qos);
        }
        catch { Participant.Dispose(); throw; }
    }
    public void Dispose() => Participant.Dispose();
}
