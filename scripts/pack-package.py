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
"""Stage native runtimes and host tools, then pack the single Org.Eclipse.CycloneDDS package."""
import argparse
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent


def checked(command):
    return subprocess.run([str(x) for x in command], check=True, text=True, capture_output=True).stdout


def msvc_runtime_files(rid):
    """Use the selected MSVC toolchain's redistributable shared release CRT."""
    redist = os.environ.get('VCToolsRedistDir')
    if not redist:
        raise RuntimeError('VCToolsRedistDir is missing; stage Windows payloads from the matching MSVC developer environment.')
    arch = rid.rsplit('-', 1)[1]
    candidates = sorted((Path(redist) / arch).glob('Microsoft.VC*.CRT/vcruntime140.dll'))
    if not candidates:
        raise RuntimeError(f'No shared MSVC redistributable CRT found for {rid} in {redist}')
    expected = {'x64': 0x8664, 'arm64': 0xaa64}[arch]
    for candidate in reversed(candidates):
        matching = []
        for path in sorted(candidate.parent.glob('*.dll')):
            data = path.read_bytes()
            pe = struct.unpack_from('<I', data, 0x3c)[0]
            if data[:2] != b'MZ' or data[pe:pe + 4] != b'PE\0\0':
                raise RuntimeError(f'Invalid MSVC redistributable DLL: {path}')
            machine = struct.unpack_from('<H', data, pe + 4)[0]
            if machine == expected or arch == 'arm64' and machine == 0xa64e:
                matching.append(path)
        if any(path.name.casefold() == 'vcruntime140.dll' for path in matching):
            return matching
    raise RuntimeError(f'No {arch} MSVC redistributable CRT found in {redist}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prefix', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/packages')
    parser.add_argument('--rid')
    parser.add_argument('--runtime-only', action='store_true', help='Stage Android runtime libraries only; never produce Android host tools.')
    parser.add_argument('--stage-only', action='store_true', help='Stage this host without packing; combine staged RID directories before the final pack.')
    args = parser.parse_args()
    prefix = args.prefix.resolve()
    build = args.build.resolve()
    rid = args.rid or checked(['dotnet', 'msbuild', ROOT / 'src/ddscsharp/Org.Eclipse.CycloneDDS.csproj',
                              '-getProperty:NETCoreSdkRuntimeIdentifier', '-nologo']).strip()
    stage_root = build / 'package'
    runtime_only_rid = rid.startswith(('android-', 'ios-', 'iossimulator-'))
    if runtime_only_rid != args.runtime_only:
        raise RuntimeError('Android/iOS targets require --runtime-only; desktop targets must also stage host IDL tools.')
    if args.runtime_only and not args.stage_only:
        raise RuntimeError('Android/iOS payloads use --runtime-only --stage-only; assemble them with desktop tools before packing.')
    native = build / 'lib'
    if os.name == 'nt' and (native / 'Release').is_dir():
        native = native / 'Release'
    macho_rid = rid.startswith(('osx-', 'ios-', 'iossimulator-'))
    crt_files = msvc_runtime_files(rid) if rid.startswith('win-') else []
    if not args.runtime_only:
        stage = stage_root / 'tools' / rid / 'native'
        if stage.exists():
            shutil.rmtree(stage)
        stage.mkdir(parents=True)
        windows = os.name == 'nt'
        executable = prefix / 'bin' / ('idlc.exe' if windows else 'idlc')
        shutil.copy2(executable, stage / executable.name)
        plugin = 'cycloneddsidlcsharp.dll' if windows else 'libcycloneddsidlcsharp.dylib' if sys.platform == 'darwin' else 'libcycloneddsidlcsharp.so'
        shutil.copy2(native / plugin, stage / plugin)
        # Keep SONAME aliases: idlc and the backend need the matching parser and DDS runtime.
        pattern = '*.dll' if windows else '*.dylib' if sys.platform == 'darwin' else '*.so*'
        libraries = prefix / ('bin' if windows else 'lib')
        for source in libraries.glob(pattern):
            if 'cycloneddsidl' in source.name or re.match(r'(lib)?ddsc[.-]', source.name):
                shutil.copy2(source, stage / source.name, follow_symlinks=True)
        for source in crt_files:
            shutil.copy2(source, stage / source.name)
        if sys.platform == 'darwin':
            for path in stage.iterdir():
                load = checked(['otool', '-l', path])
                rpaths = re.findall(r'cmd LC_RPATH\s+cmdsize \d+\s+path (.*?) \(offset', load)
                for rpath in rpaths:
                    checked(['install_name_tool', '-delete_rpath', rpath, path])
                checked(['install_name_tool', '-add_rpath', '@loader_path', path])
                checked(['codesign', '--force', '--sign', '-', path])
        elif not windows:
            # Preserve idlc's installed $ORIGIN/../lib lookup without requiring patchelf.
            lib = stage.parent / 'lib'
            lib.mkdir(exist_ok=True)
            for path in stage.glob('*.so*'):
                shutil.copy2(path, lib / path.name)
    runtime = stage_root / 'runtimes' / rid / 'native'
    if runtime.exists():
        shutil.rmtree(runtime)
    runtime.mkdir(parents=True)
    runtime_patterns = ('cycloneddscsharp.dll', 'ddsc.dll') if rid.startswith('win-') else (
        ('libcycloneddscsharp*.dylib', 'libddsc*.dylib') if macho_rid
        else ('libcycloneddscsharp.so*', 'libddsc.so*'))
    for runtime_pattern in runtime_patterns:
        matches = list(native.glob(runtime_pattern))
        if not matches:
            raise RuntimeError(f'Missing runtime files matching {runtime_pattern} in {native}; build the native bridge first.')
        for source in matches:
            shutil.copy2(source, runtime / source.name, follow_symlinks=True)
    for source in crt_files:
        shutil.copy2(source, runtime / source.name)
    if macho_rid:
        for path in runtime.iterdir():
            load = checked(['otool', '-l', path])
            for rpath in re.findall(r'cmd LC_RPATH\s+cmdsize \d+\s+path (.*?) \(offset', load):
                checked(['install_name_tool', '-delete_rpath', rpath, path])
            checked(['install_name_tool', '-add_rpath', '@loader_path', path])
            checked(['codesign', '--force', '--sign', '-', path])
    # Verify the relocated host compiler before packing.
    if not args.runtime_only:
        checked([stage / executable.name, '-v'])
    if args.stage_only:
        print(f'Staged {"runtime" if args.runtime_only else "runtime and IDL tools"} for {rid} in {stage_root}')
        return 0
    args.output.mkdir(parents=True, exist_ok=True)
    subprocess.run(['dotnet', 'pack', str(ROOT / 'src/ddscsharp/Org.Eclipse.CycloneDDS.csproj'),
                    '-c', 'Release', '-o', str(args.output.resolve()), '-p:UseSharedCompilation=false',
                    f'-p:NativePackageDirectory={stage_root}'], check=True)
    print(f'Packed Org.Eclipse.CycloneDDS with native runtime and IDL tools for {rid}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
