# HelloWorld

Port of the C++ HelloWorld publisher and subscriber. The publisher sends one
`HelloWorldData.Msg` with `userID = 1` and `message = "Hello World"` on topic
`HelloWorldData_Msg`. The subscriber ignores lifecycle-only notifications and
prints the first valid sample. Discovery/receive waits are limited to 30 seconds.
After sending, the publisher keeps the instance alive until the one-shot
subscriber exits (also limited to 30 seconds), matching the C++ example and
allowing a best-effort subscriber to consume the message before disposal.

After [building the examples](../README.md), run in separate terminals:

```sh
dotnet examples/HelloWorld/bin/Release/net9.0/HelloWorld.dll sub
```

```sh
dotnet examples/HelloWorld/bin/Release/net9.0/HelloWorld.dll pub
```

`publisher` and `subscriber` are also accepted as role names. Either role can be
paired with the corresponding original C++ executable. The code separates
[publisher](Publisher.cs), [subscriber](Subscriber.cs) and argument handling in
[Program.cs](Program.cs).
