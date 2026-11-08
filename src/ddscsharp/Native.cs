// Copyright(c) 2026 Cyclone DDS C# contributors
//
// This program and the accompanying materials are made available under the
// terms of the Eclipse Public License v. 2.0 which is available at
// http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
// v. 1.0 which is available at
// http://www.eclipse.org/org/documents/edl-v10.php.
//
// SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
using System.Reflection;
using System.Runtime.InteropServices;

namespace Org.Eclipse.CycloneDDS;

/// <summary>Controls native library discovery. Set LibraryDirectory before using DDS.</summary>
public static class DdsRuntime
{
    public static string? LibraryDirectory { get; set; } = Environment.GetEnvironmentVariable("CYCLONEDDS_CSHARP_NATIVE");

    internal static nint Load(string name)
    {
        string filename = OperatingSystem.IsWindows() ? name + ".dll"
            : OperatingSystem.IsMacOS() ? "lib" + name + ".dylib" : "lib" + name + ".so";
        if (LibraryDirectory is { } directory)
            return NativeLibrary.Load(Path.Combine(directory, filename));
        return NativeLibrary.Load(filename, typeof(DdsRuntime).Assembly,
            DllImportSearchPath.SafeDirectories | DllImportSearchPath.AssemblyDirectory);
    }
}

internal static class Native
{
    internal const string Dds = "ddsc", Bridge = "cycloneddscsharp";
    private static readonly Lazy<nint> BridgeModule = new(() => DdsRuntime.Load(Bridge));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint LibraryPathFunction();
    private static readonly Lazy<nint> DdsModule = new(() =>
    {
        var path = Marshal.GetDelegateForFunctionPointer<LibraryPathFunction>(NativeLibrary.GetExport(BridgeModule.Value, "cs_dds_library_path"))();
        return NativeLibrary.Load(Marshal.PtrToStringUTF8(path) ?? throw new DllNotFoundException("Cannot locate the Cyclone DDS module linked to the bridge."));
    });
    static Native()
    {
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);
        if (cs_abi_version() != 1) throw new NotSupportedException("Incompatible Cyclone DDS C# native bridge ABI.");
    }
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? path) =>
        name == Bridge ? BridgeModule.Value : name == Dds ? DdsModule.Value : 0;

    [DllImport(Bridge, CallingConvention = CallingConvention.Cdecl)] internal static extern int cs_descriptor_create(int participant,
        byte[] information, uint informationSize, byte[] mapping, uint mappingSize, uint expectedSize, uint expectedAlignment, out nint descriptor);
    [DllImport(Bridge, CallingConvention = CallingConvention.Cdecl)] internal static extern void cs_descriptor_free(nint descriptor);
    [DllImport(Bridge, CallingConvention = CallingConvention.Cdecl)] internal static extern uint cs_abi_version();
    [DllImport(Bridge, CallingConvention = CallingConvention.Cdecl)] internal static extern void cs_free_sample_contents(nint sample, nint descriptor);
    [DllImport(Bridge, CallingConvention = CallingConvention.Cdecl)] internal static extern int cs_read(int reader, uint max, uint mask, uint take, ulong instance, out nint batch);
    [DllImport(Bridge, CallingConvention = CallingConvention.Cdecl)] internal static extern nint cs_batch_get(nint batch, uint index, out SampleInfo info);
    [DllImport(Bridge, CallingConvention = CallingConvention.Cdecl)] internal static extern int cs_batch_free(nint batch);
    [DllImport(Bridge, CallingConvention = CallingConvention.Cdecl)] internal static extern nuint cs_sequence_size();
    [DllImport(Bridge, CallingConvention = CallingConvention.Cdecl)] internal static extern void cs_sequence_set(nint seq, uint count, nint buffer);
    [DllImport(Bridge, CallingConvention = CallingConvention.Cdecl)] internal static extern uint cs_sequence_length(nint seq);
    [DllImport(Bridge, CallingConvention = CallingConvention.Cdecl)] internal static extern nint cs_sequence_buffer(nint seq);

    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern nint dds_strretcode(int code);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_delete(int entity);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_create_participant(uint domain, nint qos, nint listener);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_create_publisher(int participant, nint qos, nint listener);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_create_subscriber(int participant, nint qos, nint listener);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_create_topic(int participant, nint descriptor, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint qos, nint listener);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_create_writer(int parent, int topic, nint qos, nint listener);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_create_reader(int parent, int topic, nint qos, nint listener);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_get_instance_handle(int entity, out ulong handle);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_get_domainid(int entity, out uint domain);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_set_status_mask(int entity, StatusMask mask);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_read_status(int entity, out StatusMask status, StatusMask mask);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_take_status(int entity, out StatusMask status, StatusMask mask);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_write(int writer, nint sample);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_write_flush(int writer);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_write_ts(int writer, nint sample, long timestamp);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_writedispose(int writer, nint sample);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_dispose(int writer, nint sample);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_dispose_ih(int writer, ulong handle);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_unregister_instance(int writer, nint sample);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_register_instance(int writer, out ulong handle, nint sample);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong dds_lookup_instance(int entity, nint sample);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_instance_get_key(int entity, ulong handle, nint sample);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_wait_for_acks(int entity, long duration);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_assert_liveliness(int entity);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_get_matched_subscriptions(int writer, [Out] ulong[]? handles, nuint size);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_get_matched_publications(int reader, [Out] ulong[]? handles, nuint size);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_create_readcondition(int reader, uint mask);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] internal delegate bool QueryFilter(nint sample);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_create_querycondition(int reader, uint mask, QueryFilter filter);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void DataAvailableCallback(int reader, nint argument);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern nint dds_create_listener(nint argument);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_delete_listener(nint listener);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_lset_data_available(nint listener, DataAvailableCallback callback);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_set_listener(int entity, nint listener);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_create_guardcondition(int participant);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_set_guardcondition(int condition, [MarshalAs(UnmanagedType.I1)] bool triggered);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_triggered(int condition);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_create_waitset(int participant);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_waitset_attach(int waitset, int entity, nint token);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_waitset_detach(int waitset, int entity);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_waitset_wait(int waitset, [Out] nint[] tokens, nuint size, long timeout);

    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern nint dds_create_qos();
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_delete_qos(nint qos);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_set_qos(int entity, nint qos);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern int dds_get_qos(int entity, nint qos);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_reliability(nint qos, ReliabilityKind kind, long blocking);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_writer_batching(nint qos, [MarshalAs(UnmanagedType.I1)] bool batchUpdates);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_durability(nint qos, DurabilityKind kind);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_history(nint qos, HistoryKind kind, int depth);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_resource_limits(nint qos, int samples, int instances, int perInstance);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_partition(nint qos, uint count, [In] nint[] names);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_deadline(nint qos, long duration);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_latency_budget(nint qos, long duration);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_lifespan(nint qos, long duration);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_liveliness(nint qos, LivelinessKind kind, long duration);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_ownership(nint qos, OwnershipKind kind);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_ownership_strength(nint qos, int strength);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_transport_priority(nint qos, int priority);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_destination_order(nint qos, DestinationOrderKind kind);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_writer_data_lifecycle(nint qos, [MarshalAs(UnmanagedType.I1)] bool autodispose);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_reader_data_lifecycle(nint qos, long noWriters, long disposed);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_userdata(nint qos, byte[] data, nuint size);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_topicdata(nint qos, byte[] data, nuint size);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] internal static extern void dds_qset_groupdata(nint qos, byte[] data, nuint size);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool dds_qget_reliability(nint qos, out ReliabilityKind kind, out long blocking);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool dds_qget_durability(nint qos, out DurabilityKind kind);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool dds_qget_history(nint qos, out HistoryKind kind, out int depth);
    [DllImport(Dds, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool dds_qget_resource_limits(nint qos, out int samples, out int instances, out int perInstance);
}
