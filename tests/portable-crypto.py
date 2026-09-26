"""Verify portable RDP crypto using only the app's bundled OpenSSL provider."""
import ctypes as C
import os
from pathlib import Path
import subprocess
import sys
import tempfile

if len(sys.argv) < 3:
    with tempfile.TemporaryDirectory(prefix='zas-no-providers-') as empty:
        env = dict(os.environ, OPENSSL_MODULES=empty, OPENSSL_CONF=os.devnull)
        for mode in ['missing', 'bundled']:
            subprocess.run([sys.executable, __file__, sys.argv[1], mode], env=env, check=True)
    raise SystemExit(0)

root = Path(sys.argv[1]).resolve()
lib = C.CDLL(str(root / 'libcrypto.3.dylib'))
lib.OSSL_PROVIDER_load.argtypes = [C.c_void_p, C.c_char_p]
lib.OSSL_PROVIDER_load.restype = C.c_void_p
lib.OSSL_PROVIDER_set_default_search_path.argtypes = [C.c_void_p, C.c_char_p]
lib.OSSL_PROVIDER_set_default_search_path.restype = C.c_int
if sys.argv[2] == 'bundled':
    assert lib.OSSL_PROVIDER_set_default_search_path(None, os.fsencode(root)) == 1
provider = lib.OSSL_PROVIDER_load(None, b'legacy')
if sys.argv[2] == 'missing':
    assert not provider, 'Negative control unexpectedly found a system provider'
    print('PASS: missing-provider negative control')
    raise SystemExit(0)
assert provider, 'Bundled legacy provider failed to load'
lib.EVP_Q_digest.argtypes = [C.c_void_p, C.c_char_p, C.c_char_p, C.c_void_p, C.c_size_t, C.c_void_p, C.POINTER(C.c_size_t)]
lib.EVP_Q_digest.restype = C.c_int
out = (C.c_ubyte * 64)()
length = C.c_size_t()
assert lib.EVP_Q_digest(None, b'MD4', None, b'', 0, out, C.byref(length)) == 1
assert bytes(out[:length.value]).hex() == '31d6cfe0d16ae931b73c59d7e0c089c0'
lib.EVP_CIPHER_fetch.argtypes = [C.c_void_p, C.c_char_p, C.c_char_p]
lib.EVP_CIPHER_fetch.restype = C.c_void_p
cipher = lib.EVP_CIPHER_fetch(None, b'RC4', None)
assert cipher, 'NTLM RC4 unavailable'
lib.EVP_CIPHER_free.argtypes = [C.c_void_p]
lib.EVP_CIPHER_free(cipher)
lib.OSSL_PROVIDER_unload.argtypes = [C.c_void_p]
lib.OSSL_PROVIDER_unload(provider)
print('PASS: bundled provider, MD4 known vector and RC4 without system module path')
