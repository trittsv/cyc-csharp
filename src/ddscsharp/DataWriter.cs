// Copyright(c) 2026 Cyclone DDS C# contributors
//
// This program and the accompanying materials are made available under the
// terms of the Eclipse Public License v. 2.0 which is available at
// http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
// v. 1.0 which is available at
// http://www.eclipse.org/org/documents/edl-v10.php.
//
// SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
namespace Org.Eclipse.CycloneDDS;

public sealed class DataWriter<T> : Entity where T : class
{
    public Topic<T> Topic { get; }
    internal DataWriter(Entity parent, Topic<T> topic, Qos? qos) : base(parent, () =>
    {
        parent.RequireParticipant(topic);
        return Create(qos, q => Native.dds_create_writer(parent.Handle, topic.Handle, q, 0));
    }) => Topic = topic;

    private TResult WithSample<TResult>(T sample, Func<nint, TResult> action)
    {
        ArgumentNullException.ThrowIfNull(sample);
        lock (Gate)
        {
            _ = Handle;
            using var arena = new NativeArena();
            var pointer = arena.Allocate(Topic.Support.Codec.Size);
            Topic.Support.Codec.Write(pointer, sample, arena);
            return action(pointer);
        }
    }
    public void Write(T sample) => WithSample(sample, p => DdsException.Check(Native.dds_write(Handle, p), "Write"));
    /// <summary>Flushes pending samples when writer batching is enabled.</summary>
    public void Flush()
    {
        lock (Gate) DdsException.Check(Native.dds_write_flush(Handle), "Flush");
    }
    public void Write(T sample, Timestamp timestamp) => WithSample(sample, p => DdsException.Check(Native.dds_write_ts(Handle, p, timestamp.Nanoseconds), "WriteTimestamped"));
    public void WriteDispose(T sample) => WithSample(sample, p => DdsException.Check(Native.dds_writedispose(Handle, p), "WriteDispose"));
    public void DisposeInstance(T key) => WithSample(key, p => DdsException.Check(Native.dds_dispose(Handle, p), "DisposeInstance"));
    public void DisposeInstance(ulong handle)
    {
        lock (Gate) DdsException.Check(Native.dds_dispose_ih(Handle, handle), "DisposeInstance");
    }
    public void UnregisterInstance(T key) => WithSample(key, p => DdsException.Check(Native.dds_unregister_instance(Handle, p), "UnregisterInstance"));
    public ulong RegisterInstance(T sample) => WithSample(sample, p =>
    {
        DdsException.Check(Native.dds_register_instance(Handle, out var h, p), "RegisterInstance"); return h;
    });
    public ulong LookupInstance(T key) => WithSample(key, p => Native.dds_lookup_instance(Handle, p));
    public T GetKey(ulong handle)
    {
        lock (Gate)
        {
            using var arena = new NativeArena();
            var p = arena.Allocate(Topic.Support.Codec.Size);
            try
            {
                DdsException.Check(Native.dds_instance_get_key(Handle, handle, p), "GetKey");
                return Topic.Support.Codec.Read(p);
            }
            finally { Native.cs_free_sample_contents(p, Topic.Support.Descriptor); }
        }
    }
    public void WaitForAcknowledgments(Duration timeout)
    {
        lock (Gate) DdsException.Check(Native.dds_wait_for_acks(Handle, timeout.Nanoseconds), "WaitForAcknowledgments");
    }
    public IReadOnlyList<ulong> MatchedSubscriptions
    {
        get
        {
            lock (Gate)
            {
                var size = DdsException.Check(Native.dds_get_matched_subscriptions(Handle, null, 0), "MatchedSubscriptions");
                if (size == 0) return [];
                var handles = new ulong[size];
                var count = DdsException.Check(Native.dds_get_matched_subscriptions(Handle, handles, (nuint)size), "MatchedSubscriptions");
                return handles.Take(Math.Min(size, count)).ToArray();
            }
        }
    }
}
