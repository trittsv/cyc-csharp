#!/usr/bin/env python3
# Copyright(c) 2026 Cyclone DDS C# contributors
#
# This program and the accompanying materials are made available under the
# terms of the Eclipse Public License v. 2.0 which is available at
# http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
# v. 1.0 which is available at
# http://www.eclipse.org/org/documents/edl-v10.php.
#
# SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
"""Verify the single-package IDL build, runtime deployment and transitive consumption in isolated apps."""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
from xml.etree import ElementTree
from xml.sax.saxutils import escape
from zipfile import ZipFile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--build", type=Path, required=True)
    args = parser.parse_args()
    package = args.package.resolve()
    with ZipFile(package) as archive:
        metadata_path = next(n for n in archive.namelist() if n.endswith(".nuspec"))
        xml = ElementTree.fromstring(archive.read(metadata_path))
        ns = {"n": "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"}
        package_id = xml.findtext("n:metadata/n:id", namespaces=ns)
        version = xml.findtext("n:metadata/n:version", namespaces=ns)
        if package_id != "Org.Eclipse.CycloneDDS":
            raise RuntimeError(f"Unexpected package ID: {package_id}")
        if f"lib/net9.0/{package_id}.dll" not in archive.namelist():
            raise RuntimeError("The managed assembly filename must match the package ID.")
        if any(n.rsplit("/", 1)[-1] == "CycloneDDS.dll" for n in archive.namelist()):
            raise RuntimeError("The package contains an obsolete managed assembly.")
        if not any(n.startswith("runtimes/") and "cycloneddscsharp" in n for n in archive.namelist()):
            raise RuntimeError("This verification requires a package containing the native runtime.")
        for required in (f"build/{package_id}.props", f"build/{package_id}.targets",
                         "tools/net9.0/Org.Eclipse.CycloneDDS.BuildTasks.dll"):
            if required not in archive.namelist():
                raise RuntimeError(f"The single package is missing {required}")
        if not any(n.startswith("tools/") and n.endswith(("/idlc", "/idlc.exe")) for n in archive.namelist()):
            raise RuntimeError("The package has no bundled IDL compiler")
        if xml.findall(".//n:dependency", namespaces=ns):
            raise RuntimeError("The combined package must not depend on a separate build-tools package")
    digest = hashlib.sha256(package.read_bytes()).hexdigest()[:16]
    directory = args.build.resolve() / "package-tests" / (digest + " consumer with spaces")
    if directory.exists(): shutil.rmtree(directory)
    feed = directory / "feed"
    feed.mkdir(parents=True, exist_ok=True)
    shutil.copy2(package, feed / package.name)
    types = directory / "Types"
    types.mkdir(exist_ok=True)
    source = Path(__file__).resolve().parent.parent / "examples/HelloWorld/HelloWorld.idl"
    (types / "Shared.idl").write_text("module Shared { struct Point { long value; }; };\n")
    (types / "HelloWorld.idl").write_text('#include "Shared.idl"\n' + source.read_text().replace('string message;', 'string message; Shared::Point point;'))
    codegen = '''<CycloneDDSIdl Include="Types/*.idl" />
    <CycloneDDSIdlInclude Include="Types" />
    <CycloneDDSIdlDefine Include="FEATURE=1" />'''
    project = f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <RestoreSources>{escape(str(feed))}</RestoreSources>
    <RestorePackagesPath>{escape(str(directory / "cache"))}</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="{package_id}" Version="{version}" />
    {codegen}
  </ItemGroup>
</Project>
'''
    (directory / "Smoke.csproj").write_text(project)
    program = '''using Org.Eclipse.CycloneDDS;
using HelloWorldData;
if (typeof(DomainParticipant).Assembly.GetName().Name != "Org.Eclipse.CycloneDDS" ||
    typeof(DomainParticipant).Namespace != "Org.Eclipse.CycloneDDS")
    throw new InvalidOperationException("The managed assembly and namespace must match the NuGet package ID.");
using var participant = new DomainParticipant(42);
using var topic = participant.CreateTopic("PackageSmoke", Msg.TypeSupport);
var qos = new Qos { Reliability = new(ReliabilityKind.Reliable, Duration.FromSeconds(3)) };
using var reader = participant.CreateReader(topic, qos);
using var writer = participant.CreateWriter(topic, qos with { WriterBatching = true });
using var condition = reader.CreateReadCondition();
using var waitset = participant.CreateWaitSet();
waitset.Attach(condition);
writer.Write(new Msg { userID = 7, message = "packaged native runtime" });
writer.Flush();
if (waitset.Wait(Duration.FromSeconds(3)).Count == 0) return 1;
var sample = reader.Take().Single();
Console.WriteLine(sample.Data.message);
return sample.Info.ValidData && sample.Data.userID == 7 && sample.Data.message == "packaged native runtime" ? 0 : 2;
'''
    program = program.replace('message = \"packaged native runtime\"', 'message = \"packaged native runtime\", point = new Shared.Point(new Shared.Point { value = 23 })')
    program = program.replace('return sample.Info.ValidData', 'if (sample.Data.point.value != 23) return 3;\nreturn sample.Info.ValidData')
    (directory / 'Program.cs').write_text(program)
    environment = os.environ.copy()
    environment.pop("CYCLONEDDS_CSHARP_NATIVE", None)
    dotnet = shutil.which("dotnet")
    if not dotnet: raise RuntimeError("dotnet is required")
    # Expose only dotnet through PATH: a consumer cannot accidentally use CMake,
    # a C compiler, globally installed idlc or this repository's native build.
    minimal_bin = directory / "dotnet-only"
    minimal_bin.mkdir(exist_ok=True)
    if os.name != "nt":
        (minimal_bin / "dotnet").unlink(missing_ok=True)
        (minimal_bin / "dotnet").symlink_to(Path(dotnet).resolve())
        environment["PATH"] = str(minimal_bin)
    for key in ("DYLD_LIBRARY_PATH", "LD_LIBRARY_PATH", "CMAKE_PREFIX_PATH"):
        environment.pop(key, None)
    project_path = directory / "Smoke.csproj"
    def build(*arguments, success=True):
        result = subprocess.run([dotnet, "build", str(project_path), "--nologo", "-p:UseSharedCompilation=false", *arguments], env=environment,
                                capture_output=True, text=True)
        if (result.returncode == 0) != success:
            raise RuntimeError(result.stdout + result.stderr)
        return result.stdout + result.stderr
    build()
    outputs = directory / "obj/Debug/net9.0/cyclonedds"
    paths = sorted(outputs.glob("*.cs"))
    if len(paths) != 2 or any(p.suffix in (".c", ".h") for p in outputs.iterdir()):
        raise RuntimeError("Expected exactly two generated C# sources and no C/header output")
    # Targeting Android must still execute this desktop host's IDL tools.
    cross = subprocess.run([dotnet, "msbuild", str(project_path), "-nologo", "-t:CycloneDDSGenerate",
                            "-p:RuntimeIdentifier=android-arm64"], env=environment, capture_output=True, text=True)
    if cross.returncode: raise RuntimeError(cross.stdout + cross.stderr)
    if not list((directory / "obj").glob("**/android-arm64/cyclonedds/HelloWorld.cs")):
        raise RuntimeError("Cross-target IDL generation did not produce Android-targeted C# output")
    mtimes = {p: p.stat().st_mtime_ns for p in paths}
    build()
    if any(p.stat().st_mtime_ns != t for p, t in mtimes.items()):
        raise RuntimeError("Unchanged inputs caused regeneration")
    shared = directory / "Types/Shared.idl"
    shared.write_text(shared.read_text().replace('long value;', 'long value; long other;'))
    build()
    if any(p.stat().st_mtime_ns == t for p, t in mtimes.items()):
        raise RuntimeError("Included IDL changes did not invalidate dependent outputs")
    mtimes = {p: p.stat().st_mtime_ns for p in paths}
    build('-p:CycloneDDSDefaultExtensibility=appendable')
    if any(p.stat().st_mtime_ns == t for p, t in mtimes.items()):
        raise RuntimeError("Compiler option changes did not invalidate outputs")
    original_project = project_path.read_text()
    project_path.write_text(original_project.replace('FEATURE=1', 'FEATURE=2'))
    mtimes = {p: p.stat().st_mtime_ns for p in paths}
    build()
    if any(p.stat().st_mtime_ns == t for p, t in mtimes.items()):
        raise RuntimeError("Preprocessor definition changes did not invalidate outputs")
    project_path.write_text(original_project)
    duplicate = directory / "Other/Shared.idl"
    duplicate.parent.mkdir()
    duplicate.write_text('module Duplicate { struct Point { long x; }; };\n')
    project_path.write_text(original_project.replace('</ItemGroup>', '<CycloneDDSIdl Include="Other/Shared.idl" /></ItemGroup>'))
    if 'basenames must be unique' not in build(success=False):
        raise RuntimeError("Duplicate IDL basenames did not produce the expected diagnostic")
    project_path.write_text(original_project)
    if 'has no IDL compiler for build host' not in build('-p:CycloneDDSToolDirectory=' + str(directory / 'missing-tools'), success=False):
        raise RuntimeError("A missing host tool payload did not produce a useful diagnostic")
    extra = directory / "Types/Removed.idl"
    extra.write_text('module Removed { struct Value { long number; }; };\n')
    build()
    extra.unlink()
    build()
    if (outputs / "Removed.cs").exists(): raise RuntimeError("Removed IDL left stale generated code")
    source = directory / "Types/HelloWorld.idl"
    original = source.read_text()
    source.write_text(original + '\ninvalid IDL here!\n')
    build(success=False)
    source.write_text(original)
    build()
    if list(outputs.glob('.stage-*')): raise RuntimeError("A failed generation left staging files")
    # A runtime-only consumer must work even when no host compiler is available,
    # including the transition after removing the last IDL item.
    project_path.write_text(project.replace(codegen, ""))
    program_path = directory / "Program.cs"
    program_path.write_text('using Org.Eclipse.CycloneDDS; Console.WriteLine(typeof(DomainParticipant).Assembly.GetName().Name);\n')
    build('-p:CycloneDDSToolDirectory=' + str(directory / 'missing-tools'))
    if list(outputs.glob('*.cs')):
        raise RuntimeError("Removing all IDL items left stale generated files")
    project_path.write_text(project)
    program_path.write_text(program)
    build()
    subprocess.run([dotnet, "clean", str(project_path), "--nologo"], env=environment, check=True, capture_output=True)
    if list(outputs.glob('*.cs')): raise RuntimeError("dotnet clean did not remove generated C#")
    build()
    publish = directory / "published"
    subprocess.run([dotnet, "publish", str(project_path), "--nologo", "-p:UseSharedCompilation=false", "-o", str(publish)],
                   env=environment, check=True, capture_output=True)
    if any('BuildTasks' in p.name or 'idlc' in p.name for p in publish.rglob('*')):
        raise RuntimeError("Build-only tools leaked into application output")
    subprocess.run([dotnet, str(publish / "Smoke.dll")], env=environment, check=True)
    print("PASS Integrated IDL build: Android target with desktop host compiler, dotnet-only PATH, paths with spaces, includes, options, incremental build, removed IDL, compiler errors, clean and publish")
    subprocess.run([dotnet, str(directory / "bin/Debug/net9.0/Smoke.dll")], env=environment, check=True)
    print(f"PASS {package_id} {version}: isolated package restore, native probing, DDS round trip")
    # A schema library references the package normally. Its consumer needs only
    # a project reference: runtime assemblies/native assets flow transitively,
    # while code generation remains in the project that owns the IDLs.
    library = directory.parent / (digest + " schema library")
    app = directory.parent / (digest + " transitive app")
    for child in (library, app):
        if child.exists(): shutil.rmtree(child)
        child.mkdir()
    shutil.copytree(types, library / "Types")
    library_project = project.replace('<OutputType>Exe</OutputType>', '')
    (library / "Schema.csproj").write_text(library_project)
    app_project = project.replace(codegen, "").replace(
        f'<PackageReference Include="{package_id}" Version="{version}" />',
        f'<ProjectReference Include="{escape(str(library / "Schema.csproj"))}" />')
    (app / "Transitive.csproj").write_text(app_project)
    (app / "Program.cs").write_text(program)
    result = subprocess.run([dotnet, "build", str(app / "Transitive.csproj"), "--nologo", "-p:UseSharedCompilation=false"],
                            env=environment, capture_output=True, text=True)
    if result.returncode: raise RuntimeError(result.stdout + result.stderr)
    subprocess.run([dotnet, str(app / "bin/Debug/net9.0/Transitive.dll")], env=environment, check=True)
    if (app / "obj/Debug/net9.0/cyclonedds").exists():
        raise RuntimeError("Code generation unexpectedly flowed into a consuming project")
    if any('BuildTasks' in p.name or 'idlc' in p.name for p in (app / "bin").rglob('*')):
        raise RuntimeError("Build-only tools leaked into transitive application output")
    print("PASS Single package: runtime-only builds, last-IDL removal and transitive schema-library/runtime consumption")
    return 0


if __name__ == "__main__":
    sys.exit(main())
