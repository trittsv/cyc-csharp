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
  printf 'Usage: ANDROID_NDK_HOME=/path/to/ndk %s android-{arm64,x64,arm,x86}\n' "$0" >&2
  exit 1
fi
case "$1" in
  android-arm64) abi=arm64-v8a ;;
  android-x64) abi=x86_64 ;;
  android-arm) abi=armeabi-v7a ;;
  android-x86) abi=x86 ;;
  *) printf 'Unsupported Android RID: %s\n' "$1" >&2; exit 1 ;;
esac
: "${ANDROID_NDK_HOME:?Set ANDROID_NDK_HOME to an installed Android NDK}"
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
source_dir="$repo_dir/build/_deps/cyclonedds-src"
android_dir="$repo_dir/build/android/$1"
install_dir="$android_dir/cyclonedds-install"
toolchain="$ANDROID_NDK_HOME/build/cmake/android.toolchain.cmake"
if [[ ! -f "$toolchain" ]]; then
  printf 'Missing Android NDK toolchain: %s\n' "$toolchain" >&2
  exit 1
fi
bash "$repo_dir/scripts/bootstrap-native.sh" --source-only
cmake -S "$source_dir" -B "$android_dir/cyclonedds-build" \
  -DCMAKE_TOOLCHAIN_FILE="$toolchain" -DANDROID_ABI="$abi" -DANDROID_PLATFORM=android-28 \
  -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON \
  -DCMAKE_SHARED_LINKER_FLAGS=-Wl,-z,max-page-size=16384 \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$install_dir" \
  -DBUILD_SHARED_LIBS=ON -DBUILD_IDLC=OFF -DBUILD_TESTING=OFF \
  -DBUILD_EXAMPLES=OFF -DBUILD_DDSPERF=OFF -DENABLE_SECURITY=OFF -DENABLE_SSL=OFF
cmake --build "$android_dir/cyclonedds-build" --parallel "${CMAKE_BUILD_PARALLEL_LEVEL:-4}"
cmake --install "$android_dir/cyclonedds-build"
cmake -S "$repo_dir" -B "$android_dir/binding" \
  -DCMAKE_TOOLCHAIN_FILE="$toolchain" -DANDROID_ABI="$abi" -DANDROID_PLATFORM=android-28 \
  -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON \
  -DCMAKE_SHARED_LINKER_FLAGS=-Wl,-z,max-page-size=16384 \
  -DCMAKE_BUILD_TYPE=Release -DCycloneDDS_DIR="$install_dir/lib/cmake/CycloneDDS" \
  -DCYCLONEDDS_SOURCE_DIR="$source_dir" \
  -DBUILD_IDLCSHARP=OFF -DBUILD_TESTING=OFF -DBUILD_CSHARP_EXAMPLES=OFF
cmake --build "$android_dir/binding" --parallel "${CMAKE_BUILD_PARALLEL_LEVEL:-4}"
python3 "$repo_dir/scripts/pack-package.py" --prefix "$install_dir" \
  --build "$android_dir/binding" --rid "$1" --runtime-only --stage-only
