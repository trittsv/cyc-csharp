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
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
revision=93cad8bec019e09b68dfe8dec626a9e16983e259
source_dir="$repo_dir/build/_deps/cyclonedds-src"
binary_dir="$repo_dir/build/_deps/cyclonedds-build"
install_dir="$repo_dir/build/_deps/cyclonedds-install"
if [[ ! -d "$source_dir/.git" ]]; then
  mkdir -p "$source_dir"
  git -C "$source_dir" init
  git -C "$source_dir" remote add origin https://github.com/eclipse-cyclonedds/cyclonedds.git
  git -C "$source_dir" fetch --depth 1 origin "$revision"
  git -C "$source_dir" checkout --detach FETCH_HEAD
fi
if [[ "$(git -C "$source_dir" rev-parse HEAD)" != "$revision" ]]; then
  printf '%s\n' "Native source revision differs from the pinned revision: $source_dir" >&2
  exit 1
fi
if [[ -n "$(git -C "$source_dir" status --porcelain)" ]]; then
  printf '%s\n' "Native source has local modifications: $source_dir" >&2
  exit 1
fi
if [[ "${1:-}" == "--source-only" ]]; then
  printf 'Pinned native source: %s\n' "$source_dir"
  exit 0
fi
if [[ $# -gt 0 ]]; then
  printf 'Usage: %s [--source-only]\n' "$0" >&2
  exit 1
fi
cmake -S "$source_dir" -B "$binary_dir" \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$install_dir" \
  '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded$<$<CONFIG:Debug>:Debug>DLL' \
  -DBUILD_SHARED_LIBS=ON -DBUILD_TESTING=OFF -DBUILD_EXAMPLES=OFF -DBUILD_DDSPERF=OFF \
  -DENABLE_SECURITY=OFF -DENABLE_SSL=OFF
cmake --build "$binary_dir" --parallel
cmake --install "$binary_dir"
printf '\nNative installation: %s\n' "$install_dir"
