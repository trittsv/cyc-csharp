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

/// <summary>The native in-memory representation, separate from the DDS wire representation.</summary>
public sealed class NativeLayout
{
    private NativeLayout(int size, int alignment, int[]? offsets = null)
    { Size = size; Alignment = alignment; Offsets = offsets ?? []; }
    public int Size { get; }
    public int Alignment { get; }
    public IReadOnlyList<int> Offsets { get; }
    public static NativeLayout Scalar(int size, int alignment)
    {
        // System V i386, including Android x86, aligns 64-bit scalars to four
        // bytes. Windows x86 and ARMv7 use eight-byte scalar alignment.
        if (size == 8 && alignment == 8 && RuntimeInformation.ProcessArchitecture == Architecture.X86 && !OperatingSystem.IsWindows())
            alignment = 4;
        return new(size, alignment);
    }
    public static NativeLayout Pointer { get; } = Scalar(IntPtr.Size, IntPtr.Size);
    public static NativeLayout Sequence { get; } = Scalar(IntPtr.Size == 8 ? 24 : 16, IntPtr.Size);
    private static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);
    public static NativeLayout Array(NativeLayout element, params int[] dimensions)
    {
        var size = element.Size;
        foreach (var dimension in dimensions) size = checked(size * dimension);
        return Scalar(size, element.Alignment);
    }
    public static NativeLayout Struct(params NativeLayout[] members)
    {
        var size = 0; var alignment = 1; var offsets = new int[members.Length];
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i]; alignment = Math.Max(alignment, member.Alignment);
            size = Align(size, member.Alignment); offsets[i] = size; size = checked(size + member.Size);
        }
        return new(Align(size, alignment), alignment, offsets);
    }
    public static NativeLayout Union(NativeLayout discriminator, params NativeLayout[] branches)
    {
        var alignment = branches.Max(b => b.Alignment);
        var offset = Align(discriminator.Size, alignment);
        alignment = Math.Max(alignment, discriminator.Alignment);
        var offsets = new int[branches.Length + 1];
        System.Array.Fill(offsets, offset, 1, branches.Length);
        return new(Align(checked(offset + branches.Max(b => b.Size)), alignment), alignment, offsets);
    }
}

/// <summary>Compiler-generated XTypes metadata. The shared runtime builds its native descriptor on first use.</summary>
public sealed class GeneratedTypeMetadata(NativeLayout layout, byte[] typeInformation, byte[] typeMapping)
{
    private readonly object gate = new();
    private DescriptorHandle? handle;
    public NativeLayout Layout { get; } = layout;
    public nint Descriptor => handle?.DangerousGetHandle()
        ?? throw new InvalidOperationException("Create a topic before accessing its native descriptor.");
    internal nint ResolveDescriptor(int participant)
    {
        lock (gate)
        {
            if (handle is null)
            {
                DdsException.Check(Native.cs_descriptor_create(participant, typeInformation, checked((uint)typeInformation.Length),
                    typeMapping, checked((uint)typeMapping.Length), checked((uint)Layout.Size), checked((uint)Layout.Alignment), out var pointer),
                    "Build native type descriptor");
                handle = new(pointer);
            }
            return handle.DangerousGetHandle();
        }
    }
    private sealed class DescriptorHandle : SafeHandle
    {
        internal DescriptorHandle(nint pointer) : base(0, true) => SetHandle(pointer);
        public override bool IsInvalid => handle == 0;
        protected override bool ReleaseHandle() { Native.cs_descriptor_free(handle); return true; }
    }
}

/// <summary>Metadata table emitted into a generated C# source file.</summary>
public sealed class GeneratedTypeLibrary(GeneratedTypeMetadata[] types)
{
    public GeneratedTypeMetadata Type(int type) => types[type];
    public int Size(int type) => types[type].Layout.Size;
    public int Offset(int type, int member) => types[type].Layout.Offsets[member];
}
