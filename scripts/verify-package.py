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
"""Check staged/final native payloads, CPU architectures and Android loadability."""
import argparse
import json
from pathlib import Path
import struct
from xml.etree import ElementTree
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parent.parent


def elf(data):
    if data[:4] != b'\x7fELF' or data[5] != 1:
        raise ValueError('Expected a little-endian ELF binary')
    bits = data[4]
    if bits not in (1, 2):
        raise ValueError('Unsupported ELF class')
    header = struct.unpack_from('<HHIQQQIHHHHHH' if bits == 2 else '<HHIIIIIHHHHHH', data, 16)
    segments = []
    for index in range(header[9]):
        fields = struct.unpack_from('<IIQQQQQQ' if bits == 2 else '<IIIIIIII', data, header[4] + index * header[8])
        if bits == 2:
            kind, _, offset, address, _, size, _, alignment = fields
        else:
            kind, offset, address, _, size, _, _, alignment = fields
        segments.append((kind, offset, address, size, alignment))
    dynamic = {}
    for kind, offset, _, size, _ in segments:
        if kind != 2:  # PT_DYNAMIC
            continue
        step = 16 if bits == 2 else 8
        for entry in range(offset, offset + size, step):
            tag, value = struct.unpack_from('<QQ' if bits == 2 else '<II', data, entry)
            if tag == 0:
                break
            dynamic.setdefault(tag, []).append(value)
    strings = None
    if 5 in dynamic:  # DT_STRTAB, a virtual address
        address = dynamic[5][0]
        for kind, offset, virtual, size, _ in segments:
            if kind == 1 and virtual <= address < virtual + size:
                strings = offset + address - virtual
                break
    def string(value):
        if strings is None:
            raise ValueError('Missing ELF dynamic string table')
        start = strings + value
        return data[start:data.index(b'\0', start)].decode('utf-8')
    return header[1], segments, [string(v) for v in dynamic.get(1, [])], [string(v) for v in dynamic.get(14, [])]


def verify_binary(path, data, rid):
    arch = rid.rsplit('-', 1)[1]
    if rid.startswith('win-'):
        pe = struct.unpack_from('<I', data, 0x3c)[0]
        if data[:2] != b'MZ' or data[pe:pe + 4] != b'PE\0\0':
            raise ValueError(f'{path}: expected a Windows PE binary')
        machine = struct.unpack_from('<H', data, pe + 4)[0]
        expected = {'x64': 0x8664, 'arm64': 0xaa64}[arch]
        # MSVC's ARM64 redistributables may be ARM64X hybrid DLLs.
        if arch == 'arm64' and machine == 0xa64e:
            machine = expected
    elif rid.startswith('osx-'):
        if data[:4] != b'\xcf\xfa\xed\xfe':
            raise ValueError(f'{path}: expected a 64-bit Mach-O binary')
        machine = struct.unpack_from('<I', data, 4)[0]
        expected = {'x64': 0x01000007, 'arm64': 0x0100000c}[arch]
    else:
        machine, segments, dependencies, sonames = elf(data)
        expected = {'x86': 3, 'arm': 40, 'x64': 62, 'arm64': 183}[arch]
        if rid.startswith('android-'):
            if not path.endswith('.so') or sonames != [path.rsplit('/', 1)[1]]:
                raise ValueError(f'{path}: Android requires an unversioned .so filename and matching SONAME')
            loads = [s for s in segments if s[0] == 1]
            if not loads or any(s[4] < 16384 for s in loads):
                raise ValueError(f'{path}: Android LOAD segments must support 16 KB pages')
            allowed = {'libc.so', 'libm.so', 'libdl.so', 'liblog.so', 'libandroid.so', 'libddsc.so'}
            if set(dependencies) - allowed:
                raise ValueError(f'{path}: unpackaged Android dependencies: {set(dependencies) - allowed}')
    if machine != expected:
        raise ValueError(f'{path}: CPU machine {machine:#x} does not match {rid} ({expected:#x})')


def verify(files, read, runtime_rids, tool_rids, version, package):
    actual_runtime = {p.split('/')[1] for p in files if p.startswith('runtimes/')}
    actual_tools = {p.split('/')[1] for p in files if p.startswith('tools/') and len(p.split('/')) > 3 and p.split('/')[2] in ('native', 'lib')}
    if actual_runtime != runtime_rids or actual_tools != tool_rids:
        raise ValueError(f'RID payload mismatch: runtimes={sorted(actual_runtime)}, tools={sorted(actual_tools)}; '
                         f'expected runtimes={sorted(runtime_rids)}, tools={sorted(tool_rids)}')
    major = version.split('.')[0]
    for rid in sorted(runtime_rids):
        required = ('ddsc.dll', 'cycloneddscsharp.dll', 'vcruntime140.dll') if rid.startswith('win-') else (
            (f'libddsc.{major}.dylib', 'libcycloneddscsharp.dylib') if rid.startswith('osx-') else (
                ('libddsc.so', 'libcycloneddscsharp.so') if rid.startswith('android-') else (f'libddsc.so.{major}', 'libcycloneddscsharp.so')))
        for name in required:
            path = f'runtimes/{rid}/native/{name}'
            if path not in files:
                raise ValueError(f'Missing runtime file {path}')
        for path in sorted(p for p in files if p.startswith(f'runtimes/{rid}/')):
            verify_binary(path, read(path), rid)
    for rid in sorted(tool_rids):
        required = ('idlc.exe', 'cycloneddsidlcsharp.dll', 'cycloneddsidl.dll', 'ddsc.dll', 'vcruntime140.dll') if rid.startswith('win-') else (
            ('idlc', 'libcycloneddsidlcsharp.dylib', f'libcycloneddsidl.{major}.dylib', f'libddsc.{major}.dylib') if rid.startswith('osx-') else (
                'idlc', 'libcycloneddsidlcsharp.so', f'libcycloneddsidl.so.{major}', f'libddsc.so.{major}'))
        for name in required:
            path = f'tools/{rid}/native/{name}'
            if path not in files:
                raise ValueError(f'Missing host tool/dependency {path}')
        for path in sorted(p for p in files if p.startswith(f'tools/{rid}/')):
            verify_binary(path, read(path), rid)
    if package:
        package_id = 'Org.Eclipse.CycloneDDS'
        for path in (f'lib/net9.0/{package_id}.dll', f'build/{package_id}.props', f'build/{package_id}.targets',
                     f'tools/net9.0/{package_id}.BuildTasks.dll', 'README.md', 'LICENSE', 'NOTICE.md'):
            if path not in files:
                raise ValueError(f'Missing package file {path}')
        metadata = ElementTree.fromstring(read(f'{package_id}.nuspec'))
        ns = {'n': 'http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd'}
        if metadata.findtext('n:metadata/n:id', namespaces=ns) != package_id or metadata.findtext('n:metadata/n:version', namespaces=ns) != version:
            raise ValueError('Package identity/version differs from VERSION')
        if metadata.findall('.//n:dependency', namespaces=ns):
            raise ValueError('The single package must not depend on another build-tools package')
    print(f'PASS {"NuGet" if package else "stage"}: {len(runtime_rids)} runtimes, {len(tool_rids)} desktop tool sets; '
          'native CPU architectures and Android SONAME/dependencies/16 KB alignment verified')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument('--stage', type=Path)
    source.add_argument('--package', type=Path)
    targets = parser.add_mutually_exclusive_group(required=True)
    targets.add_argument('--targets', type=Path)
    targets.add_argument('--runtime-rid', action='append', help='Validate a subset; Android never requires host tools')
    args = parser.parse_args()
    if args.targets:
        config = json.loads(args.targets.read_text())
        runtime_rids = {t['rid'] for group in config.values() for t in group}
        tool_rids = {t['rid'] for t in config['desktop']}
    else:
        runtime_rids = set(args.runtime_rid)
        tool_rids = {rid for rid in runtime_rids if not rid.startswith('android-')}
    version = (ROOT / 'VERSION').read_text().strip()
    if args.package:
        with ZipFile(args.package) as archive:
            verify({p for p in archive.namelist() if not p.endswith('/')}, archive.read, runtime_rids, tool_rids, version, True)
    else:
        files = {p.relative_to(args.stage).as_posix() for p in args.stage.rglob('*') if p.is_file()}
        verify(files, lambda p: (args.stage / p).read_bytes(), runtime_rids, tool_rids, version, False)


if __name__ == '__main__':
    main()
