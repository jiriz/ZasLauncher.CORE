"""Wire test: both clipboard directions, through a real negotiated RDP channel.
Uses the clipboard-enabled local sample peer, not the user's macOS pasteboard.
"""
import runpy
import pathlib
import ctypes as C
import tempfile
import struct
import time

# Reuse the localhost proxy and first run the existing connection/input tests.
m = runpy.run_path(str(pathlib.Path(__file__).with_name('native-smoke.py')))
lib, port, start, close, wait_frame = (m[k] for k in ('lib','port','start','close','wait_frame'))
lib.zr_set_clip.argtypes=[C.c_void_p,C.c_void_p,C.c_int]
lib.zr_clip_sent.argtypes=[C.c_void_p]
lib.zr_clip_info.argtypes=[C.c_void_p,C.POINTER(C.c_int),C.POINTER(C.c_uint64),C.c_void_p,C.c_int]
lib.zr_set_files.argtypes=[C.c_void_p,C.c_void_p,C.c_int,C.POINTER(C.c_char_p),C.c_int]
lib.zr_file_start.argtypes=[C.c_void_p,C.c_uint64,C.c_uint32,C.c_uint64,C.c_uint32]
lib.zr_file_read.argtypes=[C.c_void_p,C.c_uint64,C.c_void_p,C.c_int]
def wait(test):
    deadline=time.monotonic()+10
    while time.monotonic()<deadline:
        value=test()
        if value: return value
        time.sleep(.02)
    raise AssertionError('Clipboard wire transfer timed out')
def snapshot(kind):
    k,g=C.c_int(),C.c_uint64();buf=C.create_string_buffer(4096)
    n=lib.zr_clip_info(h,C.byref(k),C.byref(g),buf,len(buf))
    return (g.value,buf.raw[:n]) if k.value==kind and n>0 else None
h,t=start(port)
try:
    wait_frame(h)
    data='LOCAL'.encode('utf-16-le')
    assert lib.zr_set_clip(h,data,len(data))
    wait(lambda:lib.zr_clip_sent(h)==1)
    _,text=wait(lambda:snapshot(1))
    assert text.decode('utf-16-le').rstrip('\0')=='WIRE'
    # The server only offers its remote content after validating the local bytes.
    with tempfile.TemporaryDirectory(prefix='zas-wire-') as directory:
        path=pathlib.Path(directory)/'local.txt';payload=b'wire file bytes';path.write_bytes(payload)
        descriptor=bytearray(596);struct.pack_into('<I',descriptor,0,1)
        struct.pack_into('<I',descriptor,4,0x44);struct.pack_into('<I',descriptor,40,0x80)
        struct.pack_into('<I',descriptor,72,len(payload))
        name='local.txt'.encode('utf-16-le');descriptor[76:76+len(name)]=name
        paths=(C.c_char_p*1)(str(path).encode())
        assert lib.zr_set_files(h,bytes(descriptor),len(descriptor),paths,1)
        wait(lambda:lib.zr_clip_sent(h)==1)
        generation,remote=wait(lambda:snapshot(2))
        assert len(remote)==596 and remote[76:].decode('utf-16-le').rstrip('\0')=='remote.txt'
        assert lib.zr_file_start(h,generation,0,0,len(payload))
        buf=C.create_string_buffer(100)
        count=wait(lambda:max(0,lib.zr_file_read(h,generation,buf,len(buf))))
        assert buf.raw[:count]==payload
    print('PASS: real RDP clipboard channel, format ACK, text and file bytes in BOTH directions',flush=True)
finally: close(h,t)
