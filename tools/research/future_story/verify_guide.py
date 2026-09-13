"""Verify optional guide actor loads and exit identities against the installed PC data.

Run with --game-root and --output. No game control or dialogue extraction.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

import assets
from audit import EXE_SHA256
from connections import exits
from decode_verified import actor_ops, packet


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-root', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    game = args.game_root.resolve(strict=True)
    digest = hashlib.sha256((game / 'Chrono Trigger.exe').read_bytes()).hexdigest().upper()
    if digest != EXE_SHA256:
        raise SystemExit('Unsupported executable hash; re-audit native handlers first.')
    assets.RESOURCE = game / 'resources.bin'
    root = Path(__file__).resolve().parents[3]
    navigation = root / 'src/ChronoTriggerAccessibility.Mod/Navigation'
    labels = (navigation / 'OptionalGuideTargets.cs').read_text(encoding='utf-8')
    rows = re.findall(r'\((\d+), (\d+), (\d+), (\d+)\) => "([^"]+)"', labels)
    actors = []
    for scene, slot, kind, visual, label in rows:
        scene, slot, kind, visual = map(int, (scene, slot, kind, visual))
        code, entries, base = packet(scene)
        loads = []
        for pc, (op, operands) in actor_ops(code, entries[slot]).items():
            if operands is None or op not in (0x81, 0x82, 0x83):
                continue
            actual_kind = {0x81: 3, 0x82: 4, 0x83: 5}[op]
            actual_visual = int.from_bytes(operands[:2] if op == 0x83 else operands[:1], 'little')
            if (actual_kind, actual_visual) == (kind, visual):
                loads.append(hex(base + pc))
        if not loads:
            raise SystemExit(f'No matching native load for scene {scene}, actor {slot}: {kind}/{visual}')
        actors.append(dict(scene=scene, actor=slot, label=label, kind=kind, visual=visual, offsets=sorted(loads)))

    area_source = (navigation / 'OptionalGuideAreas.cs').read_text(encoding='utf-8')
    definitions = re.findall(r'\((\d+), (\d+)\) => "([^"]+)"', area_source)
    area_exits = []
    for scene, exit_id, label in definitions:
        scene, exit_id = int(scene), int(exit_id)
        record = next((e for e in exits(scene) if e['id'] == exit_id), None)
        if record is None:
            raise SystemExit(f'No native exit {exit_id} in scene {scene}')
        area_exits.append(dict(scene=scene, label=label, **record))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(dict(executable_sha256=digest, actors=actors, exits=area_exits), indent=2) + '\n', encoding='utf-8')
    print(f'Verified {len(actors)} optional actor load identities and {len(area_exits)} optional exits.')


if __name__ == '__main__':
    main()
