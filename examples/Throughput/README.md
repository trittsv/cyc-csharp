# Throughput

Port of the C++ Throughput publisher/subscriber, including reliable KeepAll
history, publisher partitions, writer batching, burst intervals, sequence-number
checking per publication handle, periodic rates and aggregate totals.

After [building the examples](../README.md), run in separate terminals:

```sh
dotnet examples/Throughput/bin/Release/net9.0/Throughput.dll sub 5 0 "Throughput example"
```

```sh
dotnet examples/Throughput/bin/Release/net9.0/Throughput.dll pub 8192 10 10 0 "Throughput example"
```

The subscriber prints five reporting cycles and exits. The publisher stops when
its last reader disconnects. Run with defaults to measure until Ctrl-C:

```sh
dotnet examples/Throughput/bin/Release/net9.0/Throughput.dll sub
dotnet examples/Throughput/bin/Release/net9.0/Throughput.dll pub
```

## Publisher arguments

`Throughput pub [payloadSize] [burstInterval] [burstSize] [timeOut] [partitionName]`

| Argument | Default | Meaning |
| --- | --- | --- |
| `payloadSize` | `8192` | Payload bytes; range 0 to 104857600 |
| `burstInterval` | `0` | Milliseconds between burst starts; 0 sends continuously |
| `burstSize` | `1` | Samples per burst; must be positive |
| `timeOut` | `0` | Writing duration in seconds; 0 runs until cancelled/disconnected |
| `partitionName` | `Throughput example` | Publisher/subscriber partition |

Payload bytes contain ASCII `a`. `count` increases after each successful write.
Writer batching is enabled; nonzero burst intervals flush each burst, and all
runs flush pending writes before exiting. The publisher allows 100 cached
samples; reliability uses a 10-second maximum blocking time.

## Subscriber arguments

`Throughput sub [maxCycles] [pollingDelay] [partitionName]`

| Argument | Default | Meaning |
| --- | --- | --- |
| `maxCycles` | `0` | Number of periodic reports; 0 runs until cancelled/disconnected |
| `pollingDelay` | `-1` | `-1`: listener; `0`: WaitSet; positive values: polling delay in milliseconds |
| `partitionName` | `Throughput example` | Must match the publisher partition |

The listener default follows the actual C++ implementation. The subscriber
allows 1000 cached samples. Each report shows payload size, received samples and
payload bytes, out-of-order sample count, samples/second and Mbit/second. An
unexpected next sequence number counts as one out-of-order sample, matching the
reference. Multiple writers are tracked independently by publication handle.

The main loop reports approximately once per second while new samples arrive.
The final totals include any samples queued just before writer disconnection.
