"""Read-only PC resources research, format checked against GitExl/CTViewer.

Reference: src/filesystem/resourcesbin.rs (CTExplore-derived ARC1 format).
Nothing is written to the game or extracted outside the research workspace.
"""
import functools
import gzip
from pathlib import Path
import struct

RESOURCE = Path("resources.bin")  # Set by audit.py before the first read.

def decode(data, block_seed):
    seed = (0x19000000 + block_seed) & 0xffffffff
    out = bytearray(len(data))
    for i, value in enumerate(data):
        seed = (seed * 0x41c64e6d + 12345) & 0xffffffff
        out[i] = value ^ (seed >> 24)
    return bytes(out)

def block(offset, length):
    with RESOURCE.open('rb') as f:
        f.seek(offset)
        encoded = f.read(length)
    assert len(encoded) == length
    data = decode(encoded, offset)
    expected, = struct.unpack_from('>I', data)
    unpacked = gzip.decompress(data[4:])
    assert len(unpacked) == expected
    return unpacked

@functools.lru_cache(maxsize=1)
def directory():
    with RESOURCE.open('rb') as f:
        header = decode(f.read(16), 0)
    signature, size, offset, length = struct.unpack('<4sIII', header)
    assert signature == b'ARC1' and size == RESOURCE.stat().st_size
    assert offset + length <= size
    data = block(offset, length)
    count, = struct.unpack_from('<I', data)
    entries = {}
    for i in range(count):
        path_offset, start, size = struct.unpack_from('<III', data, 4 + i * 12)
        path = data[path_offset:data.index(b'\0', path_offset)].decode('utf-8')
        assert path not in entries and start + size <= RESOURCE.stat().st_size
        entries[path] = (start, size)
    return entries

@functools.lru_cache(maxsize=128)
def read_asset(path):
    return block(*directory()[path])
