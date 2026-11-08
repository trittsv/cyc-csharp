# C# examples

These are C# ports of **every example** in the Cyclone DDS C++ binding at
[`c185b86b6b689f8ae30234f583979b049c5dfab3`](https://github.com/eclipse-cyclonedds/cyclonedds-cxx/tree/c185b86b6b689f8ae30234f583979b049c5dfab3/examples).
They use the shared binding version **11.0.1**. Each C# application selects a role
with its first argument rather than requiring two separate executables.

| C++ example and roles | C# application | Guide |
| --- | --- | --- |
| `helloworld`: publisher, subscriber | `HelloWorld pub`, `HelloWorld sub` | [HelloWorld](HelloWorld/README.md) |
| `throughput`: publisher, subscriber | `Throughput pub`, `Throughput sub` | [Throughput](Throughput/README.md) |
| `roundtrip`: ping, pong | `RoundTrip ping`, `RoundTrip pong` | [RoundTrip](RoundTrip/README.md) |

## Build and run

From the repository root, after building the native dependency as described in the
[main README](../README.md):

```sh
cmake -S . -B build -DCMAKE_PREFIX_PATH="$PWD/build/_deps/cyclonedds-install" -DCMAKE_BUILD_TYPE=Release
cmake --build build --parallel
export CYCLONEDDS_CSHARP_NATIVE="$PWD/build/lib"
```

Use two terminals to run the roles. For example:

```sh
dotnet examples/HelloWorld/bin/Release/net9.0/HelloWorld.dll sub
dotnet examples/HelloWorld/bin/Release/net9.0/HelloWorld.dll pub
```

On Windows, use `$env:CYCLONEDDS_CSHARP_NATIVE` in PowerShell and point it to
`build/lib/Release` when using a multi-configuration generator. Every role accepts
`--help`/`-h`. Ctrl-C stops discovery or the running measurement; Unix also handles
SIGTERM. Invalid arguments return exit code 2 and runtime errors return 1.

By default the examples use the native default domain. Set
`CYCLONEDDS_EXAMPLE_DOMAIN_ID` to a numeric domain ID to isolate a managed pair.
Both processes must use the same domain, and Throughput also needs the same
partition. Cyclone's normal `CYCLONEDDS_URI` configuration remains available.

## Compatibility and measurements

The IDL content is copied from the reference examples. HelloWorld retains the
existing local filename `HelloWorld.idl`; its upstream name is
`HelloWorldData.idl`. Topic names, type names, key definitions and partitions
match the C++ programs, allowing a C# role to communicate with a C++ role.

Throughput retains writer batching and explicit burst flushing. RoundTrip echoes
the original DDS source timestamp and stops Pong on a disposed instance. Listener
callbacks use the managed binding's thread-pool delivery. Reported timings include
managed marshalling, allocations and scheduling; do not interpret them as native
C++ performance measurements. Throughput reports payload bytes, excluding DDS
and transport overhead, exactly as the reference example does.

The ports validate command-line ranges and count successfully written/received
samples. Ping's `numSamples` counts measured responses rather than reporting
intervals. Its configured duration limits measurement after the five-second
warm-up; Pong's timeout limits inactivity. These explicit limits keep finite
runs finite in both listener and WaitSet modes.

## Automated verification

```sh
ctest --test-dir build --output-on-failure
```

The `csharp_examples` suite runs separate processes for all six roles, checks all
Throughput receive modes, bursts, zero-sized and fragmented payloads, finite
runs, all four Ping/Pong listener combinations, echo validation, latency output,
DDS-based Pong termination, CLI validation and Unix signal cancellation.
Per-process output is saved under `build/example-tests` on failure or success.

To test the C# roles against actual C++ executables too, build the referenced C++
project with `BUILD_EXAMPLES=ON` against the same native Cyclone DDS installation,
then run:

```sh
python3 scripts/test-examples.py --build build --native build/lib --cxx-bin /path/to/cyclonedds-cxx-build/bin
```

That adds both directions of HelloWorld and Throughput communication, plus C#
Ping/C++ Pong and C++ Ping/C# Pong. These optional tests use domain 0 to match the
reference applications' default; the managed-only suite uses domain 73.
