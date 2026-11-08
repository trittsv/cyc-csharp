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

/// <summary>Owns a DDS entity. Closing a parent closes all its managed children.</summary>
public abstract class Entity : IDisposable
{
    internal readonly object Gate;
    private readonly HashSet<Entity> children = [];
    private int handle;
    public Entity? Parent { get; }
    public bool IsDisposed { get { lock (Gate) return handle == 0; } }
    internal int Handle
    {
        get { ObjectDisposedException.ThrowIf(handle == 0, this); return handle; }
    }
    internal DomainParticipant Participant => this as DomainParticipant ?? Parent!.Participant;

    internal Entity(Entity? parent, Func<int> create)
    {
        Parent = parent;
        Gate = parent?.Gate ?? new object();
        lock (Gate)
        {
            if (parent is not null) _ = parent.Handle;
            handle = DdsException.Check(create(), GetType().Name);
            parent?.children.Add(this);
        }
    }

    public ulong InstanceHandle
    {
        get { lock (Gate) { DdsException.Check(Native.dds_get_instance_handle(Handle, out var value), "GetInstanceHandle"); return value; } }
    }
    public uint DomainId
    {
        get { lock (Gate) { DdsException.Check(Native.dds_get_domainid(Handle, out var value), "GetDomainId"); return value; } }
    }
    public void SetStatusMask(StatusMask mask)
    {
        lock (Gate) DdsException.Check(Native.dds_set_status_mask(Handle, mask), "SetStatusMask");
    }
    public StatusMask ReadStatus(StatusMask mask)
    {
        lock (Gate) { DdsException.Check(Native.dds_read_status(Handle, out var result, mask), "ReadStatus"); return result; }
    }
    public StatusMask TakeStatus(StatusMask mask)
    {
        lock (Gate) { DdsException.Check(Native.dds_take_status(Handle, out var result, mask), "TakeStatus"); return result; }
    }
    public void SetQos(Qos qos)
    {
        ArgumentNullException.ThrowIfNull(qos);
        lock (Gate)
        {
            using var native = qos.ToNative();
            DdsException.Check(Native.dds_set_qos(Handle, native.DangerousGetHandle()), "SetQos");
        }
    }
    /// <summary>Reads the actual history, reliability, durability and resource limit policies.</summary>
    public Qos GetCoreQos()
    {
        lock (Gate)
        {
            using var native = new QosHandle();
            var p = native.DangerousGetHandle();
            DdsException.Check(Native.dds_get_qos(Handle, p), "GetQos");
            return new Qos
            {
                Reliability = Native.dds_qget_reliability(p, out var r, out var b) ? new(r, new(b)) : null,
                History = Native.dds_qget_history(p, out var h, out var d) ? new(h, d) : null,
                Durability = Native.dds_qget_durability(p, out var dur) ? dur : null,
                ResourceLimits = Native.dds_qget_resource_limits(p, out var s, out var i, out var pi) ? new(s, i, pi) : null
            };
        }
    }
    public void AssertLiveliness()
    {
        lock (Gate) DdsException.Check(Native.dds_assert_liveliness(Handle), "AssertLiveliness");
    }
    public void Dispose()
    {
        lock (Gate)
        {
            if (handle == 0) return;
            foreach (var child in children.ToArray()) child.Dispose();
            BeforeDelete();
            int result = Native.dds_delete(handle);
            GC.KeepAlive(this);
            handle = 0;
            Parent?.children.Remove(this);
            GC.SuppressFinalize(this);
            DdsException.Check(result, "Delete");
        }
    }
    internal virtual void BeforeDelete() { }
    ~Entity()
    {
        // Never propagate native errors from finalization. Explicit disposal reports them.
        try { Dispose(); } catch { }
    }
    internal static int Create(Qos? qos, Func<nint, int> create)
    {
        using var value = qos?.ToNative();
        return create(value?.DangerousGetHandle() ?? 0);
    }
    internal void RequireParticipant(Entity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!ReferenceEquals(Participant, other.Participant))
            throw new ArgumentException("Entities must belong to the same participant.");
        _ = other.Handle;
    }
}

public sealed class DomainParticipant : Entity
{
    public const uint DefaultDomain = uint.MaxValue;
    public DomainParticipant(uint domainId = DefaultDomain, Qos? qos = null)
        : base(null, () => Create(qos, q => Native.dds_create_participant(domainId, q, 0))) { }
    public Publisher CreatePublisher(Qos? qos = null) => new(this, qos);
    public Subscriber CreateSubscriber(Qos? qos = null) => new(this, qos);
    public Topic<T> CreateTopic<T>(string name, TypeSupport<T> support, Qos? qos = null) where T : class => new(this, name, support, qos);
    public DataWriter<T> CreateWriter<T>(Topic<T> topic, Qos? qos = null) where T : class => new(this, topic, qos);
    public DataReader<T> CreateReader<T>(Topic<T> topic, Qos? qos = null) where T : class => new(this, topic, qos);
    public GuardCondition CreateGuardCondition() => new(this);
    public WaitSet CreateWaitSet() => new(this);
}

public sealed class Publisher : Entity
{
    internal Publisher(DomainParticipant parent, Qos? qos) : base(parent, () => Create(qos, q => Native.dds_create_publisher(parent.Handle, q, 0))) { }
    public DataWriter<T> CreateWriter<T>(Topic<T> topic, Qos? qos = null) where T : class => new(this, topic, qos);
    public void WaitForAcknowledgments(Duration timeout)
    {
        lock (Gate) DdsException.Check(Native.dds_wait_for_acks(Handle, timeout.Nanoseconds), "WaitForAcknowledgments");
    }
}

public sealed class Subscriber : Entity
{
    internal Subscriber(DomainParticipant parent, Qos? qos) : base(parent, () => Create(qos, q => Native.dds_create_subscriber(parent.Handle, q, 0))) { }
    public DataReader<T> CreateReader<T>(Topic<T> topic, Qos? qos = null) where T : class => new(this, topic, qos);
}

public sealed class Topic<T> : Entity where T : class
{
    public string Name { get; }
    public string TypeName => Support.TypeName;
    public TypeSupport<T> Support { get; }
    internal Topic(DomainParticipant participant, string name, TypeSupport<T> support, Qos? qos)
        : base(participant, () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(support);
            if (name.Contains('\0')) throw new ArgumentException("Topic name cannot contain NUL.", nameof(name));
            var topic = Create(qos, q => Native.dds_create_topic(participant.Handle, support.ResolveDescriptor(participant.Handle), name, q, 0));
            GC.KeepAlive(support);
            return topic;
        }) { Name = name; Support = support; }
}
