"""All-resource treasure audit against the native grid builder 0x179690.

Run from the repository root (read-only):
  py -3 -X utf8 tools/research/future_story/audit_treasures.py

Native facts (0x179690, FieldEnvironmentCapture.TryTreasures):
  * the scene's record range is TakaraOffsetTbl[scene]..[scene+1]; while its first record is
    (0, 0), the third field names another scene whose range is used instead (an alias);
  * FieldState+0x2190/0x2194 = resolved [first, last); grid[y*w + x] = record - first;
  * an opened record whose cell has collision shape bit 0 swaps FE/EE/E0/F0 to FF/EF/E1/F1.
TryTreasures lists a record while its open bit is clear: as "Treasure chest" when the cell's
shape bit 0 is set and the tile is FE/EE/E0/F0, else (with the guide active) "Item pickup".
This script checks every one of the 343 records against the installed map planes.
"""
import json
import struct
import sys
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
REPORTS = REPO / "artifacts" / "research" / "content-coverage-0335" / "claude"
REPORTS.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(REPO / "tools" / "research" / "future_story"))
import assets  # noqa: E402

assets.RESOURCE = REPO.parent / "resources.bin"
from connections import scene_info, names  # noqa: E402
from export_route_fixtures import terrain  # noqa: E402


def layers(index):
    raw = assets.read_asset(f"Game/field/MapTable/MapTable_{index:04}.dat")
    sizes = [((raw[0] >> shift) & 3) * 16 + 16 for shift in (0, 2, 4, 6)]
    w1, h1, w2, h2 = sizes
    one = raw[6:6 + w1 * h1]
    two = raw[6 + w1 * h1:6 + w1 * h1 + w2 * h2]
    return (w1, h1, one), (w2, h2, two)


def main():
    offsets_raw = assets.read_asset("Game/common/TakaraOffsetTbl.dat")
    data = assets.read_asset("Game/common/TakaraDataTbl.dat")
    total, = struct.unpack_from("<I", data)
    records = [struct.unpack_from("<BBHH", data, 4 + i * 6) for i in range(total)]
    count, = struct.unpack_from("<I", offsets_raw) if len(offsets_raw) % 2 == 0 and False else (None,)
    # The offset table layout is probed rather than assumed: u16 per scene, optional u32 count.
    candidates = []
    for header in (0, 4):
        values = [struct.unpack_from("<H", offsets_raw, header + i * 2)[0] for i in range((len(offsets_raw) - header) // 2)]
        if values and all(a <= b for a, b in zip(values, values[1:])) and values[-1] <= total:
            candidates.append((header, values))
    header, offsets = candidates[0]
    scene_names = names()
    report = []
    owners = defaultdict(list)
    for scene in range(len(offsets) - 1):
        begin, end = offsets[scene], offsets[scene + 1]
        chain = [scene]
        while begin < end and records[begin][0] == 0 and records[begin][1] == 0:
            target = records[begin][2]
            if target in chain or target + 1 >= len(offsets):
                break
            chain.append(target)
            begin, end = offsets[target], offsets[target + 1]
        if begin >= end:
            continue
        try:
            info = scene_info(scene)
            width, height, cells = terrain(info["map"])
            (w1, h1, one), (w2, h2, two) = layers(info["map"])
        except Exception as error:  # noqa: BLE001 - report every unreadable scene
            report.append(dict(scene=scene, error=str(error)))
            continue
        used = Counter((records[i][0], records[i][1]) for i in range(begin, end))
        for index in range(begin, end):
            x, y, contents, extra = records[index]
            inside = x < width and y < height
            shape = cells[y * width + x][0] if inside else None
            tile1 = one[y * w1 + x] if x < w1 and y < h1 else None
            tile2 = two[y * w2 + x] if x < w2 and y < h2 else None
            chest_shape = shape is not None and (shape & 1) != 0
            closed = tile1 in (0xFE, 0xEE, 0xE0, 0xF0)
            kind = "chest" if chest_shape and closed else \
                "chest-shape-open-tile" if chest_shape else "guide-pickup"
            row = dict(scene=scene, name=scene_names.get(scene), aliasChain=chain, record=index,
                       x=x, y=y, inside=inside, shape=shape, tile1=tile1, tile2=tile2, kind=kind,
                       sharedCell=used[(x, y)] > 1)
            report.append(row)
            owners[index].append(scene)
    rows = [r for r in report if "record" in r]
    print(f"records: {total}; scenes with treasure: {len({r['scene'] for r in rows})}; "
          f"rows (incl. aliases): {len(rows)}; unreadable: {[r for r in report if 'error' in r][:5]}")
    print("offset header bytes:", header, "scenes in table:", len(offsets) - 1)
    print("kinds:", Counter(r["kind"] for r in rows))
    print("records never owned by a scene:", sorted(set(range(total)) - set(owners)))
    print("aliased scenes:", sorted({(r['scene'], tuple(r['aliasChain'])) for r in rows if len(r['aliasChain']) > 1}))
    for r in rows:
        if not r["inside"] or r["sharedCell"] or r["kind"] != "chest":
            print("  ", r)
    (REPORTS / "treasure-audit.json").write_text(json.dumps(rows, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
