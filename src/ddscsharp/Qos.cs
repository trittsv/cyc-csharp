// Copyright(c) 2026 Cyclone DDS C# contributors
//
// This program and the accompanying materials are made available under the
// terms of the Eclipse Public License v. 2.0 which is available at
// http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
// v. 1.0 which is available at
// http://www.eclipse.org/org/documents/edl-v10.php.
//
// SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
using System.Runtime.InteropServices;

namespace Org.Eclipse.CycloneDDS;

public enum ReliabilityKind { BestEffort, Reliable }
public enum DurabilityKind { Volatile, TransientLocal, Transient, Persistent }
public enum HistoryKind { KeepLast, KeepAll }
public enum LivelinessKind { Automatic, ManualByParticipant, ManualByTopic }
public enum OwnershipKind { Shared, Exclusive }
public enum DestinationOrderKind { ByReceptionTimestamp, BySourceTimestamp }
public sealed record Reliability(ReliabilityKind Kind, Duration MaxBlockingTime);
public sealed record History(HistoryKind Kind, int Depth = 1);
public sealed record ResourceLimits(int MaxSamples = -1, int MaxInstances = -1, int MaxSamplesPerInstance = -1);
public sealed record Liveliness(LivelinessKind Kind, Duration LeaseDuration);
public sealed record ReaderDataLifecycle(Duration AutopurgeNoWriterSamplesDelay, Duration AutopurgeDisposedSamplesDelay);

/// <summary>Policies omitted from this value use the Cyclone DDS defaults.</summary>
public sealed record Qos
{
    public Reliability? Reliability { get; init; }
    public bool? WriterBatching { get; init; }
    public DurabilityKind? Durability { get; init; }
    public History? History { get; init; }
    public ResourceLimits? ResourceLimits { get; init; }
    public IReadOnlyList<string>? Partitions { get; init; }
    public Duration? Deadline { get; init; }
    public Duration? LatencyBudget { get; init; }
    public Duration? Lifespan { get; init; }
    public Liveliness? Liveliness { get; init; }
    public OwnershipKind? Ownership { get; init; }
    public int? OwnershipStrength { get; init; }
    public int? TransportPriority { get; init; }
    public DestinationOrderKind? DestinationOrder { get; init; }
    public bool? AutodisposeUnregisteredInstances { get; init; }
    public ReaderDataLifecycle? ReaderDataLifecycle { get; init; }
    public byte[]? UserData { get; init; }
    public byte[]? TopicData { get; init; }
    public byte[]? GroupData { get; init; }

    internal QosHandle ToNative()
    {
        if (History is { Kind: HistoryKind.KeepLast, Depth: <= 0 })
            throw new ArgumentOutOfRangeException(nameof(History));
        if (ResourceLimits is { } limits)
        {
            static bool Invalid(int n) => n == 0 || n < -1;
            if (Invalid(limits.MaxSamples) || Invalid(limits.MaxInstances) || Invalid(limits.MaxSamplesPerInstance))
                throw new ArgumentOutOfRangeException(nameof(ResourceLimits));
        }
        var q = new QosHandle();
        try
        {
            var p = q.DangerousGetHandle();
            if (Reliability is { } r) { Validate(r.Kind); Native.dds_qset_reliability(p, r.Kind, r.MaxBlockingTime.Nanoseconds); }
            if (WriterBatching is { } batching) Native.dds_qset_writer_batching(p, batching);
            if (Durability is { } d) { Validate(d); Native.dds_qset_durability(p, d); }
            if (History is { } h) { Validate(h.Kind); Native.dds_qset_history(p, h.Kind, h.Depth); }
            if (ResourceLimits is { } l) Native.dds_qset_resource_limits(p, l.MaxSamples, l.MaxInstances, l.MaxSamplesPerInstance);
            if (Deadline is { } deadline) Native.dds_qset_deadline(p, deadline.Nanoseconds);
            if (LatencyBudget is { } latency) Native.dds_qset_latency_budget(p, latency.Nanoseconds);
            if (Lifespan is { } lifespan) Native.dds_qset_lifespan(p, lifespan.Nanoseconds);
            if (Liveliness is { } liveliness) { Validate(liveliness.Kind); Native.dds_qset_liveliness(p, liveliness.Kind, liveliness.LeaseDuration.Nanoseconds); }
            if (Ownership is { } ownership) { Validate(ownership); Native.dds_qset_ownership(p, ownership); }
            if (OwnershipStrength is { } strength) Native.dds_qset_ownership_strength(p, strength);
            if (TransportPriority is { } priority) Native.dds_qset_transport_priority(p, priority);
            if (DestinationOrder is { } order) { Validate(order); Native.dds_qset_destination_order(p, order); }
            if (AutodisposeUnregisteredInstances is { } autodispose) Native.dds_qset_writer_data_lifecycle(p, autodispose);
            if (ReaderDataLifecycle is { } lifecycle) Native.dds_qset_reader_data_lifecycle(p, lifecycle.AutopurgeNoWriterSamplesDelay.Nanoseconds, lifecycle.AutopurgeDisposedSamplesDelay.Nanoseconds);
            if (UserData is { } user) Native.dds_qset_userdata(p, user, (nuint)user.Length);
            if (TopicData is { } topic) Native.dds_qset_topicdata(p, topic, (nuint)topic.Length);
            if (GroupData is { } group) Native.dds_qset_groupdata(p, group, (nuint)group.Length);
            if (Partitions is { } partitions)
            {
                var strings = new List<nint>();
                try
                {
                    foreach (var value in partitions)
                    {
                        ArgumentNullException.ThrowIfNull(value);
                        if (value.Contains('\0')) throw new ArgumentException("Partitions cannot contain NUL.");
                        strings.Add(Marshal.StringToCoTaskMemUTF8(value));
                    }
                    Native.dds_qset_partition(p, (uint)strings.Count, strings.ToArray());
                }
                finally { foreach (var value in strings) Marshal.FreeCoTaskMem(value); }
            }
            return q;
        }
        catch { q.Dispose(); throw; }
    }

    private static void Validate<T>(T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
    }
}

internal sealed class QosHandle : SafeHandle
{
    internal QosHandle() : base(0, true)
    {
        SetHandle(Native.dds_create_qos());
        if (IsInvalid) throw new OutOfMemoryException();
    }
    public override bool IsInvalid => handle == 0;
    protected override bool ReleaseHandle() { Native.dds_delete_qos(handle); return true; }
}
