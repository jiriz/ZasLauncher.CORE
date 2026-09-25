#!/bin/bash
set -euo pipefail
out="$1"
rid="${2:-}"
source_dir="$(cd "$(dirname "$0")" && pwd)"
prefix="${FREERDP_PREFIX:-/opt/homebrew/opt/freerdp}"
if [[ ! -d "$prefix/include" ]]; then prefix="/usr/local/opt/freerdp"; fi
if [[ ! -d "$prefix/include" ]]; then echo 'FreeRDP 3 is required to build: brew install freerdp' >&2; exit 1; fi
arch="$(uname -m)"
if [[ "$rid" == osx-x64 ]]; then arch=x86_64; elif [[ "$rid" == osx-arm64 ]]; then arch=arm64; fi
mkdir -p "$out"
xcrun clang -std=c11 -O2 -Wall -Wextra -Werror -Wno-unused-result -Wno-deprecated-declarations -fvisibility=hidden -arch "$arch" \
  -dynamiclib "$source_dir/zas_rdp.c" -o "$out/libzasrdp.dylib" \
  -I"$prefix/include/freerdp3" -I"$prefix/include/winpr3" \
  -L"$prefix/lib" -lfreerdp-client3 -lfreerdp3 -lwinpr3 \
  -Wl,-install_name,@rpath/libzasrdp.dylib
python3 "$source_dir/bundle.py" "$out"
