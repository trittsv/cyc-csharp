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

/// <summary>A rooted native callback with coalesced managed notification delivery.</summary>
public sealed class DataAvailableListener<T> : IDisposable where T : class
{
    private readonly DataReader<T> reader;
    private readonly Action<DataReader<T>> action;
    private readonly Native.DataAvailableCallback callback;
    private nint native;
    private int active = 1, pending, running;
    private Exception? failure;
    public Exception? LastException => Volatile.Read(ref failure);
    internal DataAvailableListener(DataReader<T> reader, Action<DataReader<T>> action)
    {
        this.reader = reader;
        this.action = action;
        callback = (_, _) => Notify();
        native = Native.dds_create_listener(0);
        if (native == 0) throw new OutOfMemoryException();
        try
        {
            Native.dds_lset_data_available(native, callback);
            DdsException.Check(Native.dds_set_listener(reader.Handle, native), "SetListener");
        }
        catch { Native.dds_delete_listener(native); native = 0; throw; }
    }
    private void Notify()
    {
        if (Volatile.Read(ref active) == 0) return;
        Interlocked.Exchange(ref pending, 1);
        if (Interlocked.CompareExchange(ref running, 1, 0) == 0)
            ThreadPool.QueueUserWorkItem(_ => Drain());
    }
    private void Drain()
    {
        try
        {
            while (Volatile.Read(ref active) != 0 && Interlocked.Exchange(ref pending, 0) != 0)
            {
                if (reader.IsDisposed) return;
                try { action(reader); }
                catch (Exception error) { Interlocked.CompareExchange(ref failure, error, null); }
            }
        }
        finally
        {
            Interlocked.Exchange(ref running, 0);
            if (Volatile.Read(ref active) != 0 && Volatile.Read(ref pending) != 0) Notify();
        }
    }
    /// <summary>Stops new notifications. An already running user callback may finish after Dispose returns.</summary>
    public void Dispose()
    {
        lock (reader.Gate)
        {
            if (Interlocked.Exchange(ref active, 0) == 0) return;
            if (native != 0)
            {
                DdsException.Check(Native.dds_set_listener(reader.Handle, 0), "ClearListener");
                Native.dds_delete_listener(native);
                native = 0;
            }
            GC.KeepAlive(callback);
        }
    }
}
