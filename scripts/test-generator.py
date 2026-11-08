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
"""Compile the upstream IDL corpus using the C#-only backend.

Expected rejections are reported separately from supported fixtures. A change in
either direction requires updating the checked-in expectations deliberately.
The C# compiler is invoked directly against installed SDK reference assemblies;
this check needs no NuGet feeds or test framework packages.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent


def run(command):
    return subprocess.run([str(x) for x in command], capture_output=True, text=True)


def checked(command):
    result = run(command)
    if result.returncode:
        raise RuntimeError(f"Command failed: {command}\n{result.stdout}{result.stderr}")
    return result


def compiler_paths(dotnet):
    sdk_list = checked([dotnet, "--list-sdks"]).stdout.splitlines()
    version, parent = re.match(r"(\S+) \[(.*)\]", sdk_list[-1]).groups()
    sdk_root = Path(parent)
    csc = sdk_root / version / "Roslyn/bincore/csc.dll"
    packs = sdk_root.parent / "packs/Microsoft.NETCore.App.Ref"
    candidates = sorted(packs.glob("*/ref/net9.0"), key=lambda p: tuple(int(x) for x in p.parents[1].name.split(".") if x.isdigit()))
    if not candidates:
        raise RuntimeError("The .NET 9 reference pack must be installed.")
    return csc, candidates[-1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--idlc", type=Path, required=True)
    parser.add_argument("--plugin", type=Path, required=True)
    parser.add_argument("--prefix", type=Path, required=True)
    args = parser.parse_args()
    args.build = args.build.resolve()
    args.prefix = args.prefix.resolve()
    args.idlc = args.idlc.resolve()
    args.plugin = args.plugin.resolve()
    dotnet = shutil.which("dotnet")
    if not dotnet:
        raise RuntimeError("dotnet is required")
    csc, refs = compiler_paths(dotnet)
    assembly = ROOT / "src/ddscsharp/bin/Release/net9.0/Org.Eclipse.CycloneDDS.dll"
    if not assembly.is_file():
        raise RuntimeError("Build the managed_tests CMake target first.")
    output_root = args.build / "idlcsharp-tests"
    output_root.mkdir(parents=True, exist_ok=True)
    references = output_root / "references.rsp"
    references.write_text("\n".join([
        "-nostdlib+", "-target:library", "-nullable:enable", "-warnaserror+",
        f'-r:"{assembly}"', *[f'-r:"{p}"' for p in refs.glob("*.dll")]
    ]) + "\n")
    fixtures = ROOT / "src/ddscsharp/tests/upstream/idl"
    expectations = json.loads((fixtures.parent / "expectations.json").read_text())
    actual_names = {p.name for p in fixtures.glob("*.idl")}
    if actual_names != set(expectations):
        raise RuntimeError("Every upstream fixture must have an explicit expectation.")
    supported = rejected = failures = 0
    reports = []
    for name, expectation in expectations.items():
        source = fixtures / name
        output = output_root / source.stem
        if output.exists(): shutil.rmtree(output)
        output.mkdir()
        generated_cs = output / (source.stem + ".cs")
        generated_cs.unlink(missing_ok=True)
        command = [args.idlc, "-x", "final", "-l", args.plugin, "-o", output, source]
        try:
            result = run(command)
            if expectation["status"] == "reject":
                if result.returncode == 0 or expectation["diagnostic"] not in result.stderr:
                    raise RuntimeError(f"Expected rejection: {expectation['diagnostic']}\n{result.stderr}")
                if any(output.iterdir()):
                    raise RuntimeError("Rejected input left partial generated source files.")
                rejected += 1
                print(f"REJECT (documented limitation) {name}: {expectation['diagnostic']}")
                reports.append({"fixture": name, "status": "expected-rejection"})
                continue
            if result.returncode:
                raise RuntimeError(result.stderr)
            digest = hashlib.sha256(generated_cs.read_bytes()).digest()
            checked(command)
            if hashlib.sha256(generated_cs.read_bytes()).digest() != digest:
                raise RuntimeError("Generation is not deterministic.")
            if {p.suffix for p in output.iterdir()} != {".cs"}:
                raise RuntimeError("The C# backend emitted files other than C# source.")
            checked([dotnet, csc, f"@{references}", f"-out:{output / (source.stem + '.dll')}", generated_cs])
            supported += 1
            print(f"PASS {name}: deterministic C#-only generation, managed compilation")
            reports.append({"fixture": name, "status": "compiled"})
        except Exception as error:
            failures += 1
            print(f"FAIL {name}: {error}", file=sys.stderr)
            reports.append({"fixture": name, "status": "failed", "error": str(error)})
    (output_root / "results.json").write_text(json.dumps(reports, indent=2) + "\n")
    print(f"{supported} compiled, {rejected} documented rejections, {failures} failures")
    return int(failures != 0)


if __name__ == "__main__":
    sys.exit(main())
