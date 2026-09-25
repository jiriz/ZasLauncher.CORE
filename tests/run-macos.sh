#!/bin/bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
dotnet_bin="${DOTNET:-$HOME/.dotnet/dotnet}"
prefix="${FREERDP_PREFIX:-/opt/homebrew/opt/freerdp}"
if [[ ! -d "$prefix" ]]; then prefix=/usr/local/opt/freerdp; fi
rid=osx-arm64
if [[ "$(uname -m)" == x86_64 ]]; then rid=osx-x64; fi
work="$(mktemp -d "${TMPDIR:-/tmp/}zas-rdp-tests.XXXXXX")"
server_pid=""
cleanup() {
  if [[ -n "$server_pid" ]]; then kill "$server_pid" 2>/dev/null || true; wait "$server_pid" 2>/dev/null || true; fi
  rm -rf "$work"
}
trap cleanup EXIT
"$dotnet_bin" build ZasLauncherGUI/ZasLauncherGUI.csproj -r "$rid" --nologo
"$dotnet_bin" build tests/RdpUi/RdpUi.csproj -p:NativeRdpDir="$root/ZasLauncherGUI/bin/Debug/net10.0/$rid/rdp" --nologo
xcrun clang -std=c11 -g -fsanitize=address,undefined -Wno-deprecated-declarations -Wno-unused-result \
  tests/native-contract.c -I"$prefix/include/freerdp3" -I"$prefix/include/winpr3" \
  -L"$prefix/lib" -lfreerdp-client3 -lfreerdp3 -lwinpr3 -o "$work/contract"
"$work/contract"
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$work/key.pem" -out "$work/cert.pem" \
  -subj '/CN=localhost' -days 1 >/dev/null 2>&1
python3 - "$work" <<'PY'
import struct,sys
from pathlib import Path
w=h=32
pixels=b''.join(bytes((x*8,y*8,200,255)) for y in range(h) for x in range(w))
header=struct.pack('<2sIHHI',b'BM',54+len(pixels),0,0,54)+struct.pack('<IiiHHIIiiII',40,w,h,1,32,0,len(pixels),0,0,0,0)
(Path(sys.argv[1])/'test_icon.bmp').write_bytes(header+pixels)
PY
port=$((40000 + $$ % 20000))
(cd "$work"; exec "$prefix/bin/sfreerdp-server" --local-only --port="$port" --cert="$work/cert.pem" --key="$work/key.pem") >"$work/server.log" 2>&1 &
server_pid=$!
socket="${TMPDIR:-/tmp/}tfreerdp-server.$port"
for ((i=0;i<50;i++)); do [[ -S "$socket" ]] && break; sleep .1; done
# Proxy in native-smoke binds only 127.0.0.1, the sample server uses a local Unix socket.
python3 tests/native-smoke.py "$root/ZasLauncherGUI/bin/Debug/net10.0/$rid/rdp/libzasrdp.dylib" "$socket" \
  "$dotnet_bin" "$root/tests/RdpUi/bin/Debug/net10.0/RdpUi.dll"
