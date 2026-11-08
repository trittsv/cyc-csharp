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

namespace Org.Eclipse.CycloneDDS;

public abstract class Condition : Entity
{
    internal Condition(Entity parent, Func<int> create) : base(parent, create) { }
    public bool Triggered
    {
        get { lock (Gate) return DdsException.Check(Native.dds_triggered(Handle), "Triggered") != 0; }
    }
}
public class ReadCondition : Condition
{
    public DataState State { get; }
    internal ReadCondition(Entity reader, DataState state) : base(reader, () => Native.dds_create_readcondition(reader.Handle, state.Mask)) => State = state;
    internal ReadCondition(Entity reader, DataState state, Func<int> create) : base(reader, create) => State = state;
    internal virtual void CheckError() { }
}

public sealed class QueryCondition<T> : ReadCondition where T : class
{
    private readonly QueryPredicate<T> predicate;
    internal QueryCondition(DataReader<T> reader, DataState state, Func<T, bool> filter)
        : this(reader, state, new QueryPredicate<T>(reader.Topic.Support.Codec, filter)) { }
    private QueryCondition(DataReader<T> reader, DataState state, QueryPredicate<T> predicate)
        : base(reader, state, () => Native.dds_create_querycondition(reader.Handle, state.Mask, predicate.Callback)) => this.predicate = predicate;
    internal override void CheckError() => predicate.CheckError();
    public void ThrowIfFilterFailed() => CheckError();
}

internal sealed class QueryPredicate<T> where T : class
{
    private Exception? failure;
    internal Native.QueryFilter Callback { get; }
    internal QueryPredicate(Codec<T> codec, Func<T, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        Callback = pointer =>
        {
            try { return filter(codec.Read(pointer)); }
            catch (Exception e) { Interlocked.CompareExchange(ref failure, e, null); return false; }
        };
    }
    internal void CheckError()
    {
        if (Volatile.Read(ref failure) is { } error)
            throw new InvalidOperationException("DDS query predicate failed. The predicate must not call DDS operations.", error);
    }
}
public sealed class GuardCondition : Condition
{
    internal GuardCondition(DomainParticipant participant) : base(participant, () => Native.dds_create_guardcondition(participant.Handle)) { }
    public void Set(bool triggered)
    {
        lock (Gate) DdsException.Check(Native.dds_set_guardcondition(Handle, triggered), "SetGuardCondition");
    }
}

public sealed class WaitSet : Entity
{
    private readonly Dictionary<nint, Entity> attached = [];
    private nint nextToken = 1;
    internal WaitSet(DomainParticipant participant) : base(participant, () => Native.dds_create_waitset(participant.Handle)) { }
    public void Attach(Entity condition)
    {
        lock (Gate)
        {
            RequireParticipant(condition);
            if (attached.ContainsValue(condition)) throw new ArgumentException("Entity already attached.", nameof(condition));
            var token = checked(nextToken++);
            DdsException.Check(Native.dds_waitset_attach(Handle, condition.Handle, token), "Attach");
            attached.Add(token, condition);
        }
    }
    public void Detach(Entity condition)
    {
        lock (Gate)
        {
            var entry = attached.FirstOrDefault(x => ReferenceEquals(x.Value, condition));
            if (entry.Key == 0) throw new ArgumentException("Entity is not attached.", nameof(condition));
            DdsException.Check(Native.dds_waitset_detach(Handle, condition.Handle), "Detach");
            attached.Remove(entry.Key);
        }
    }
    /// <summary>Returns triggered attachments. Timeout returns an empty list.</summary>
    public IReadOnlyList<Entity> Wait(Duration timeout, CancellationToken cancellationToken = default)
    {
        // Bounded native waits allow concurrent disposal and guard changes without pinning
        // the participant lock for the duration of an infinite wait.
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long elapsed = checked(stopwatch.Elapsed.Ticks * 100);
            long remaining = timeout == Duration.Infinite ? long.MaxValue : Math.Max(0, timeout.Nanoseconds - elapsed);
            int count;
            lock (Gate)
            {
                foreach (var condition in attached.Values.OfType<ReadCondition>()) condition.CheckError();
                foreach (var stale in attached.Where(x => x.Value.IsDisposed).Select(x => x.Key).ToArray()) attached.Remove(stale);
                var tokens = new nint[Math.Max(1, attached.Count)];
                count = DdsException.Check(Native.dds_waitset_wait(Handle, tokens, (nuint)tokens.Length, Math.Min(remaining, 10_000_000)), "Wait");
                if (count > 0)
                    return tokens.Take(Math.Min(count, tokens.Length)).Where(attached.ContainsKey).Select(x => attached[x]).ToArray();
            }
            if (remaining == 0 || (timeout != Duration.Infinite && stopwatch.Elapsed.Ticks * 100 >= timeout.Nanoseconds)) return [];
            Thread.Yield();
        }
    }
}
