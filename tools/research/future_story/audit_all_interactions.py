"""Independent inventory of every actor's item, money and menu entry closure.

Unlike report_catalog_coverage this starts from ALL native actors, not the
generated catalog. Branch conditions are not interpreted, so output establishes
entry coverage, not current availability or promised rewards after a battle.
"""
import argparse
import hashlib
import json
from pathlib import Path
import assets
from audit import EXE_SHA256
from decode_verified import packet, walk


def report(game, catalog_path):
    assert hashlib.sha256((game / 'Chrono Trigger.exe').read_bytes()).hexdigest().upper() == EXE_SHA256
    assets.RESOURCE = game / 'resources.bin'
    catalog = json.loads(catalog_path.read_text())
    rows, missing = [], []
    for scene in catalog['Scenes']:
        if scene['Id'] in catalog['MissingScripts']:
            continue
        code, entries, base = packet(scene['Id'])
        for actor, functions in enumerate(entries):
            init = walk(code, [functions[0]])
            loads = {op for op, a in init.values() if a is not None and op in (0x81, 0x82, 0x83)}
            party = any(op in (0x57, 0x5c, 0x62, 0x68, 0x6a, 0x6c, 0x6d, 0x80) for op, a in init.values())
            positioned = any(op in (0x8b, 0x8d) for op, a in init.values())
            physical = bool(loads) or positioned and not party
            for fn in (1, 2):
                pending, seen, effects = [functions[fn]], set(), set()
                while pending:
                    entry = pending.pop()
                    if entry in seen:
                        continue
                    seen.add(entry)
                    for pc, (op, a) in walk(code, [entry]).items():
                        if a is None:
                            raise ValueError(f'Unknown opcode: scene{scene["Id"]} actor{actor} at{base+pc:X}')
                        if op in (0xc7, 0xca, 0xcd, 0xc8):
                            effects.add((op, base + pc))
                        if op in (2, 3, 4) and a[0] // 2 < len(entries):
                            pending.append(entries[a[0] // 2][a[1] & 15])
                if not effects:
                    continue
                native = next((a for a in scene['Actors'] if a['Id'] == actor), None)
                row = dict(Scene=scene['Id'], Actor=actor, Function=fn, Physical=physical,
                           CatalogActor=native is not None, Effects=sorted(effects),
                           Classification='native destination' if native else 'party/controller entry' if not physical else 'missing destination')
                rows.append(row)
                if physical and native is None:
                    missing.append(row)
    return dict(ExecutableSha256=EXE_SHA256, CatalogSha256=hashlib.sha256(catalog_path.read_bytes()).hexdigest(),
                EntryCount=len(rows), PhysicalEntries=sum(r['Physical'] for r in rows),
                MissingPhysicalDestinations=missing, Entries=rows,
                Limits='All talk/contact entries and ordinary delegated calls, branch-insensitive. Automatic post-battle/cinematic rewards are effects, not extra pickup coordinates. Party-indexed request opcodes05..07 do not identify a fixed actor. Runtime still checks call gates, flags and live geometry.')


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--game-root', type=Path, required=True)
    p.add_argument('--catalog', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    result = report(a.game_root, a.catalog)
    a.output.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({k: v for k, v in result.items() if k not in ('Entries', 'Limits')}))
    raise SystemExit(bool(result['MissingPhysicalDestinations']))
