# Standards and API decisions

[OMG IDL4-to-C# 1.0](https://www.omg.org/spec/IDL4-CSHARP/1.0), adopted in April
2022, is the published reference for data-type mapping. It is distinct from a
DDS entity API specification. The [DDS Foundation standards list](https://www.dds-foundation.org/omg-dds-standard/)
lists DDS C# API as work in progress. Cyclone's [roadmap](https://github.com/eclipse-cyclonedds/cyclonedds/blob/master/ROADMAP.md)
also lists C# language binding support as upcoming work. This implementation does
not assume that a finished DDS C# API standard exists.

The selected data mapping preserves IDL names, uses classes for structs, typed
discriminated union classes, native C# arrays, `string`, and
`Omg.Types.ISequence<T>`. Generated classes provide default, copy and applicable
all-values constructors plus structural equality. Typedefs resolve to the
underlying type. Type metadata comes from Cyclone’s IDL compiler; wire behavior comes from its native runtime.
The implementation is based on the mapping; it is not a complete implementation
of every mapping building block. See [STATUS.md](STATUS.md) for omissions.

The DDS API uses .NET naming and disposal idioms while retaining DDS entity
relationships. Examples include `DomainParticipant`, `Topic<T>`,
`DataWriter<T>.Write`, `DataReader<T>.Take`, `ReadCondition`, and `WaitSet`.
`Duration` represents nonnegative DDS nanoseconds with a distinct infinite value;
`Timestamp` uses nanoseconds since the Unix epoch. Native errors become
`DdsException` with a DDS `ReturnCode`.

This API currently uses one immutable QoS policy value type for all entity
kinds; native Cyclone checks whether policies apply and whether combinations are
consistent. `GetCoreQos()` intentionally reads only history, reliability,
durability and resource limits. It is not a full QoS snapshot.

The native ABI bridge isolates sample-information and sequence layouts and the
pinned native typelib ABI. The generator emits C# exclusively, embedding the
compiler’s serialized XTypes TypeInformation and TypeMapping, including keys and
annotations. C# computes native sample layouts; the bridge validates the total
size and alignment against the descriptor it constructs with Cyclone’s type
builder. Managed codecs translate objects into that in-memory representation.
The native descriptor owns XCDR encoding, key serialization and discovery type
information. No application-specific native library or second wire serializer
is required. Metadata-only operations such as cloning work without a participant.
