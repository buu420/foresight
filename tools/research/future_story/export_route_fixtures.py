"""Export installed initial terrain and exit-entry neighborhoods for offline routing.

MapTable decoding follows the audited GitExl/CTViewer maps.rs layout. These are
initial resource planes, not a substitute for changed live terrain or actors.
"""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import assets
from audit import EXE_SHA256
from connections import scene_info


def terrain(index):
    raw = assets.read_asset(f'Game/field/MapTable/MapTable_{index:04}.dat')
    sizes = [((raw[0] >> shift) & 3) * 16 + 16 for shift in (0, 2, 4, 6)]
    w1, h1, w2, h2 = sizes
    w3, h3 = (raw[1] & 3) * 16 + 16, ((raw[1] >> 2) & 3) * 16 + 16
    offset = 6 + w1 * h1 + w2 * h2 + (w3 * h3 if raw[1] & 128 else 0)
    width, height = max(w1, w2), max(h1, h2)
    cells = []
    while len(cells) < width * height:
        shape, flags, layer = raw[offset:offset + 3]
        offset += 3
        repeat = 1
        if shape & 128:
            repeat = raw[offset] or 256
            offset += 1
        cells.extend([(shape & 127, flags, layer)] * repeat)
    if len(cells) != width * height or offset != len(raw):
        raise ValueError(f'Map {index}: inconsistent terrain payload')
    return width, height, cells


def export(game, catalog, output):
    assert hashlib.sha256((game / 'Chrono Trigger.exe').read_bytes()).hexdigest().upper() == EXE_SHA256
    assets.RESOURCE = game / 'resources.bin'
    reports, missing = 0, []
    with output.open('w', encoding='utf8') as target:
        for scene in catalog['Scenes']:
            info = scene_info(scene['Id'])
            try:
                width, height, cells = terrain(info['map'])
            except (KeyError, ValueError, IndexError) as error:
                missing.append(dict(Scene=scene['Id'], Map=info['map'], Reason=str(error)))
                continue
            exits = [128] * (width * height)
            seeds = set()
            for e in info['exits']:
                for n in range(e['length']):
                    x = e['source'][0] + (0 if e['vertical'] else n)
                    y = e['source'][1] + (n if e['vertical'] else 0)
                    if not (0 <= x < width and 0 <= y < height):
                        continue
                    exits[y * width + x] = e['id']
                    for dx, dy in ((0, -1), (1, 0), (0, 1), (-1, 0)):
                        if 0 <= x + dx < width and 0 <= y + dy < height:
                            seeds.add(((x + dx) * 256 + 128, (y + dy) * 256 + 128))
            encode = lambda values: base64.b64encode(bytes(values)).decode('ascii')
            record = dict(Scene=scene['Id'], Name=scene['Name'], Seeds=sorted(seeds), Map=dict(
                Width=width, Height=height, CollisionShapes=encode(c[0] for c in cells),
                TerrainFlags=encode(c[1] for c in cells), CollisionLayers=encode(c[2] for c in cells),
                PlayerLayer=1, ExitWidth=width, ExitHeight=height, ExitCells=encode(exits)))
            target.write(json.dumps(record, separators=(',', ':')) + '\n')
            reports += 1
    print(json.dumps(dict(Maps=reports, MissingTerrain=missing)))


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--game-root', type=Path, required=True)
    p.add_argument('--catalog', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    export(a.game_root, json.loads(a.catalog.read_text()), a.output)
