Copyright notices and licensing information are retained in individual files.
The project is available under EPL-2.0 OR BSD-3-Clause, matching the licensing
choice of the Cyclone DDS C++ binding. See [LICENSE](LICENSE).

The license text was copied without modification from
`eclipse-cyclonedds/cyclonedds-cxx` at revision
`c185b86b6b689f8ae30234f583979b049c5dfab3`.

The IDL fixtures in `src/ddscsharp/tests/upstream/idl` were copied without
modification from that revision's `src/ddscxx/tests/data` directory. They retain
their upstream notices where present and are covered by the upstream license.
`docs/upstream-tests.json` records upstream test names for coverage tracking;
it is not a claim that those tests have been ported.

The examples port all three example directories from that same C++ revision.
Their IDL content is copied unchanged (HelloWorldData.idl is locally named
HelloWorld.idl). Adapted example sources retain upstream copyright attribution
and add the C# contributors' attribution. See `examples/README.md` for the exact
roles, wire compatibility and deliberate CLI/statistics behavior choices.

New implementation files use the same license-header wording and SPDX expression,
with attribution to the Cyclone DDS C# contributors. This repository is an initial
implementation and does not assert Eclipse project membership, endorsement, or
OMG conformance certification.
