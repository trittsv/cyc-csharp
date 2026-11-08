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
using Org.Eclipse.CycloneDDS;
using Test;

internal static class Program
{
    private static int passed, failed, topicId;
    private static string? filter;
    private const uint Domain = 41;
    private static readonly Qos Reliable = new()
    {
        Reliability = new(ReliabilityKind.Reliable, Duration.FromSeconds(3)),
        History = new(HistoryKind.KeepAll)
    };

    private static int Main(string[] args)
    {
        filter = args.FirstOrDefault();
        Run("Duration.zero-and-infinity", () => { Equal(0L, Duration.Zero.Nanoseconds); Equal(long.MaxValue, Duration.Infinite.Nanoseconds); });
        Run("Duration.conversions", () => Equal(Duration.FromSeconds(2), Duration.FromTimeSpan(TimeSpan.FromSeconds(2))));
        Run("Duration.negative-rejected", () => Throws<ArgumentOutOfRangeException>(() => new Duration(-1)));
        Run("Duration.overflow-rejected", () => Throws<OverflowException>(() => Duration.FromSeconds(long.MaxValue)));
        Run("Duration.arithmetic", () => Equal(Duration.FromMilliseconds(1500), Duration.FromSeconds(2) - Duration.FromMilliseconds(500)));
        Run("Duration.underflow-rejected", () => Throws<ArgumentOutOfRangeException>(() => _ = Duration.Zero - Duration.FromSeconds(1)));
        Run("Time.unix-epoch", () => Equal(0L, Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch).Nanoseconds));
        Run("Time.roundtrip", () => { var t = DateTimeOffset.UtcNow; Equal(t, Timestamp.FromDateTimeOffset(t).ToDateTimeOffset()); });
        Run("Generated.constants", () => { Equal(3, ARRAY_SIZE.Value); Equal("Hello, DDS!", GREETING.Value); });
        Run("Generated.defaults", () =>
        {
            var v = new Containers();
            Equal(string.Empty, v.label); Equal(0, v.numbers.Count); Equal(3, v.points.Length);
            True(v.points.All(p => p is not null)); Equal(2, v.matrix.GetLength(0)); Equal(3, v.matrix.GetLength(1));
        });
        Run("Generated.constructor-and-copy", () =>
        {
            var a = new Message(1, "one", 2); var b = new Message(a);
            True(a.Equals(b)); Equal(a.GetHashCode(), b.GetHashCode()); b.text = "changed"; Equal("one", a.text); True(!a.Equals(b));
        });
        Run("Generated.deep-copy-containers", () =>
        {
            var a = MakeContainers(); var b = new Containers(a);
            True(a.Equals(b)); Equal(a.GetHashCode(), b.GetHashCode());
            b.points[0].x = 100; b.words[0] = "different"; b.matrix[0, 0] = 22; b.grid[0][0] = 23;
            Equal(1.5, a.points[0].x); Equal("one", a.words[0]); Equal(1.0, a.matrix[0, 0]); Equal(5, a.grid[0][0]);
        });
        Run("Bounded.utf8-string", () =>
        {
            var v = MakeContainers(); v.label = new string('ä', 8); _ = Containers.TypeSupport.Clone(v);
            v.label += "x"; Throws<ArgumentOutOfRangeException>(() => Containers.TypeSupport.Clone(v));
        });
        Run("Bounded.sequence", () =>
        {
            var v = new Containers(); v.numbers.AddRange(Enumerable.Range(0, 9));
            Throws<ArgumentOutOfRangeException>(() => Containers.TypeSupport.Clone(v));
        });
        Run("Bounded.array", () =>
        {
            var v = new Containers();
            Throws<ArgumentOutOfRangeException>(() => v.points = new Point[2]);
        });
        Run("Bounded.multidimensional-array", () =>
        {
            var v = new Containers();
            Throws<ArgumentOutOfRangeException>(() => v.matrix = new double[3, 2]);
        });
        Run("Generated.embedded-nul-rejected", () => Throws<ArgumentException>(() => Message.TypeSupport.Clone(new() { text = "a\0b" })));
        Run("Generated.char-overflow-rejected", () => Throws<ArgumentOutOfRangeException>(() => Primitives.TypeSupport.Clone(new() { letter = '€' })));
        Run("Generated.optional-null", () => { var v = Optional.TypeSupport.Clone(new()); True(v.number is null && v.text is null && v.point is null); });
        Run("Generated.optional-values", () =>
        {
            var a = new Optional { id = 7, number = 19, text = "optional", point = new() { x = 2, y = 3 } };
            var b = new Optional(a); True(a.Equals(b)); True(!ReferenceEquals(a.point, b.point));
        });
        Run("Generated.union-discriminator", () =>
        {
            var choice = new Choice { number = 12 }; Equal(1, choice.Discriminator); Equal(12, choice.number);
            Throws<InvalidOperationException>(() => _ = choice.text);
            choice.Settext("third", 3); Equal(3, choice.Discriminator); Equal("third", choice.text);
            Throws<ArgumentException>(() => choice.Settext("invalid", 7));
        });
        Run("Generated.union-default", () => { var a = new Choice { fallback = 2.5 }; True(a.Discriminator != 1); True(a.Equals(new Choice(a))); });
        Run("Generated.union-sequence", () => { var a = new Choice(); a.Setnumbers([1, 2, 3]); var b = new Choice(a); True(a.Equals(b)); b.numbers[0] = 9; Equal(1, a.numbers[0]); });
        Run("Generated.recursive-copy", () =>
        {
            var v = new Recursive { id = 1, child = new() { id = 2 } }; v.children.Add(new() { id = 3 });
            var copy = new Recursive(v); True(v.Equals(copy)); True(!ReferenceEquals(v.child, copy.child));
        });
        Run("Generated.cyclic-sample-rejected", () => { var v = new Recursive(); v.child = v; Throws<ArgumentException>(() => Recursive.TypeSupport.Clone(v)); });
        Run("DomainParticipant.properties", () => { using var p = Participant(); Equal(Domain, p.DomainId); True(p.InstanceHandle != 0); });
        Run("DomainParticipant.parent-close", () =>
        {
            var p = Participant(); var publisher = p.CreatePublisher(); var subscriber = p.CreateSubscriber();
            var topic = p.CreateTopic(Name(), Message.TypeSupport); var writer = publisher.CreateWriter(topic); var reader = subscriber.CreateReader(topic);
            p.Dispose(); True(publisher.IsDisposed && subscriber.IsDisposed && topic.IsDisposed && writer.IsDisposed && reader.IsDisposed);
            Throws<ObjectDisposedException>(() => writer.Write(new())); Throws<ObjectDisposedException>(() => reader.Take()); p.Dispose();
        });
        Run("Topic.name-and-type", () => { using var p = Participant(); using var t = p.CreateTopic("TopicProperties", Message.TypeSupport); Equal("TopicProperties", t.Name); Equal("Test::Message", t.TypeName); });
        Run("Topic.invalid-name", () => { using var p = Participant(); Throws<ArgumentException>(() => p.CreateTopic("", Message.TypeSupport)); });
        Run("Topic.cross-participant-rejected", () =>
        {
            using var p = Participant(); using var other = Participant(); using var t = other.CreateTopic(Name(), Message.TypeSupport);
            Throws<ArgumentException>(() => p.CreateWriter(t)); Throws<ArgumentException>(() => p.CreateReader(t));
        });
        Run("PublisherSubscriber.entities", () =>
        {
            using var p = Participant(); using var pub = p.CreatePublisher(); using var sub = p.CreateSubscriber();
            using var t = p.CreateTopic(Name(), Message.TypeSupport); using var w = pub.CreateWriter(t, Reliable); using var r = sub.CreateReader(t, Reliable);
            Match(w, r); w.Write(new(1, "publisher", 3)); Equal("publisher", TakeOne(r).Data.text);
        });
        Run("Qos.defaults", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            Equal(ReliabilityKind.Reliable, w.GetCoreQos().Reliability!.Kind); Equal(HistoryKind.KeepAll, r.GetCoreQos().History!.Kind);
            Equal(DurabilityKind.Volatile, w.GetCoreQos().Durability);
        }));
        Run("Qos.invalid-history", () => { using var p = Participant(); Throws<ArgumentOutOfRangeException>(() => p.CreatePublisher(new() { History = new(HistoryKind.KeepLast, 0) })); });
        Run("Qos.invalid-resource-limits", () => { using var p = Participant(); Throws<ArgumentOutOfRangeException>(() => p.CreateSubscriber(new() { ResourceLimits = new(0) })); });
        Run("Qos.invalid-enum", () => { using var p = Participant(); Throws<ArgumentOutOfRangeException>(() => p.CreatePublisher(new() { Durability = (DurabilityKind)100 })); });
        Run("DataWriterReader.message-roundtrip", () => RoundTrip(Message.TypeSupport, new Message(1, "Grüße 🌍", 42)));
        Run("DataWriterReader.primitive-extremes", () => RoundTrip(Primitives.TypeSupport, new Primitives
        {
            id = 1, flag = true, letter = 'z', oct = 255, i8 = sbyte.MinValue, u8 = byte.MaxValue,
            i16 = short.MinValue, u16 = ushort.MaxValue, i32 = int.MinValue, u32 = uint.MaxValue,
            i64 = long.MinValue, u64 = ulong.MaxValue, f32 = 1.25f, f64 = -123.5,
            color = Color.BLUE, permissions = Permissions.ADMIN | Permissions.READ
        }));
        Run("DataWriterReader.containers", () => RoundTrip(Containers.TypeSupport, MakeContainers()));
        Run("DataWriterReader.optionals", () => RoundTrip(Optional.TypeSupport, new Optional { id = 1, number = 17, text = "hello", point = new() { x = 3.2 } }));
        Run("DataWriterReader.optional-null", () => RoundTrip(Optional.TypeSupport, new Optional { id = 1 }));
        Run("DataWriterReader.mutable-xcdr", () => { var v = new Mutable { id = 2, text = "mutable" }; v.numbers.AddRange([1, 2, 3]); RoundTrip(Mutable.TypeSupport, v); });
        Run("DataWriterReader.keyless", () => RoundTrip(Keyless.TypeSupport, new Keyless { value = 73 }));
        Run("DataWriterReader.union-number", () => RoundTrip(Choice.TypeSupport, new Choice { number = 19 }));
        Run("DataWriterReader.union-string", () => { var v = new Choice(); v.Settext("union 🌍", 3); RoundTrip(Choice.TypeSupport, v); });
        Run("DataWriterReader.union-default", () => RoundTrip(Choice.TypeSupport, new Choice { fallback = 3.14 }));
        Run("DataWriterReader.union-sequence", () => { var v = new Choice(); v.Setnumbers([1, 2, 3]); RoundTrip(Choice.TypeSupport, v); });
        Run("DataWriterReader.enum-union", () => RoundTrip(ColorChoice.TypeSupport, new ColorChoice { green = "green" }));
        Run("DataWriterReader.bool-union", () => RoundTrip(BoolChoice.TypeSupport, new BoolChoice { yes = 42 }));
        Run("DataWriterReader.recursive", () => { var v = new Recursive { id = 1, child = new() { id = 2 } }; v.children.Add(new() { id = 3 }); RoundTrip(Recursive.TypeSupport, v); });
        Run("DataReader.read-preserves-take-removes", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            w.Write(new(1, "read", 2)); WaitData(r); Equal(1, r.Read().Count); Equal(1, r.Read().Count); Equal(1, r.Take().Count); Equal(0, r.Take().Count);
        }));
        Run("DataReader.sample-state", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            w.Write(new(1, "state", 2)); WaitData(r); var first = r.Read(state: DataState.NewData);
            Equal(SampleState.NotRead, first.Single().Info.SampleState); Equal(ViewState.New, first.Single().Info.ViewState);
            Equal(0, r.Read(state: DataState.NewData).Count); Equal(SampleState.Read, r.Take().Single().Info.SampleState);
        }));
        Run("DataReader.maximum", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            for (int i = 0; i < 10; i++) w.Write(new(i, "limit", i));
            WaitUntil(() => r.Read().Count == 10); Equal(3, r.Take(3).Count); Equal(7, r.Take().Count);
            Throws<ArgumentOutOfRangeException>(() => r.Take(0));
        }));
        Run("SampleInfo.timestamp-and-handles", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            var ts = new Timestamp(1_700_000_000_123_456_789); w.Write(new(1, "timestamp", 2), ts); var s = TakeOne(r);
            Equal(ts, s.Info.SourceTimestamp); True(s.Info.ValidData); True(s.Info.InstanceHandle != 0); Equal(w.InstanceHandle, s.Info.PublicationHandle);
        }));
        Run("DataWriter.register-lookup-getkey", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            var key = new Message(37, "not a key", 2); var h = w.RegisterInstance(key);
            True(h != 0); Equal(h, w.LookupInstance(key)); Equal(37, w.GetKey(h).id);
        }));
        Run("DataReader.instance-selection", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            w.Write(new(1, "one", 1)); w.Write(new(2, "two", 2)); WaitUntil(() => r.Read().Count == 2);
            var handle = r.Read().Single(s => s.Data.id == 1).Info.InstanceHandle;
            Equal(1, r.Take(instanceHandle: handle).Single().Data.id); Equal(2, r.Take().Single().Data.id);
        }));
        Run("DataWriter.dispose-notification", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            var value = new Message(1, "dispose", 2); w.Write(value); _ = TakeOne(r); w.DisposeInstance(value);
            var s = TakeOne(r, validOnly: false); True(!s.Info.ValidData); Equal(InstanceState.NotAliveDisposed, s.Info.InstanceState); Equal(1, s.Data.id);
        }));
        Run("DataWriter.unregister-no-writers", () =>
        {
            using var p = Participant(); using var t = p.CreateTopic(Name(), Message.TypeSupport);
            using var r = p.CreateReader(t, Reliable); using var w = p.CreateWriter(t, Reliable with { AutodisposeUnregisteredInstances = false });
            Match(w, r); var value = new Message(1, "unregister", 2); w.Write(value); _ = TakeOne(r); w.UnregisterInstance(value);
            Equal(InstanceState.NotAliveNoWriters, TakeOne(r, false).Info.InstanceState);
        });
        Run("DataWriter.write-dispose", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            w.WriteDispose(new(1, "write-dispose", 2)); var value = TakeOne(r); True(value.Info.ValidData); Equal(InstanceState.NotAliveDisposed, value.Info.InstanceState);
        }));
        Run("DataWriter.wait-for-acks", () => Endpoints(Message.TypeSupport, (p, t, w, r) => { w.Write(new(1, "ack", 1)); w.WaitForAcknowledgments(Duration.FromSeconds(3)); }));
        Run("DataWriter.batched-flush", () =>
        {
            using var p = Participant(); using var t = p.CreateTopic(Name(), Message.TypeSupport);
            using var r = p.CreateReader(t, Reliable); using var w = p.CreateWriter(t, Reliable with { WriterBatching = true });
            Match(w, r);
            for (int i = 0; i < 20; i++) w.Write(new(i, "batch", i));
            w.Flush(); w.WaitForAcknowledgments(Duration.FromSeconds(3));
            WaitUntil(() => r.Read().Count == 20);
            var samples = r.Take(); Equal(20, samples.Count); True(samples.Select(s => s.Data.id).Order().SequenceEqual(Enumerable.Range(0, 20)));
        });
        Run("DataWriter.flush-after-close", () =>
        {
            var p = Participant(); var t = p.CreateTopic(Name(), Message.TypeSupport); var w = p.CreateWriter(t);
            p.Dispose(); Throws<ObjectDisposedException>(() => w.Flush());
        });
        Run("Discovery.matched-endpoints", () => Endpoints(Message.TypeSupport, (p, t, w, r) => { True(w.MatchedSubscriptions.Contains(r.InstanceHandle)); True(r.MatchedPublications.Contains(w.InstanceHandle)); }));
        Run("Discovery.no-matches", () =>
        {
            using var p = Participant(); using var t = p.CreateTopic(Name(), Message.TypeSupport); using var w = p.CreateWriter(t);
            Equal(0, w.MatchedSubscriptions.Count); using var other = p.CreateTopic(Name(), Message.TypeSupport); using var r = p.CreateReader(other); Equal(0, r.MatchedPublications.Count);
        });
        Run("Condition.guard-trigger-reset", () =>
        {
            using var p = Participant(); using var c = p.CreateGuardCondition(); True(!c.Triggered); c.Set(true); True(c.Triggered); c.Set(false); True(!c.Triggered);
        });
        Run("WaitSet.guard-and-timeout", () =>
        {
            using var p = Participant(); using var c = p.CreateGuardCondition(); using var ws = p.CreateWaitSet(); ws.Attach(c);
            Equal(0, ws.Wait(Duration.FromMilliseconds(5)).Count); c.Set(true); Equal(c, ws.Wait(Duration.Zero).Single()); ws.Detach(c); Equal(0, ws.Wait(Duration.Zero).Count);
        });
        Run("WaitSet.concurrent-guard", () =>
        {
            using var p = Participant(); using var c = p.CreateGuardCondition(); using var ws = p.CreateWaitSet(); ws.Attach(c);
            var wait = Task.Run(() => ws.Wait(Duration.FromSeconds(3))); Thread.Sleep(30); c.Set(true); True(wait.Wait(2000)); Equal(c, wait.Result.Single());
        });
        Run("WaitSet.cancellation", () =>
        {
            using var p = Participant(); using var ws = p.CreateWaitSet(); using var cancel = new CancellationTokenSource(30);
            Throws<OperationCanceledException>(() => ws.Wait(Duration.Infinite, cancel.Token));
        });
        Run("WaitSet.duplicate-attachment", () =>
        {
            using var p = Participant(); using var c = p.CreateGuardCondition(); using var ws = p.CreateWaitSet(); ws.Attach(c); Throws<ArgumentException>(() => ws.Attach(c));
        });
        Run("Condition.read-condition", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            using var condition = r.CreateReadCondition(); using var ws = p.CreateWaitSet(); ws.Attach(condition);
            True(!condition.Triggered); w.Write(new(1, "condition", 2)); Equal(condition, ws.Wait(Duration.FromSeconds(3)).Single());
            Equal("condition", r.Take(condition).Single().Data.text); True(!condition.Triggered);
        }));
        Run("Query.filtered-take", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            using var query = r.CreateQueryCondition(value => value.value >= 10, DataState.Any);
            using var ws = p.CreateWaitSet(); ws.Attach(query);
            w.Write(new(1, "low", 1)); w.Write(new(2, "high", 19));
            Equal(query, ws.Wait(Duration.FromSeconds(3)).Single());
            Equal("high", r.Take(query).Single().Data.text); Equal("low", r.Take().Single().Data.text);
        }));
        Run("Query.exceptions-stay-managed", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            using var query = r.CreateQueryCondition(_ => throw new FormatException("filter error"));
            w.Write(new(1, "filter", 1)); WaitData(r);
            Throws<InvalidOperationException>(() => query.ThrowIfFilterFailed());
        }));
        Run("Listener.native-data-available", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            using var received = new ManualResetEventSlim(); Message? value = null;
            using var listener = r.Listen(reader => { var sample = reader.Take().FirstOrDefault(x => x.Info.ValidData); if (sample is not null) { value = sample.Data; received.Set(); } });
            w.Write(new(1, "listener", 2)); True(received.Wait(3000)); Equal("listener", value!.text); True(listener.LastException is null);
        }));
        Run("Listener.callback-errors-captured", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            using var listener = r.Listen(_ => throw new FormatException("listener error"));
            w.Write(new(1, "listener-error", 2)); WaitUntil(() => listener.LastException is not null); True(listener.LastException is FormatException);
        }));
        Run("Listener.stress-and-gc", () => Endpoints(Message.TypeSupport, (p, t, w, r) =>
        {
            int count = 0;
            using var listener = r.Listen(reader => Interlocked.Add(ref count, reader.Take().Count(s => s.Info.ValidData)));
            GC.Collect(); GC.WaitForPendingFinalizers();
            for (int i = 0; i < 100; i++) w.Write(new(i, "stress", i));
            WaitUntil(() => Volatile.Read(ref count) == 100); True(listener.LastException is null);
        }));
        Run("Entity.concurrent-parent-close", () =>
        {
            var p = Participant(); var t = p.CreateTopic(Name(), Message.TypeSupport); var w = p.CreateWriter(t);
            var writing = Task.Run(() => { try { for (int i = 0; i < 1000; i++) w.Write(new(i, "race", i)); } catch (ObjectDisposedException) { } });
            p.Dispose(); True(writing.Wait(3000)); True(w.IsDisposed);
        });
        Run("Regression.loan-return-on-decoding-failure", LoanFailure);
        Run("Interop.native-C-writer", () => Interop(true));
        Run("Interop.native-C-reader", () => Interop(false));
        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
        try { test(); passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}: {error}"); }
    }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}.");
    }
    private static DomainParticipant Participant() => new(Domain);
    private static string Name() => $"CSharp_{Environment.ProcessId}_{Interlocked.Increment(ref topicId)}";
    private static void WaitUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException(); Thread.Sleep(5); }
    }
    private static void Match<T>(DataWriter<T> writer, DataReader<T> reader) where T : class => WaitUntil(() => writer.MatchedSubscriptions.Count > 0 && reader.MatchedPublications.Count > 0);
    private static void WaitData<T>(DataReader<T> reader) where T : class
    {
        using var condition = reader.CreateReadCondition(DataState.Any); using var ws = reader.ParticipantForTests().CreateWaitSet();
        ws.Attach(condition); if (ws.Wait(Duration.FromSeconds(5)).Count == 0) throw new TimeoutException();
    }
    private static Sample<T> TakeOne<T>(DataReader<T> reader, bool validOnly = true) where T : class
    {
        Sample<T>? result = null;
        WaitUntil(() => { result = reader.Take().FirstOrDefault(s => !validOnly || s.Info.ValidData); return result is not null; });
        return result!;
    }
    private static void Endpoints<T>(TypeSupport<T> support, Action<DomainParticipant, Topic<T>, DataWriter<T>, DataReader<T>> test) where T : class
    {
        using var p = Participant(); using var t = p.CreateTopic(Name(), support); using var r = p.CreateReader(t, Reliable); using var w = p.CreateWriter(t, Reliable);
        Match(w, r); test(p, t, w, r);
    }
    private static void RoundTrip<T>(TypeSupport<T> support, T sample) where T : class => Endpoints(support, (p, t, w, r) => { w.Write(sample); True(sample.Equals(TakeOne(r).Data)); });
    private static Containers MakeContainers()
    {
        var value = new Containers { id = 4, label = "bounded", point = new() { x = 8, y = 9 } };
        value.numbers.AddRange([-1, 0, 1, int.MaxValue]); value.words.AddRange(["one", "Grüße", "🌍"]);
        value.points[0].x = 1.5; value.matrix[0, 0] = 1; value.matrix[1, 2] = 9;
        value.path.Add(new() { x = 2, y = 3 }); value.grid.Add(new Omg.Types.Sequence<int>([5, 6]));
        return value;
    }
    private static void LoanFailure() => Endpoints(Message.TypeSupport, (p, t, w, r) =>
    {
        var throwing = new DelegateCodec<Message>(Message.__Codec.Size, _ => throw new FormatException("decode failure"), (ptr, value, arena) => Message.__Codec.Write(ptr, value, arena));
        var support = new TypeSupport<Message>("Test::Message", Message.TypeSupport.Descriptor, throwing, () => new());
        using var otherTopic = p.CreateTopic(Name(), support); using var otherReader = p.CreateReader(otherTopic, Reliable); using var otherWriter = p.CreateWriter(otherTopic, Reliable);
        Match(otherWriter, otherReader); otherWriter.Write(new(1, "loan", 2));
        Throws<FormatException>(() => { WaitUntil(() => otherReader.Read().Count > 0); });
        // A loan left outstanding prevents normal destruction in Cyclone DDS.
        otherReader.Dispose();
    });
    private static void Interop(bool nativeWriter)
    {
        var directory = Path.GetFullPath(DdsRuntime.LibraryDirectory ?? throw new Exception("CYCLONEDDS_CSHARP_NATIVE is required for interoperability tests."));
        using var p = Participant(); var name = Name(); using var topic = p.CreateTopic(name, Message.TypeSupport);
        var start = new ProcessStartInfo(Path.Combine(directory, OperatingSystem.IsWindows() ? "c_interop.exe" : "c_interop")) { RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(nativeWriter ? "write" : "read"); start.ArgumentList.Add(Domain.ToString()); start.ArgumentList.Add(name);
        if (nativeWriter)
        {
            using var reader = p.CreateReader(topic, Reliable); using var process = Process.Start(start)!;
            try { var value = TakeOne(reader); Equal(71, value.Data.id); Equal("from native C", value.Data.text); Equal(314, value.Data.value); }
            finally { Finish(process); }
        }
        else
        {
            using var writer = p.CreateWriter(topic, Reliable); using var process = Process.Start(start)!;
            try { WaitUntil(() => writer.MatchedSubscriptions.Count > 0); writer.Write(new(72, "from managed C#", 271)); writer.WaitForAcknowledgments(Duration.FromSeconds(5)); Finish(process); }
            finally { if (!process.HasExited) process.Kill(true); }
        }
    }
    private static void Finish(Process process)
    {
        if (!process.WaitForExit(7000)) { process.Kill(true); throw new TimeoutException("Native peer did not exit."); }
        if (process.ExitCode != 0) throw new Exception($"Native peer exited {process.ExitCode}: {process.StandardError.ReadToEnd()}");
    }
}

internal static class EntityTestExtensions
{
    internal static DomainParticipant ParticipantForTests(this Entity entity)
    {
        while (entity is not DomainParticipant) entity = entity.Parent!;
        return (DomainParticipant)entity;
    }
}
