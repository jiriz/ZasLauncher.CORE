"""Integration test: run against a local FreeRDP sample server only.
Usage: python3 tests/native-smoke.py /path/to/libzasrdp.dylib /path/to/tfreerdp-server.33987
No credentials, remote hosts, or user clipboard are accessed.
"""
import ctypes as C
import sys
import os
import subprocess
import threading
import time
import socket
import socketserver
import select

class Proxy(socketserver.BaseRequestHandler):
    def handle(self):
        with socket.socket(socket.AF_UNIX) as remote:
            remote.connect(sys.argv[2])
            pair = [self.request, remote]
            while True:
                ready, _, _ = select.select(pair, [], [], 10)
                for source in ready:
                    data = source.recv(65536)
                    if not data: return
                    (remote if source is self.request else self.request).sendall(data)

proxy = socketserver.ThreadingTCPServer(("127.0.0.1", 0), Proxy)
proxy.daemon_threads = True
threading.Thread(target=proxy.serve_forever, daemon=True).start()
port = proxy.server_address[1]
lib = C.CDLL(sys.argv[1])
lib.zr_create.argtypes = [C.c_char_p, C.c_int, C.c_char_p, C.c_char_p, C.c_int, C.c_int, C.c_int]
lib.zr_create.restype = C.c_void_p
for fn in ('zr_run', 'zr_state'):
    getattr(lib, fn).argtypes = [C.c_void_p]
for fn in ('zr_stop', 'zr_free'):
    getattr(lib, fn).argtypes = [C.c_void_p]
    getattr(lib, fn).restype = None
lib.zr_input.argtypes = [C.c_void_p, C.c_int, C.c_int, C.c_int, C.c_int]
lib.zr_frame.argtypes = [C.c_void_p, C.c_void_p, C.c_int, C.c_int, C.c_int,
                       C.POINTER(C.c_int), C.POINTER(C.c_int), C.POINTER(C.c_uint64)]
def start(port):
    handle = lib.zr_create(b'127.0.0.1', port, b'test', b'', 1024, 768, 0x409)
    assert handle
    t = threading.Thread(target=lib.zr_run, args=(handle,))
    t.start()
    return handle, t

def frame(handle):
    width, height, serial = C.c_int(), C.c_int(), C.c_uint64()
    lib.zr_frame(handle, None, 0, 0, 0, C.byref(width), C.byref(height), C.byref(serial))
    if not width.value: return None
    pixels = C.create_string_buffer(width.value * height.value * 4)
    copied = lib.zr_frame(handle, pixels, width.value * 4, width.value, height.value,
                         C.byref(width), C.byref(height), C.byref(serial))
    assert copied and serial.value
    assert lib.zr_frame(handle, pixels, width.value * 4, width.value, height.value,
                        C.byref(width), C.byref(height), C.byref(serial)) in (0, 1)
    return pixels.raw

def wait_frame(handle):
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        state = lib.zr_state(handle)
        if state == 4: raise AssertionError('RDP connection failed')
        pixels = frame(handle)
        if pixels and len(set(pixels)) > 1: return pixels
        time.sleep(.05)
    raise AssertionError('No rendered frame')

def close(handle, thread):
    lib.zr_stop(handle)
    thread.join(15)
    assert not thread.is_alive(), 'Worker did not terminate'
    lib.zr_free(handle)

sessions = []
try:
    for _ in range(2):
        h, t = start(port); sessions.append((h,t)); wait_frame(h)
    a, b = sessions
    before = frame(b[0])
    for kind, x, y, z in [(1,0x0800,50,50),(1,0x9000,50,50),(1,0x1000,50,50),
                          (2,0x1e,1,0),(2,0x1e,0,0),(4,1200,800,0)]:
        assert lib.zr_input(b[0],kind,x,y,z)
    deadline = time.monotonic() + 5
    while frame(b[0]) == before and time.monotonic() < deadline: time.sleep(.02)
    assert frame(b[0]) != before, 'Mouse input did not update remote image'
    # Sample server changes resolution on G. This verifies scan-code input AND framebuffer resizing.
    assert lib.zr_input(b[0], 2, 0x22, 1, 0)
    assert lib.zr_input(b[0], 2, 0x22, 0, 0)
    deadline = time.monotonic() + 5
    while len(frame(b[0]) or b'') != 800*600*4 and time.monotonic() < deadline: time.sleep(.02)
    assert len(frame(b[0])) == 800*600*4, 'Server resize was not applied'
    close(*a); sessions.remove(a)
    assert lib.zr_state(b[0]) == 2, 'Closing tab A terminated tab B'
    assert frame(b[0])
    close(*b); sessions.remove(b)
    # Reconnect and close while connecting. Also cover cancellation before run starts.
    h,t = start(port); sessions.append((h,t)); wait_frame(h)
    close(h,t); sessions.clear()
    for _ in range(10):
        h = lib.zr_create(b'127.0.0.1', port, b'test', b'', 1024,768,0x409)
        lib.zr_stop(h); assert lib.zr_run(h) == 0; lib.zr_free(h)
    print('PASS: two independent rendered sessions, input, resize request, reconnect, cancellation, disposal', flush=True)
    if len(sys.argv) > 3:
        env = dict(os.environ, ZAS_RDP_TEST_PORT=str(port))
        subprocess.run(sys.argv[3:], env=env, check=True, timeout=60)
finally:
    for item in sessions: close(*item)
