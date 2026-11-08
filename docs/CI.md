# CI and platform builds

[Build, test and package](../.github/workflows/build.yml) runs on pushes to
`master`, all pull requests, manual dispatches, and temporarily on pushes to
`main` (the current development branch). Remove `main` from the push filter when
that temporary coverage is no longer needed. A new run cancels an older run for
the same branch or pull request.

The repository's [global.json](../global.json) selects the latest installed stable
.NET 9.0 SDK feature band. Installing SDK 9 alone does not select it when a runner
also has SDK 10. The internal MSBuild task targets .NET 9 and references the
selected SDK's MSBuild assemblies, so building it with SDK 10 would mix
incompatible framework references. This repository SDK selection does not
change the library/package version.

All native project libraries are shared. Windows builds consistently use the
shared MSVC CRT (`/MD` in Release, `/MDd` in Debug), including the pinned Cyclone
runtime, `idlc`, the backend and the bridge. The compiler exchanges `FILE*` and
other CRT objects between DLLs, so giving each module its own statically linked
CRT is incompatible with this plugin ABI. Release CRT DLLs from the selected
`VCToolsRedistDir` architecture are staged beside both the runtime and host tools.

CI sets `CYCLONEDDS_URI` to explicit IPv4 loopback/unicast discovery with automatic
participant indices and local discovery peers. Cross-process tests therefore
exercise real DDS networking without relying on multicast availability on hosted
runners. The library's normal discovery defaults are unchanged. Failed desktop
tests retain CTest/example diagnostic logs as an artifact for seven days.

The authoritative platform matrix is [targets.json](../.github/targets.json):

| Platform | Runtime RIDs | Host IDL tools |
| --- | --- | --- |
| Linux (glibc) | `linux-x64`, `linux-arm64` | Both architectures |
| Windows | `win-x64`, `win-arm64` | Both architectures |
| macOS | `osx-x64`, `osx-arm64` | Both architectures |
| Android | `android-arm64`, `android-x64`, `android-arm`, `android-x86` | None |
| iOS | `ios-arm64`, `iossimulator-arm64`, `iossimulator-x64` | None |

Linux uses Ubuntu 24.04 runners; native dependency compatibility follows that
build environment. Alpine/musl is not in this matrix. Windows uses Windows 2025
x64 and Windows 11 ARM64; macOS uses macOS 15 Intel and ARM64. Each desktop is
built and tested natively. Android cross-builds run on Linux with the pinned
NDK **27.2.12479018**, require Android **9/API 28** or newer, and use these ABIs:
`arm64-v8a`, `x86_64`, `armeabi-v7a`, and `x86`. API 28 is required by the pinned
Cyclone DDS thread implementation. The shared libraries use unversioned `.so`
filenames/SONAMEs for Android packaging and 16 KB ELF segment alignment.

## Workflow gates

1. Read the target matrix and the shared [VERSION](../VERSION).
2. On all six desktop hosts, build the pinned Cyclone DDS runtime, binding and
   C# IDL backend. Run an early native IDL compiler smoke test and CTest (managed/native runtime tests, IDL fixtures and
   separate-process examples), then stage only native runtime/tool payloads.
3. For each Android ABI and iOS device/simulator RID, build Cyclone DDS and the
   common runtime bridge with host IDL tools and managed examples/tests disabled.
   Mobile targets never build or ship an `idlc` executable or C# backend.
4. Verify each payload's actual ELF/PE/Mach-O CPU architecture. For Android,
   also check SONAMEs, native dependencies and 16 KB page alignment.
5. Download all native payloads into one staging directory. Require every RID
   listed in the matrix, reject unexpected/missing payloads and Android tools,
   then build **one** `Org.Eclipse.CycloneDDS.<VERSION>.nupkg`.
6. Verify the package contents and run the isolated package consumer tests on
   every desktop host. These cover one-reference IDL generation, includes,
   incremental builds, errors, clean/publish, runtime-only usage, transitive
   schema libraries and actual DDS sample exchange. Tools must stay out of app
   output. The combined package must work without a separate native install.
7. Only after all gates pass, archive the verified `.nupkg` as a directly
   downloadable final artifact, retained for 30 days.

Native transfer artifacts and the package candidate are intermediate artifacts
with one-day retention. No platform job packs a separate NuGet. The final archive
contains the same single package verified on all desktop hosts; it is not pushed
to NuGet.org and needs no publishing credentials. Fork pull requests use the
ordinary `pull_request` event with read-only repository permissions.

Android and iOS currently have cross-build and native binary checks, not
emulator/device DDS integration tests. Desktop test success must come from the actual GitHub
runs; local macOS validation alone does not establish Windows/Linux support.

## Local Android cross-build

Install the pinned Android NDK and set its path:

```sh
export ANDROID_NDK_HOME=/absolute/path/to/android-sdk/ndk/27.2.12479018
bash scripts/build-android.sh android-arm64
python3 scripts/verify-package.py \
  --stage build/android/android-arm64/binding/package --runtime-rid android-arm64
```

The Android script fetches/verifies the pinned Cyclone source through
`bootstrap-native.sh --source-only`. Build directories are separate per ABI.
It stages runtime-only files under
`build/android/<rid>/binding/package/runtimes/<rid>/native`.

On macOS with Xcode installed, cross-build iOS device and simulator assets:

```sh
bash scripts/build-ios.sh ios-arm64
bash scripts/build-ios.sh iossimulator-arm64
bash scripts/build-ios.sh iossimulator-x64
python3 scripts/verify-package.py \
   --stage build/ios/ios-arm64/binding/package --runtime-rid ios-arm64
```

Combine those runtime directories with the desktop `build/package` directories,
then use `dotnet pack ... -p:NativePackageDirectory=/absolute/path/to/stage` as
described in the [README](../README.md#create-the-nuget-package). Before and after
packing, validate the complete matrix:

```sh
python3 scripts/verify-package.py --stage /absolute/path/to/stage --targets .github/targets.json
python3 scripts/verify-package.py \
  --package artifacts/packages/Org.Eclipse.CycloneDDS.11.0.1.nupkg --targets .github/targets.json
```
