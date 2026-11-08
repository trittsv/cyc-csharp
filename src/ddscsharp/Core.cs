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

public enum ReturnCode
{
    Ok = 0, Error = 1, Unsupported = 2, BadParameter = 3, PreconditionNotMet = 4,
    OutOfResources = 5, NotEnabled = 6, ImmutablePolicy = 7, InconsistentPolicy = 8,
    AlreadyDeleted = 9, Timeout = 10, NoData = 11, IllegalOperation = 12, NotAllowedBySecurity = 13
}

public sealed class DdsException : Exception
{
    public ReturnCode Code { get; }
    internal DdsException(int code, string operation)
        : base($"{operation}: {Marshal.PtrToStringUTF8(Native.dds_strretcode(code))} ({code})") => Code = (ReturnCode)(-code);
    internal static int Check(int result, string operation)
    {
        if (result < 0) throw new DdsException(result, operation);
        return result;
    }
}

public readonly record struct Duration : IComparable<Duration>
{
    public long Nanoseconds { get; }
    public static Duration Zero => default;
    public static Duration Infinite => new(long.MaxValue);
    public Duration(long nanoseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nanoseconds);
        Nanoseconds = nanoseconds;
    }
    public static Duration FromSeconds(long seconds) => new(checked(seconds * 1_000_000_000));
    public static Duration FromMilliseconds(long milliseconds) => new(checked(milliseconds * 1_000_000));
    public static Duration FromTimeSpan(TimeSpan value) => new(checked(value.Ticks * 100));
    public TimeSpan ToTimeSpan() => Nanoseconds == long.MaxValue ? TimeSpan.MaxValue : TimeSpan.FromTicks(Nanoseconds / 100);
    public int CompareTo(Duration other) => Nanoseconds.CompareTo(other.Nanoseconds);
    public static Duration operator +(Duration left, Duration right) => new(checked(left.Nanoseconds + right.Nanoseconds));
    public static Duration operator -(Duration left, Duration right) => new(checked(left.Nanoseconds - right.Nanoseconds));
}

/// <summary>DDS time in nanoseconds since the Unix epoch.</summary>
public readonly record struct Timestamp(long Nanoseconds)
{
    public static Timestamp Now => FromDateTimeOffset(DateTimeOffset.UtcNow);
    public static Timestamp FromDateTimeOffset(DateTimeOffset time) => new(checked((time.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) * 100));
    public DateTimeOffset ToDateTimeOffset() => DateTimeOffset.UnixEpoch.AddTicks(Nanoseconds / 100);
}

[Flags] public enum SampleState : uint { Read = 1, NotRead = 2, Any = 3 }
[Flags] public enum ViewState : uint { New = 4, NotNew = 8, Any = 12 }
[Flags] public enum InstanceState : uint { Alive = 16, NotAliveDisposed = 32, NotAliveNoWriters = 64, Any = 112 }
public readonly record struct DataState(SampleState Sample, ViewState View, InstanceState Instance)
{
    public static DataState Any => new(SampleState.Any, ViewState.Any, InstanceState.Any);
    public static DataState NewData => new(SampleState.NotRead, ViewState.Any, InstanceState.Alive);
    internal uint Mask => (uint)Sample | (uint)View | (uint)Instance;
}

[Flags] public enum StatusMask : uint
{
    None = 0, InconsistentTopic = 1, OfferedDeadlineMissed = 2, RequestedDeadlineMissed = 4,
    OfferedIncompatibleQos = 8, RequestedIncompatibleQos = 16, SampleLost = 32,
    SampleRejected = 64, DataOnReaders = 128, DataAvailable = 256, LivelinessLost = 512,
    LivelinessChanged = 1024, PublicationMatched = 2048, SubscriptionMatched = 4096, All = 8191
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct SampleInfo
{
    public readonly SampleState SampleState;
    public readonly ViewState ViewState;
    public readonly InstanceState InstanceState;
    private readonly uint validData;
    private readonly long timestamp;
    public readonly ulong InstanceHandle, PublicationHandle;
    public readonly uint DisposedGenerationCount, NoWritersGenerationCount;
    public readonly uint SampleRank, GenerationRank, AbsoluteGenerationRank;
    public bool ValidData => validData != 0;
    public Timestamp SourceTimestamp => new(timestamp);
}

public sealed record Sample<T>(T Data, SampleInfo Info);
