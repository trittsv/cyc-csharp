// Copyright(c) 2026 Cyclone DDS C# contributors
//
// This program and the accompanying materials are made available under the
// terms of the Eclipse Public License v. 2.0 which is available at
// http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
// v. 1.0 which is available at
// http://www.eclipse.org/org/documents/edl-v10.php.
//
// SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using Omg.Types;

namespace Org.Eclipse.CycloneDDS;

/// <summary>Generated type metadata and marshalling. Serialization on the wire is performed by Cyclone DDS.</summary>
public sealed class TypeSupport<T> where T : class
{
    private readonly nint descriptor;
    private readonly GeneratedTypeMetadata? metadata;
    private readonly Func<T> create;
    public TypeSupport(string typeName, nint descriptor, Codec<T> codec, Func<T> create)
    {
        if (descriptor == 0) throw new ArgumentException("A topic descriptor is required.", nameof(descriptor));
        TypeName = typeName; this.descriptor = descriptor; Codec = codec; this.create = create;
    }
    public TypeSupport(string typeName, GeneratedTypeMetadata metadata, Codec<T> codec, Func<T> create)
    {
        TypeName = typeName; this.metadata = metadata; Codec = codec; this.create = create;
    }
    public string TypeName { get; }
    public nint Descriptor => metadata?.Descriptor ?? descriptor;
    internal nint ResolveDescriptor(int participant) => metadata?.ResolveDescriptor(participant) ?? descriptor;
    public Codec<T> Codec { get; }
    public T Create() => create();
    public T Clone(T value)
    {
        using var arena = new NativeArena();
        var pointer = arena.Allocate(Codec.Size);
        Codec.Write(pointer, value, arena);
        return Codec.Read(pointer);
    }
}

/// <summary>An exception-safe arena for outgoing samples. Native writers copy data before returning.</summary>
public sealed unsafe class NativeArena : IDisposable
{
    private readonly List<nint> allocations = [];
    private readonly HashSet<object> path = new(ReferenceEqualityComparer.Instance);
    private bool disposed;
    public nint Allocate(int size)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        var p = (nint)NativeMemory.AllocZeroed((nuint)Math.Max(1, size));
        if (p == 0) throw new OutOfMemoryException();
        allocations.Add(p);
        return p;
    }
    internal void Enter(object value)
    {
        if (path.Count >= 256 || !path.Add(value))
            throw new ArgumentException("DDS samples must be acyclic and at most 256 objects deep.");
    }
    internal void Leave(object value) => path.Remove(value);
    public void Dispose()
    {
        if (disposed) return;
        foreach (var p in allocations) NativeMemory.Free((void*)p);
        allocations.Clear();
        disposed = true;
    }
}

public abstract class Codec<T>
{
    public abstract int Size { get; }
    public abstract T Read(nint pointer);
    public abstract void Write(nint pointer, T value, NativeArena arena);
}

public sealed class DeferredCodec<T>(Func<Codec<T>> resolve) : Codec<T>
{
    public override int Size => resolve().Size;
    public override T Read(nint pointer) => resolve().Read(pointer);
    public override void Write(nint pointer, T value, NativeArena arena) => resolve().Write(pointer, value, arena);
}

public interface IMemberCodec<T>
{
    void Read(nint pointer, T value);
    void Write(nint pointer, T value, NativeArena arena);
}
public sealed class MemberCodec<T, TMember>(int offset, Codec<TMember> codec, Func<T, TMember> get, Action<T, TMember> set) : IMemberCodec<T>
{
    public void Read(nint pointer, T value) => set(value, codec.Read(pointer + offset));
    public void Write(nint pointer, T value, NativeArena arena) => codec.Write(pointer + offset, get(value), arena);
}
public sealed class StructCodec<T>(int size, Func<T> create, params IMemberCodec<T>[] members) : Codec<T> where T : class
{
    public override int Size => size;
    public override T Read(nint pointer)
    {
        var value = create();
        foreach (var member in members) member.Read(pointer, value);
        return value;
    }
    public override void Write(nint pointer, T value, NativeArena arena)
    {
        ArgumentNullException.ThrowIfNull(value);
        arena.Enter(value);
        try { foreach (var member in members) member.Write(pointer, value, arena); }
        finally { arena.Leave(value); }
    }
}

public sealed class DelegateCodec<T>(int size, Func<nint, T> read, Action<nint, T, NativeArena> write) : Codec<T>
{
    public override int Size => size;
    public override T Read(nint pointer) => read(pointer);
    public override void Write(nint pointer, T value, NativeArena arena) => write(pointer, value, arena);
}

public sealed class ReferenceCodec<T>(int size, Func<nint, T> read, Action<nint, T, NativeArena> write) : Codec<T> where T : class
{
    public override int Size => size;
    public override T Read(nint pointer) => read(pointer);
    public override void Write(nint pointer, T value, NativeArena arena)
    {
        ArgumentNullException.ThrowIfNull(value);
        arena.Enter(value);
        try { write(pointer, value, arena); }
        finally { arena.Leave(value); }
    }
}

public static unsafe class Codecs
{
    public static T Clone<T>(Codec<T> codec, T value)
    {
        using var arena = new NativeArena();
        var p = arena.Allocate(codec.Size);
        codec.Write(p, value, arena);
        return codec.Read(p);
    }
    private sealed class BlittableCodec<T> : Codec<T> where T : unmanaged
    {
        public override int Size => sizeof(T);
        public override T Read(nint pointer) => *(T*)pointer;
        public override void Write(nint pointer, T value, NativeArena arena) => *(T*)pointer = value;
    }
    public static Codec<byte> Byte { get; } = new BlittableCodec<byte>();
    public static Codec<sbyte> SByte { get; } = new BlittableCodec<sbyte>();
    public static Codec<short> Int16 { get; } = new BlittableCodec<short>();
    public static Codec<ushort> UInt16 { get; } = new BlittableCodec<ushort>();
    public static Codec<int> Int32 { get; } = new BlittableCodec<int>();
    public static Codec<uint> UInt32 { get; } = new BlittableCodec<uint>();
    public static Codec<long> Int64 { get; } = new BlittableCodec<long>();
    public static Codec<ulong> UInt64 { get; } = new BlittableCodec<ulong>();
    public static Codec<float> Float32 { get; } = new BlittableCodec<float>();
    public static Codec<double> Float64 { get; } = new BlittableCodec<double>();
    public static Codec<bool> Boolean { get; } = new DelegateCodec<bool>(1, p => *(byte*)p != 0, (p, v, _) => *(byte*)p = v ? (byte)1 : (byte)0);
    public static Codec<char> Char { get; } = new DelegateCodec<char>(1, p => (char)*(byte*)p, (p, v, _) =>
    {
        if (v > 255) throw new ArgumentOutOfRangeException(nameof(v), "IDL char is an 8-bit value.");
        *(byte*)p = (byte)v;
    });
    public static Codec<T> Enum<T>() where T : struct, Enum => new DelegateCodec<T>(4,
        p => (T)System.Enum.ToObject(typeof(T), *(int*)p), (p, v, _) => *(int*)p = Convert.ToInt32(v));
    public static Codec<T> Bitmask<T>(int size) where T : struct, Enum => new DelegateCodec<T>(size,
        p => (T)System.Enum.ToObject(typeof(T), size switch { 1 => *(byte*)p, 2 => *(ushort*)p, 4 => *(uint*)p, 8 => *(ulong*)p, _ => throw new ArgumentOutOfRangeException(nameof(size)) }),
        (p, v, _) => { ulong n = Convert.ToUInt64(v); switch (size) { case 1: *(byte*)p = checked((byte)n); break; case 2: *(ushort*)p = checked((ushort)n); break; case 4: *(uint*)p = checked((uint)n); break; case 8: *(ulong*)p = n; break; default: throw new ArgumentOutOfRangeException(nameof(size)); } });
}

public sealed class OptionalReferenceCodec<T>(Codec<T> element, bool directPointer = false) : Codec<T?> where T : class
{
    public override int Size => nint.Size;
    public override T? Read(nint pointer)
    {
        var target = Marshal.ReadIntPtr(pointer);
        return target == 0 ? null : element.Read(directPointer ? pointer : target);
    }
    public override void Write(nint pointer, T? value, NativeArena arena)
    {
        if (value is null) { Marshal.WriteIntPtr(pointer, 0); return; }
        if (directPointer) { element.Write(pointer, value, arena); return; }
        var target = arena.Allocate(element.Size);
        element.Write(target, value, arena);
        Marshal.WriteIntPtr(pointer, target);
    }
}

public sealed class OptionalValueCodec<T>(Codec<T> element) : Codec<T?> where T : struct
{
    public override int Size => nint.Size;
    public override T? Read(nint pointer)
    {
        var target = Marshal.ReadIntPtr(pointer);
        return target == 0 ? null : element.Read(target);
    }
    public override void Write(nint pointer, T? value, NativeArena arena)
    {
        if (value is null) { Marshal.WriteIntPtr(pointer, 0); return; }
        var target = arena.Allocate(element.Size);
        element.Write(target, value.Value, arena);
        Marshal.WriteIntPtr(pointer, target);
    }
}

public sealed unsafe class StringCodec(int bound = 0) : Codec<string>
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public override int Size => bound > 0 ? checked(bound + 1) : nint.Size;
    public override string Read(nint pointer) => Marshal.PtrToStringUTF8(bound > 0 ? pointer : Marshal.ReadIntPtr(pointer)) ?? string.Empty;
    public override void Write(nint pointer, string value, NativeArena arena)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0')) throw new ArgumentException("IDL strings cannot contain embedded NUL.", nameof(value));
        var bytes = Utf8.GetBytes(value);
        if (bound > 0 && bytes.Length > bound) throw new ArgumentOutOfRangeException(nameof(value), $"String exceeds its {bound}-byte IDL bound.");
        var target = bound > 0 ? pointer : arena.Allocate(checked(bytes.Length + 1));
        bytes.CopyTo(new Span<byte>((void*)target, bytes.Length));
        ((byte*)target)[bytes.Length] = 0;
        if (bound == 0) Marshal.WriteIntPtr(pointer, target);
    }
}

public sealed class SequenceCodec<T>(Codec<T> element, int bound = 0) : Codec<ISequence<T>>
{
    public override int Size => checked((int)Native.cs_sequence_size());
    public override ISequence<T> Read(nint pointer)
    {
        var count = checked((int)Native.cs_sequence_length(pointer));
        var buffer = Native.cs_sequence_buffer(pointer);
        ISequence<T> result = new Sequence<T> { Capacity = count };
        for (int i = 0; i < count; i++) result.Add(element.Read(buffer + checked(i * element.Size)));
        return result;
    }
    public override void Write(nint pointer, ISequence<T> value, NativeArena arena)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (bound > 0 && value.Count > bound) throw new ArgumentOutOfRangeException(nameof(value), $"Sequence exceeds its {bound}-element IDL bound.");
        var buffer = value.Count == 0 ? 0 : arena.Allocate(checked(value.Count * element.Size));
        for (int i = 0; i < value.Count; i++) element.Write(buffer + checked(i * element.Size), value[i], arena);
        Native.cs_sequence_set(pointer, (uint)value.Count, buffer);
    }
}

public sealed class ArrayCodec<TArray, TElement>(Codec<TElement> element, params int[] dimensions) : Codec<TArray> where TArray : class
{
    private readonly int count = dimensions.Aggregate(1, checked((a, b) => a * b));
    public override int Size => checked(count * element.Size);
    public override TArray Read(nint pointer)
    {
        var result = Array.CreateInstance(typeof(TElement), dimensions);
        for (int i = 0; i < count; i++) result.SetValue(element.Read(pointer + checked(i * element.Size)), Indices(i));
        return (TArray)(object)result;
    }
    public override void Write(nint pointer, TArray value, NativeArena arena)
    {
        Validate(value, dimensions);
        var array = (Array)(object)value;
        for (int i = 0; i < count; i++) element.Write(pointer + checked(i * element.Size), (TElement)array.GetValue(Indices(i))!, arena);
    }
    private int[] Indices(int index)
    {
        var result = new int[dimensions.Length];
        for (int i = dimensions.Length - 1; i >= 0; i--) { result[i] = index % dimensions[i]; index /= dimensions[i]; }
        return result;
    }
    public static void Validate(TArray value, params int[] shape)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is not Array array || array.Rank != shape.Length || shape.Where((n, i) => array.GetLength(i) != n || array.GetLowerBound(i) != 0).Any())
            throw new ArgumentOutOfRangeException(nameof(value), "Array dimensions must match its IDL declaration.");
    }
    public static TArray Create(Func<TElement> create, params int[] shape)
    {
        var array = Array.CreateInstance(typeof(TElement), shape);
        int length = shape.Aggregate(1, (a, b) => checked(a * b));
        var indices = new int[shape.Length];
        for (int i = 0; i < length; i++)
        {
            int index = i;
            for (int j = shape.Length - 1; j >= 0; j--) { indices[j] = index % shape[j]; index /= shape[j]; }
            array.SetValue(create(), indices);
        }
        return (TArray)(object)array;
    }
}

/// <summary>Structural equality used by generated data classes.</summary>
public static class DdsValue
{
    public static bool Equal(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.GetType() != right.GetType()) return false;
        if (left is string) return left.Equals(right);
        if (left is IEnumerable a && right is IEnumerable b)
        {
            var x = a.GetEnumerator(); var y = b.GetEnumerator();
            try
            {
                while (x.MoveNext()) if (!y.MoveNext() || !Equal(x.Current, y.Current)) return false;
                return !y.MoveNext();
            }
            finally { (x as IDisposable)?.Dispose(); (y as IDisposable)?.Dispose(); }
        }
        return left.Equals(right);
    }
    public static int Hash(object? value)
    {
        if (value is IEnumerable values and not string)
        {
            var hash = new HashCode(); foreach (var item in values) hash.Add(Hash(item)); return hash.ToHashCode();
        }
        return value?.GetHashCode() ?? 0;
    }
}
