#!/bin/bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
: "${FREERDP_SOURCE:?Set FREERDP_SOURCE to the official FreeRDP 3.26.0 source checkout}"
prefix="${FREERDP_PREFIX:-/opt/homebrew/opt/freerdp}"
work="$(mktemp -d "${TMPDIR:-/tmp/}zas-clipboard-wire.XXXXXX")"
server_pid=""
trap 'if [[ -n "$server_pid" ]]; then kill "$server_pid" 2>/dev/null || true; wait "$server_pid" 2>/dev/null || true; fi; rm -rf "$work"' EXIT
python3 tests/build-clipboard-peer.py "$FREERDP_SOURCE" "$work/peer" "$prefix"
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$work/key.pem" -out "$work/cert.pem" -subj '/CN=localhost' -days 1 >/dev/null 2>&1
port=$((40000 + $$ % 20000))
(cd "$work/peer"; exec ./server --local-only --port="$port" --cert="$work/cert.pem" --key="$work/key.pem") >"$work/peer.log" 2>&1 &
server_pid=$!
socket="${TMPDIR:-/tmp/}tfreerdp-server.$port"
for ((i=0;i<50;i++)); do [[ -S "$socket" ]] && break; sleep .1; done
native="${NATIVE_RDP_LIB:-$root/ZasLauncherGUI/bin/Debug/net10.0/osx-arm64/rdp/libzasrdp.dylib}"
python3 tests/native-clipboard.py "$native" "$socket" || { cat "$work/peer.log"; exit 1; }
