# Reference revisions

| Dependency | Revision | Purpose |
| --- | --- | --- |
| [Cyclone DDS C](https://github.com/eclipse-cyclonedds/cyclonedds) | `93cad8bec019e09b68dfe8dec626a9e16983e259` (11.0.1) | Native runtime, IDL parser, XTypes metadata, runtime descriptors and serialization |
| [Cyclone DDS C++](https://github.com/eclipse-cyclonedds/cyclonedds-cxx) | `c185b86b6b689f8ae30234f583979b049c5dfab3` | API/test reference, license text and IDL fixture provenance |

The C++ repository was cloned temporarily outside this repository for inspection.
It is not a runtime or build dependency. The copied IDL fixtures and the test-name
inventory record the exact reference revision.

`scripts/bootstrap-native.sh` fetches the pinned C revision into `build/_deps` and
installs a shared runtime there. It refuses to overwrite a different checkout or
local source modifications. Builds use the upstream IDL compiler plugin ABI, so
the parser and generator must use matching Cyclone headers/libraries. The common
native bridge also consumes the pinned internal typelib API and private entity
headers to register embedded XTypes metadata in a participant’s domain. Set
`CYCLONEDDS_SOURCE_DIR` to this exact source revision when building the bridge
against a separate installation. Consumers receive prebuilt matching binaries. Newer
native versions need their own verification; a minimum CMake version check alone
is not proof of ABI compatibility.

No external managed packages are required by the runtime, examples or tests.
Native dependency builds in the bootstrap script disable security and SSL;
applications needing those features should supply a suitably configured native
Cyclone build. The wrapper does not implement security plugin configuration APIs.
