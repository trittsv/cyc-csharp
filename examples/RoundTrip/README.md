# RoundTrip

Port of C++ Ping/Pong. Both sides use topic `RoundTrip` with the unmodified
`RoundTripModule.DataType` IDL. Ping writes on partition `ping` and reads `pong`;
Pong uses the opposite partitions. Pong echoes the payload and exact DDS source
timestamp. Writers disable automatic disposal on unregistration, so ordinary
Ping exit does not terminate Pong.

After [building the examples](../README.md), run in separate terminals:

```sh
dotnet examples/RoundTrip/bin/Release/net9.0/RoundTrip.dll pong
```

```sh
dotnet examples/RoundTrip/bin/Release/net9.0/RoundTrip.dll ping 1024 1000 0
```

Both commands accept `-l` before positional arguments to receive through a native
data-available listener. Without `-l`, they use read conditions and WaitSets. The
two modes can be mixed freely:

```sh
dotnet examples/RoundTrip/bin/Release/net9.0/RoundTrip.dll pong -l
dotnet examples/RoundTrip/bin/Release/net9.0/RoundTrip.dll ping -l 1024 1000 0
```

## Arguments

`RoundTrip ping [-l] [payloadSize] [numSamples] [timeOut]`

| Argument | Default | Meaning |
| --- | --- | --- |
| `payloadSize` | `0` | Payload bytes, 0 to 104857600 |
| `numSamples` | `0` | Measured echo count after warm-up; 0 has no count limit |
| `timeOut` | `0` | Measurement duration in seconds after warm-up; 0 has no duration limit |

Ping warms up for five seconds after the first echo, then reports median,
minimum, 99th percentile and maximum in microseconds for:

- Half of the round-trip interval, measured with a monotonic clock.
- Write access: time inside `DataWriter.Write`.
- Read access: time inside `DataReader.Take`.

The full exchange includes transport, Pong processing and managed scheduling;
half RTT is not an independently measured one-way network delay. Ping verifies
the echoed payload and source timestamp. It prints one-second windows plus overall
statistics. When both limits are nonzero, the first reached limit ends the run.
A missing response with a finite timeout is an error; Ctrl-C stops a run normally.
Before the first echo, Ping retries the initial request with its original source
timestamp. This accommodates a C++ Pong that attaches its WaitSet after discovery;
these startup retries are excluded from measurements and duplicate startup echoes
are ignored.

`RoundTrip pong [-l] [timeOut]`

Pong's default timeout is `0` (infinite). A positive value limits inactivity in
seconds, restarting when a valid sample arrives. Ping/Pong match both directions
before starting; finite timeouts also bound initial discovery. Run one active
Ping/Pong pair per domain because the wire type has no request correlation key.

## Terminate Pong through DDS

```sh
dotnet examples/RoundTrip/bin/Release/net9.0/RoundTrip.dll ping quit
```

This sends a disposed instance, following the C++ example's termination protocol.
Pong checks instance state before valid data, prints `Quitting Pong`, and exits.
A quit client waits for Pong's reader to disconnect and retries the command for
up to five seconds if a legacy Pong is still attaching its receive condition.
A normal measured Ping run leaves Pong available for another run.
