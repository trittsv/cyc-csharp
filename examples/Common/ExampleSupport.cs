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
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Org.Eclipse.CycloneDDS;

namespace Org.Eclipse.CycloneDDS.Examples;

internal sealed class ExampleCancellation : IDisposable
{
    private readonly CancellationTokenSource source = new();
    private readonly PosixSignalRegistration? termination;
    internal CancellationToken Token => source.Token;
    internal ExampleCancellation()
    {
        Console.CancelKeyPress += OnCancel;
        if (!OperatingSystem.IsWindows())
            termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; source.Cancel(); });
    }
    private void OnCancel(object? sender, ConsoleCancelEventArgs args) { args.Cancel = true; source.Cancel(); }
    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancel;
        termination?.Dispose();
        source.Dispose();
    }
}

internal static class ExampleSupport
{
    internal static int Run(Func<int> run)
    {
        try { return run(); }
        catch (OperationCanceledException) { Console.WriteLine("Cancelled."); return 0; }
        catch (ArgumentException error) { Console.Error.WriteLine(error.Message); return 2; }
        catch (Exception error) { Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}"); return 1; }
    }
    internal static DomainParticipant CreateParticipant()
    {
        var domain = Environment.GetEnvironmentVariable("CYCLONEDDS_EXAMPLE_DOMAIN_ID");
        return domain is null ? new DomainParticipant() : new DomainParticipant(uint.Parse(domain, NumberStyles.None, CultureInfo.InvariantCulture));
    }
    internal static int Number(string[] args, int index, int fallback, string name, int minimum = 0, int maximum = int.MaxValue)
    {
        if (index >= args.Length) return fallback;
        if (!int.TryParse(args[index], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
            throw new ArgumentException($"{name} must be an integer between {minimum} and {maximum}.");
        return value;
    }
    internal static void WaitForMatch<T>(DomainParticipant participant, DataWriter<T>? writer, DataReader<T>? reader,
        Duration timeout, CancellationToken cancellation) where T : class
    {
        using var waitset = participant.CreateWaitSet();
        if (writer is not null) { writer.SetStatusMask(StatusMask.PublicationMatched); waitset.Attach(writer); }
        if (reader is not null) { reader.SetStatusMask(StatusMask.SubscriptionMatched); waitset.Attach(reader); }
        bool Matched() => (writer is null || writer.MatchedSubscriptions.Count > 0) && (reader is null || reader.MatchedPublications.Count > 0);
        var started = Stopwatch.GetTimestamp();
        Console.WriteLine("Waiting for readers and writers to match...");
        while (!Matched())
        {
            cancellation.ThrowIfCancellationRequested();
            writer?.TakeStatus(StatusMask.PublicationMatched);
            reader?.TakeStatus(StatusMask.SubscriptionMatched);
            if (Matched()) break;
            var remaining = Remaining(timeout, started);
            if (remaining == Duration.Zero || waitset.Wait(remaining, cancellation).Count == 0)
                throw new TimeoutException("DDS peer discovery timed out.");
        }
        Console.WriteLine("Reader and writer matched.");
    }
    internal static Duration Remaining(Duration duration, long started)
    {
        if (duration == Duration.Infinite) return duration;
        var elapsed = checked(Stopwatch.GetElapsedTime(started).Ticks * 100);
        return new Duration(Math.Max(0, duration.Nanoseconds - elapsed));
    }
    internal static void WaitForReaderDisconnect<T>(DomainParticipant participant, DataWriter<T> writer,
        Duration timeout, CancellationToken cancellation) where T : class
    {
        using var waitset = participant.CreateWaitSet();
        writer.SetStatusMask(StatusMask.PublicationMatched);
        waitset.Attach(writer);
        long started = Stopwatch.GetTimestamp();
        while (writer.MatchedSubscriptions.Count > 0)
        {
            writer.TakeStatus(StatusMask.PublicationMatched);
            if (writer.MatchedSubscriptions.Count == 0) return;
            var remaining = Remaining(timeout, started);
            if (remaining == Duration.Zero || waitset.Wait(remaining, cancellation).Count == 0)
                throw new TimeoutException("The reader did not finish before the deadline.");
        }
    }
    internal static double Microseconds(long started, long completed) => (completed - started) * 1_000_000.0 / Stopwatch.Frequency;
    internal static void SleepUntil(long started, TimeSpan interval, CancellationToken cancellation)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var remaining = interval - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) return;
            if (cancellation.WaitHandle.WaitOne(remaining > TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : remaining))
                cancellation.ThrowIfCancellationRequested();
        }
    }
}
