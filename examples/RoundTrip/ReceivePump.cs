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
using System.Collections.Concurrent;
using System.Diagnostics;
using RoundTripModule;

namespace Org.Eclipse.CycloneDDS.Examples.RoundTrip;

internal sealed record ReceivedBatch(IReadOnlyList<Sample<DataType>> Samples, long BeforeTake, long AfterTake);

/// <summary>Receives with either a native listener or a read condition, measuring only the take call.</summary>
internal sealed class ReceivePump : IDisposable
{
    private readonly DataReader<DataType> reader;
    private readonly ReadCondition? condition;
    private readonly WaitSet? waitset;
    private readonly DataAvailableListener<DataType>? listener;
    private readonly ConcurrentQueue<ReceivedBatch> batches = new();
    private readonly AutoResetEvent ready = new(false);
    private readonly object gate = new();
    private bool stopped;
    internal ReceivePump(RoundTripEndpoints endpoints, bool useListener)
    {
        reader = endpoints.Reader;
        if (useListener)
        {
            listener = reader.Listen(_ => Drain());
            Drain(); // Include history received before listener installation.
        }
        else
        {
            condition = reader.CreateReadCondition(DataState.Any);
            waitset = endpoints.Participant.CreateWaitSet();
            waitset.Attach(condition);
        }
    }
    private ReceivedBatch Take()
    {
        long before = Stopwatch.GetTimestamp();
        var samples = reader.Take();
        return new(samples, before, Stopwatch.GetTimestamp());
    }
    private void Drain()
    {
        lock (gate)
        {
            if (stopped) return;
            var batch = Take();
            if (batch.Samples.Count == 0) return;
            batches.Enqueue(batch);
            ready.Set();
        }
    }
    internal ReceivedBatch? Wait(Duration timeout, CancellationToken cancellation)
    {
        if (waitset is not null) return waitset.Wait(timeout, cancellation).Count == 0 ? null : Take();
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            if (listener!.LastException is { } error) throw new InvalidOperationException("RoundTrip listener failed.", error);
            if (batches.TryDequeue(out var batch)) return batch;
            var remaining = ExampleSupport.Remaining(timeout, started);
            if (remaining == Duration.Zero) return null;
            var pause = remaining == Duration.Infinite ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromTicks(Math.Min(remaining.Nanoseconds / 100, 1_000_000));
            ready.WaitOne(pause);
        }
    }
    public void Dispose()
    {
        lock (gate) { if (stopped) return; stopped = true; }
        listener?.Dispose();
        waitset?.Dispose();
        condition?.Dispose();
        ready.Dispose();
    }
}
