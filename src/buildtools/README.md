# Eclipse Cyclone DDS IDL build integration

IDL build support is included in **Org.Eclipse.CycloneDDS 11.0.1**. There is no
separate tools package. The internal `Org.Eclipse.CycloneDDS.BuildTasks` project
is not packable; its assembly ships under `tools/net9.0` in the main package.
Requires the .NET 9 SDK or newer. The package includes the native IDL compiler,
C# backend and their dependencies for its supported build hosts. Applications
need no CMake, C compiler, separately installed idlc or native type library.

```xml
<ItemGroup>
  <PackageReference Include="Org.Eclipse.CycloneDDS" Version="11.0.1" />
  <CycloneDDSIdl Include="Types/**/*.idl" />
</ItemGroup>
```

`dotnet build` generates C# into `obj/<configuration>/<framework>/cyclonedds`
and adds it to compilation. `dotnet clean` removes generated files. No generated
C, headers or application-specific shared library are produced. IDL-defined
modules determine the generated namespaces; the runtime uses `Org.Eclipse.CycloneDDS`.

For includes and preprocessor definitions:

```xml
<ItemGroup>
  <CycloneDDSIdlInclude Include="SharedTypes" />
  <CycloneDDSIdlDefine Include="FEATURE=1" />
</ItemGroup>
<PropertyGroup>
  <CycloneDDSDefaultExtensibility>final</CycloneDDSDefaultExtensibility>
</PropertyGroup>
```

Include every IDL containing type definitions in `CycloneDDSIdl`, including IDLs
referenced by other files. Each basename must be unique within the project.
The compiler preprocessor runs during builds to detect included-file and macro
changes; C# is regenerated only when effective inputs or tool binaries change.
Removed items have their generated outputs removed on the next build. Compiler
errors fail the build and cannot leave partially generated sources.

Tools run on the **build host**, independently of the application's target RID.
A package can contain multiple `tools/<host-rid>/native` payloads. A host missing
from the installed package produces a clear build error. Locally produced
packages contain only the hosts explicitly staged when packing. CI builds desktop host tools and Android runtime-only payloads, then combines
them into one multi-platform package. Android has no host compiler payload.
The same package includes native libraries under `runtimes/<target-rid>/native`
for the application's execution platform. Publish and runtime RID selection use the usual .NET rules.
The compiler and internal MSBuild task are not copied into application output.
Projects without IDL items use only the runtime; generated schema libraries can
pass their runtime dependency to consuming projects without exporting generation
targets. Do not mark the main package reference as `PrivateAssets="all"` when
your library needs that dependency to reach its consumers.

For maintainers, see `scripts/pack-package.py`. The override
`CycloneDDSToolDirectory` selects a complete native tool directory for development.
`CycloneDDSGeneratedDirectory` must be dedicated to generated output because
stale `.cs` and `.cs.sha256` files in that directory are removed automatically.

Licensed under EPL-2.0 OR BSD-3-Clause, as the Cyclone DDS C++ binding.
