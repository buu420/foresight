"""Offline scene connections, using the installed PC tables, no game control."""
import json
from pathlib import Path
import struct
from assets import read_asset, directory

ROOT = Path(__file__).parent

def names():
    return {int(line.split(',')[0].rsplit('_', 1)[1]): line.split(',', 1)[1]
            for line in read_asset('Localize/en/msg/debug_map.txt').decode().splitlines()}

def exits(scene):
    offsets = read_asset('Game/common/MapJumpOffsetTbl.dat')
    data = read_asset('Game/common/MapJumpDataTbl.dat')
    count, = struct.unpack_from('<I', offsets)
    assert scene + 1 < count
    start, end = struct.unpack_from('<HH', offsets, 4 + scene * 2)
    result = []
    for local_id, record in enumerate(range(start, end)):
        x, y, shape, facing, dest, dx, dy = struct.unpack_from('<BBBBHBB', data, 4 + record * 8)
        result.append(dict(id=local_id, record=record, source=[x, y], length=(shape & 127) + 1,
                           vertical=bool(shape & 128), facing=facing, destination=dest, arrival=[dx, dy]))
    return result

def scene_info(scene):
    h = read_asset(f'Game/field/Mapinfo/mapinfo_{scene}.dat')
    return dict(scene=scene, name=names().get(scene), script=struct.unpack_from('<H', h, 16)[0],
                map=struct.unpack_from('<H', h, 12)[0], exits=exits(scene))

def world(world_index):
    data = read_asset(f'Game/world/EventTable/EventTable_{world_index:04}.dat')
    labels = read_asset('Localize/en/msg/w_map.txt').decode().splitlines()
    result = []
    for i in range(data[0]):
        x, y, name, dest, facing, dx, dy = struct.unpack_from('<BBBHBBB', data, 1 + i * 8)
        result.append(dict(id=i, source=[x & 127, y & 63], enabled=bool(x & 128), name_index=name,
                           name=labels[name], destination=dest, facing=facing, arrival=[dx,dy]))
    return result
