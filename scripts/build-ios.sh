#!/usr/bin/env bash
# Copyright(c) 2026 Cyclone DDS C# contributors
#
# This program and the accompanying materials are made available under the
# terms of the Eclipse Public License v. 2.0 which is available at
# http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
# v. 1.0 which is available at
# http://www.eclipse.org/org/documents/edl-v10.php.
#
# SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
set -euo pipefail
if [[ $# -ne 1 ]]; then
  printf 'Usage: %s ios-{arm64} | iossimulator-{arm64,x64}\n' "$0" >&2
  exit 1
fi
case "$1" in
  ios-arm64) sdk=iphoneos; arch=arm64 ;;
  iossimulator-arm64) sdk=iphonesimulator; arch=arm64 ;;
  iossimulator-x64) sdk=iphonesimulator; arch=x86_64 ;;
  *) printf 'Unsupported iOS RID: %s\n' "$1" >&2; exit 1 ;;
esac
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
source_dir="$repo_dir/build/_deps/cyclonedds-src"
ios_dir="$repo_dir/build/ios/$1"
install_dir="$ios_dir/cyclonedds-install"
apple_args=(
  -DCMAKE_SYSTEM_NAME=iOS
  "-DCMAKE_OSX_SYSROOT=$sdk"
  "-DCMAKE_OSX_ARCHITECTURES=$arch"
  -DCMAKE_OSX_DEPLOYMENT_TARGET=13.0
)
bash "$repo_dir/scripts/bootstrap-native.sh" --source-only
cmake -S "$source_dir" -B "$ios_dir/cyclonedds-build" \
  "${apple_args[@]}" \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$install_dir" \
  -DBUILD_SHARED_LIBS=ON -DBUILD_IDLC=OFF -DBUILD_TESTING=OFF \
  -DBUILD_EXAMPLES=OFF -DBUILD_DDSPERF=OFF -DENABLE_SECURITY=OFF -DENABLE_SSL=OFF
cmake --build "$ios_dir/cyclonedds-build" --parallel "${CMAKE_BUILD_PARALLEL_LEVEL:-4}"
cmake --install "$ios_dir/cyclonedds-build"
cmake -S "$repo_dir" -B "$ios_dir/binding" \
  "${apple_args[@]}" \
  -DCMAKE_BUILD_TYPE=Release -DCycloneDDS_DIR="$install_dir/lib/cmake/CycloneDDS" \
  -DCYCLONEDDS_SOURCE_DIR="$source_dir" \
  -DBUILD_IDLCSHARP=OFF -DBUILD_TESTING=OFF -DBUILD_CSHARP_EXAMPLES=OFF
cmake --build "$ios_dir/binding" --parallel "${CMAKE_BUILD_PARALLEL_LEVEL:-4}"
python3 "$repo_dir/scripts/pack-package.py" --prefix "$install_dir" \
  --build "$ios_dir/binding" --rid "$1" --runtime-only --stage-only