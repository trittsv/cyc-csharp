# Implementation status

This is a working initial binding, **not full C++ feature or test parity**. The
reference C++ revision contains **558 test declarations across 39 C++ source
files**. Parameterized declarations can represent more executable cases.
[upstream-tests.json](upstream-tests.json) preserves every discovered declaration
and its source file. None is marked as ported merely because it was inventoried.

The managed integration executable currently contains **78 tests**, including
native DDS operations and independent C peers. The generator corpus contains
**22 unmodified upstream IDL fixtures**: **14 generate exclusively C# and compile**;
**8 have explicit, checked rejection expectations**. Expected rejection means
unsupported input is diagnosed consistently, not that the feature is implemented.

## Runtime coverage against the C++ suite

| Upstream source files | Current C# verification | Remaining coverage |
| --- | --- | --- |
| `DomainParticipant`, `Publisher`, `Subscriber`, `Topic` | Creation, parent relationships, recursive close, domain and instance handles, typed communication, invalid and cross-participant arguments | Default QoS management, entity enablement, all lookup/default-entity paths |
| `DataWriter`, `DataReader` | Write/read/take, bounds, state masks, instance selection, register/lookup/get-key, disposal/unregistration, timestamps, write-dispose, acknowledgments, batching/flush | Full operation overloads, read-next/next-instance, reader key lookup, all native error paths |
| `DataReaderSelector`, `DataReaderManipulatorSelector` | Explicit maximum, state, instance and condition arguments | C++ selector/manipulator API equivalents and all selector combinations |
| `SampleInfo` | Validity, states, timestamps, instance/publication handles, invalid-data lifecycle notifications | All rank and generation-count scenarios |
| `Condition`, `WaitSet`, `Query` | Read and guard conditions, attach/detach, trigger/reset, timeouts, cancellation, concurrent guard wake-up, typed query filtering and predicate errors | Full dispatch API, query parameter updates, concurrent attachment changes |
| `Listener`, `ListenerStress` | Native data-available notification, exception containment, GC rooting and 100-sample stress | Other status listeners, listener propagation/inheritance, full stress matrix |
| `EntityStatus` | Status masks and matched endpoint discovery; empty-match regression | Typed status structures/counters and every status transition |
| `Qos` | Core policy inspection, history/resource/enum validation, reliability and writer lifecycle behavior | All QoS comparison/conversion/default/inheritance cases and all policy getters |
| `Duration`, `Time` | Zero/infinity, conversions, arithmetic, negative values and overflow, timestamp conversion | Every C++ arithmetic/comparison edge case |
| `Exception` | Typed DDS return codes and managed validation failures | All exception mappings and unsupported native operations |
| `Bounded`, `DataModels`, `GeneratedEntities` | Bounded strings/sequences/arrays, defaults, all-values/copy constructors, deep copy and equality | Full generated entity traits and mapping variants |
| `CDRStreamer`, `Serdata`, `Conversions`, `KeyHash`, `ExtendedTypes`, `Regression` | Native descriptor-driven round trips, primitive extremes, containers, enums, bitmasks, unions, optional/recursive/mutable types; compiler corpus; C↔C# peers | Complete serialization vectors, key hash vectors, every regression and schema-evolution case |
| `DeferredDestruction` | Parent disposal during concurrent write, listener teardown, loan cleanup when decoding throws | Every deferred-lifetime/concurrency case |
| `Liveliness` | Native assertion operation exposed | Lease-expiration and full liveliness transition tests |
| `FindTopic`, `FindDataWriter`, `FindDataReader`, `TopicTypeDiscovery` | Endpoint match handle discovery | Public topic/entity lookup and remote type discovery APIs |
| `External` | Explicit unsupported-input diagnostics | `@external` mapping and shallow-copy semantics |
| `RecursiveIdlcxxProbes` | Recursive optional/sequence compilation and runtime samples; explicit recursive-array diagnostics | Full recursive/typedef/external acceptance and rejection matrix |
| `QosProvider` | Not implemented | XML QoS provider and tests |
| `Iceoryx` | Not implemented | Shared-memory-specific capabilities and tests |
| `Util` | Managed assertions, endpoint synchronization, fixture and native-peer helpers | C++ helper code itself is not ported |

These are behavior-level relationships, not one-to-one ports of the upstream
declarations. The machine-readable inventory remains the checklist for a full
port. C++-specific stream internals are not copied into C#: this binding uses
Cyclone's native C serialization backend, and needs independent compatibility
vectors rather than tests of C++ implementation details.

## IDL mapping limits

Working: modules, constants, typedef resolution, the supported C backend's scalar
types, strings, bounded strings, enums, bitmasks, structs, unions, rectangular
arrays, sequences (including nested sequences), optional values/references and
recursive structs through optional/sequence members. Final, appendable and
mutable descriptors are built by the shared runtime from embedded compiler XTypes metadata. C# names preserve IDL case.
Sequence properties use the OMG `ISequence<T>` shape, and array setters validate
their dimensions. Outgoing samples must be acyclic and at most 256 objects deep.

Not yet implemented: struct inheritance, `@external`, maps, wide strings/chars,
fixed-point/long-double marshalling, interfaces/CORBA mappings, custom annotation
generation, and `@csharp_mapping` options. Generated-name collisions currently
produce explicit errors instead of all of the standard's name-remapping rules.
Not every standardized annotation affecting managed defaults/ranges is applied.

Direct recursive arrays remain an explicit generator limitation; typedef arrays
of forward-declared types are rejected by the upstream parser. Both are recorded
in the corpus expectations.

The single `Org.Eclipse.CycloneDDS` package includes the runtime, idlc, the C#
backend and host-native dependencies with an MSBuild task. `<CycloneDDSIdl>` generates only `.cs` under
obj and adds it to compilation. Preprocessed input hashing tracks includes,
definitions, tool binaries and compiler options. Incremental builds, removed
inputs, failure handling, clean and publish are exercised in a package consumer.
The consumer requires no CMake, C compiler or generated native type libraries.

Native descriptors are built lazily when a topic is first created. Their safe
handles release the descriptor after its managed metadata becomes unreachable;
static generated metadata ordinarily retains them for the assembly lifetime.
The common bridge uses Cyclone’s pinned internal typelib API, so its binary must
match the native runtime revision and build configuration. No internal native
structure layout is exposed to generated C# or application code.


## Examples

All three C++ example families are ported under `examples/`: HelloWorld
publisher/subscriber, Throughput publisher/subscriber, and RoundTrip Ping/Pong.
The examples retain the upstream IDL, topic names and partitions, with managed
CLI role selection. Throughput supports listener, WaitSet, polling, batching,
bursts and reporting. RoundTrip supports both receive modes on each side,
source-timestamp-preserving echo, warm-up, latency/access percentiles, count/time
limits and dispose-based termination. See [the examples guide](../examples/README.md).
Separate-process example tests are included in CTest; an optional C++ binary
path enables direct interoperability checks against all six reference roles.

## Release work

1. Port the outstanding behavior from every upstream test file, including native
   wire vectors, status transitions, discovery and schema evolution.
2. Finish the IDL mapping gaps and replace checked rejection expectations with
   managed compilation plus runtime tests as each feature lands.
3. Add every DDS QoS policy/getter, typed status listener, discovery operation,
   QoS provider and applicable shared-memory feature.
4. Run Linux/Windows/macOS CI and native memory/concurrency instrumentation;
   current successful local verification is macOS ARM64.
5. Run the configured multi-platform packaging CI on GitHub, add Android device
   tests, XML API documentation and release compatibility guarantees.

The current NuGet metadata is `Org.Eclipse.CycloneDDS`, version
`11.0.1`. IDL build support is bundled in that package; the internal MSBuild
task project does not produce its own NuGet. Local packing is supported; no
remote publishing is performed.
