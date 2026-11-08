# Cyclone DDS C#

Use [Eclipse Cyclone DDS](https://github.com/eclipse-cyclonedds/cyclonedds) from
C#: describe your data in IDL, let `dotnet build` generate the C# types, and
publish or receive typed DDS samples.

**`Org.Eclipse.CycloneDDS`** includes the managed API, native runtimes, IDL
compiler and automatic C# generation in one NuGet package.

The package uses version **11.0.1**, aligned with Cyclone DDS C and C++.
The runtime namespace and assembly are `Org.Eclipse.CycloneDDS` and
`Org.Eclipse.CycloneDDS.dll`.

## Quick start: IDL to a running C# application

You need the **.NET 9 SDK or newer** and a package containing the compiler tools
for your build host and native runtime libraries for the machine running your
application. CMake, a C compiler and a separately installed `idlc` are not needed
when using this package.

The package has not been published to NuGet yet. Obtain the locally built
`.nupkg` file from [artifacts/packages](artifacts/packages), or follow
[Build the package from source](#build-the-package-from-source) below. Add the
folder containing the package as a NuGet source, using its absolute path:

```sh
dotnet nuget add source /absolute/path/to/cyc-csharp/artifacts/packages --name cyclonedds-local
```

### 1. Create a project and reference the package

```sh
dotnet new console --name DdsHello --framework net9.0
cd DdsHello
```

Replace `DdsHello.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Org.Eclipse.CycloneDDS" Version="11.0.1" />
    <CycloneDDSIdl Include="Types/**/*.idl" />
  </ItemGroup>
</Project>
```

The package supplies both runtime and build support. Compiler tools stay in the
NuGet cache and are not copied into the published application. Projects without
`CycloneDDSIdl` items do not invoke the compiler. Keep the normal package reference
so runtime dependencies can flow to projects consuming your library.

### 2. Describe your data in IDL

Create a `Types` directory and save this as `Types/Hello.idl`:

```idl
module Demo {
  @final struct Message {
    @key long id;
    string text;
  };
};
```

This generates the C# class `Demo.Message`. The IDL module determines its
namespace, and `@key` identifies the field used to distinguish DDS instances.

### 3. Write normal C# application code

Replace `Program.cs` with this small publisher and subscriber in one process:

```csharp
using Org.Eclipse.CycloneDDS;
using Demo;

using var participant = new DomainParticipant(0);
using var topic = participant.CreateTopic("Hello", Message.TypeSupport);
var qos = new Qos
{
    Reliability = new(ReliabilityKind.Reliable, Duration.FromSeconds(3))
};
using var reader = participant.CreateReader(topic, qos);
using var writer = participant.CreateWriter(topic, qos);
using var condition = reader.CreateReadCondition();
using var waitset = participant.CreateWaitSet();
waitset.Attach(condition);

writer.Write(new Message { id = 1, text = "Hello from C#!" });

if (waitset.Wait(Duration.FromSeconds(3)).Count == 0)
    throw new TimeoutException("No DDS sample received.");

foreach (var sample in reader.Take())
    if (sample.Info.ValidData)
        Console.WriteLine($"{sample.Data.id}: {sample.Data.text}");
```

`Message.TypeSupport` is generated alongside the data class and connects it to
the DDS runtime. Dispose DDS entities with `using`; disposing a participant also
disposes its children.

### 4. Build and run

```sh
dotnet build
dotnet run --no-build
```

Expected output:

```text
1: Hello from C#!
```

The build restores the package, converts the IDLs to `.cs` files under
`obj/Debug/net9.0/cyclonedds`, and compiles them with your application. There is
no separate generator command or generated native library to manage. Edit your
IDL and build again; included files and compiler-option changes are tracked.
`dotnet clean` removes generated output.

Keep generated files under `obj` and out of source control. Include every IDL
that defines types in `CycloneDDSIdl`, including definition files referenced by
other IDLs, and use unique IDL basenames within a project. See the
[IDL build guide](src/buildtools/README.md) for include paths, preprocessor
definitions and other build options.

## Examples and next steps

The [examples overview](examples/README.md) covers separate publisher and
subscriber processes and all three example families ported from the C++ binding:

- [HelloWorld](examples/HelloWorld/README.md): send and receive a keyed message.
- [Throughput](examples/Throughput/README.md): listeners, wait sets, polling,
  batching and throughput measurements.
- [RoundTrip](examples/RoundTrip/README.md): Ping/Pong latency measurements,
  timestamp-preserving echoes and DDS-based termination.

The quick start uses one participant to keep setup small. Separate processes
communicate through matching domain IDs, topic names and compatible types and
QoS. The full HelloWorld example also shows discovery synchronization before
publishing. Cyclone's usual `CYCLONEDDS_URI` configuration is honored.

## Features

- Participants, publishers, subscribers, typed topics, data writers and readers.
- Read/take with state masks, maximum sample counts and instance selection.
- Sample metadata, source timestamps, registration, lookup, key recovery,
  disposal, unregistration, write-dispose, and acknowledgment waiting.
- QoS policies including reliability, durability, history, resource limits,
  partitions, deadlines, liveliness, lifespan, ownership and lifecycle settings.
- Read conditions, managed query predicates, guard conditions, wait sets,
  status masks, matched endpoint discovery, and data-available listeners.
- Generated primitive, enum, bitmask, struct, union, typedef, array, string,
  sequence, optional and recursive data types, within the documented limits.
- Native Cyclone DDS C descriptors, keys, type information and XCDR handling.
  The C#-only generator embeds XTypes metadata and managed sample layouts.
- Parent-owned entity lifetimes, serialized disposal, rooted callbacks,
  outgoing allocation arenas and exception-safe native loan return.

## Lifetime and callbacks

Dispose entities with `using`. Disposing a parent also disposes its children.
Read and take return managed copies; the native loan is returned before the call
completes, including when decoding throws. Invalid-data lifecycle notifications
carry meaningful key fields and `SampleInfo`; other data fields are unspecified.

Query predicates run on native DDS threads and must not call DDS operations.
Predicate exceptions are captured and surfaced by query access or
`ThrowIfFilterFailed()`. Data-available user callbacks run on the managed thread
pool; inspect the listener's `LastException` for callback failures. Disposing a
listener prevents new notifications; an already running callback can finish.

The first `DdsRuntime.LibraryDirectory` value must be set before using DDS or
generated metadata. The environment variable `CYCLONEDDS_CSHARP_NATIVE` initializes
it. Without it, native libraries are resolved through .NET's native probing rules.

## Status and standards

This is an initial implementation with working native DDS communication,
generated data types and integration tests. It does **not** yet provide the
full API or test coverage of the C++ binding. See
[implementation status](docs/STATUS.md) for coverage and remaining work.
Local verification is macOS ARM64; Linux and Windows builds are configured in
CI and require verification on those platforms.

The entity model follows the
[Cyclone DDS C++ binding](https://github.com/eclipse-cyclonedds/cyclonedds-cxx).
Generated data classes are based on
[OMG IDL4-to-C# 1.0](https://www.omg.org/spec/IDL4-CSHARP/1.0). The separate
DDS C# API is listed as work in progress by the
[DDS Foundation](https://www.dds-foundation.org/omg-dds-standard/).
See [standards and API decisions](docs/STANDARDS.md) for the mapping choices;
this project does not claim complete OMG conformance.

## Build the package from source

This section is for building this repository and producing the NuGet package
used above. Applications consuming the prebuilt package use the normal .NET build.

### Requirements

- .NET SDK 9.0, CMake 3.20 or later, a C11 compiler, and Python 3.9 or later.
- A shared build of Cyclone DDS 11.0.1 with `idlc` and development headers.
  The reproducible native revision is recorded in [DEPENDENCIES.md](docs/DEPENDENCIES.md).
- Local UDP sockets for the integration tests. No DDS server is required.

The initial verification platform is macOS ARM64. Linux and Windows builds are
included in CI configuration but are not claimed as verified until that CI runs.

### Build and test

Build the pinned native dependency, then the binding:

```sh
bash scripts/bootstrap-native.sh
cmake -S . -B build \
  -DCMAKE_PREFIX_PATH="$PWD/build/_deps/cyclonedds-install" \
  -DCMAKE_BUILD_TYPE=Release
cmake --build build --parallel
ctest --test-dir build --output-on-failure
```

If Cyclone DDS is already installed, set `CMAKE_PREFIX_PATH` to that installation
and set `CYCLONEDDS_SOURCE_DIR` to its matching pinned source tree. The common
bridge uses Cyclone’s internal typelib ABI, isolated in native code.
`BUILD_TESTING=OFF` disables the test builds and `BUILD_CSHARP_EXAMPLES=OFF`
disables the examples. For a multi-configuration generator, add `--config Release`
to the build command and `-C Release` to `ctest`.

The build compiles the native bridge and IDL backend, generates C# types,
builds the managed test executable, and builds HelloWorld, Throughput and RoundTrip.
CTest runs the examples in separate processes as well as native DDS
integration tests and independently compiles the upstream IDL corpus through the
C# compiler. The independent C interoperability peer uses the C backend. Documented rejections are reported separately from supported
fixtures; they are not counted as supported type-generation tests.

### Create the NuGet package

After building the native bridge and IDL backend, run:

```sh
python3 scripts/pack-package.py \
  --prefix build/_deps/cyclonedds-install --build build
```

This stages the native runtime, a relocatable IDL compiler and its dependencies,
and packs the managed runtime, internal MSBuild task and build integration into
`artifacts/packages/Org.Eclipse.CycloneDDS.11.0.1.nupkg`. The package version comes
from [VERSION](VERSION). There is no separate compiler-tools package.

The staging directory supports multiple platforms:

```text
build/package/
  runtimes/<target-rid>/native/
  tools/<build-host-rid>/native/
```

Each machine can run the script with `--stage-only` to produce its own RID
payloads. Combine those `runtimes` and `tools` directories, then pack all staged
platforms into one NuGet:

```sh
dotnet pack src/ddscsharp/Org.Eclipse.CycloneDDS.csproj -c Release -o artifacts/packages \
  -p:NativePackageDirectory="/absolute/path/to/combined-stage"
```

A pack requires both native runtime and compiler-tool payloads. Runtime selection
uses .NET's target RID; compiler selection uses the build host, independently.
CI builds all configured desktop, Android and iOS payloads in parallel, combines
them into one package, and verifies that package on every desktop host before
archiving it. See [CI and platform builds](docs/CI.md) for the matrix and gates. Packing does not publish
to NuGet or reserve an ID.

Verify the package in isolated consumers:

```sh
python3 scripts/test-package.py \
  --package artifacts/packages/Org.Eclipse.CycloneDDS.11.0.1.nupkg \
  --build build
```

The check restores one package from a local feed and uses only dotnet in PATH on
Unix. It checks includes, incremental builds, changed options, removed IDLs,
compiler failures, clean, publish, runtime-only builds and a schema library with
a transitive runtime dependency. The applications exchange typed DDS samples
through the packaged native runtime.

### Generate types inside a CMake build

Repository development can also use the CMake helper. It emits C# exclusively:

```cmake
include(cmake/GenerateCSharp.cmake)
cyclonedds_generate_csharp(TARGET telemetry_types FILE "${CMAKE_CURRENT_SOURCE_DIR}/Telemetry.idl")
```

Add the resulting `Telemetry.cs` to compilation. `INCLUDES` adds include paths
and `DEPENDS` tracks included IDLs. Generate included definition files too.
For application projects, use the package integration shown in the quick start.

## Source layout

```text
src/ddscsharp/          Managed binding and its native ABI bridge
src/ddscsharp/tests/    Managed/C integration tests and upstream IDL corpus
src/idlcsharp/         C# backend for the native IDL compiler
src/buildtools/        Internal MSBuild task and packaged build integration
examples/              Runnable examples
cmake/                 Type-generation build integration
scripts/               Dependency bootstrap and verification tools
docs/                  Standards, dependencies, status and upstream test inventory
```

## License

EPL-2.0 OR BSD-3-Clause, matching the Cyclone DDS C++ binding. See [LICENSE](LICENSE)
and [NOTICE.md](NOTICE.md). Upstream fixtures retain their original notices;
new sources use the same license-header wording with their own attribution.
