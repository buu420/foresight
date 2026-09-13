"""Read-only installed-PC area audit. Run with --game-root and --output.

This reports native script facts and map connections; it does not emulate game
state, control the game, extract images, or print the dialogue text.
"""
import argparse
import hashlib
import json
from pathlib import Path

import assets
from connections import scene_info, world
from decode_verified import packet, actor_ops, walk

EXE_SHA256 = "8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7"
SCENES = [208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221,
          223, 224, 225, 226, 227, 228, 229, 230, 231, 232, 233, 234, 235, 259, 344, 437, 464, 465]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    game = args.game_root.resolve(strict=True)
    digest = hashlib.sha256((game / "Chrono Trigger.exe").read_bytes()).hexdigest().upper()
    if digest != EXE_SHA256:
        raise SystemExit("Unsupported executable hash; re-audit the native instruction handlers first.")
    assets.RESOURCE = game / "resources.bin"
    archive_count = len(assets.directory())
    result = []
    for scene in SCENES:
        code, entries, base = packet(scene)
        actors = []
        for index, entry in enumerate(entries):
            instructions = actor_ops(code, entry)
            unknown = [(base + pc, op) for pc, (op, operands) in instructions.items() if operands is None]
            if unknown:
                raise SystemExit(f"Unknown instruction boundaries in scene {scene}, actor {index}: {unknown}")
            loads = [dict(offset=hex(base + pc), opcode=hex(op), operands=a.hex())
                     for pc, (op, a) in sorted(walk(code, [entry[0]]).items()) if op in (0x81, 0x82, 0x83) and a is not None]
            progression = [dict(offset=hex(base + pc), value=a[0])
                           for pc, (op, a) in sorted(instructions.items())
                           if a is not None and (op == 0x5A or op == 0x56 and a[1:] == b"\0\0")]
            flags = [dict(offset=hex(base + pc), set=op == 0x65,
                          index=a[1] + (256 if a[0] & 128 else 0), mask=1 << (a[0] & 15))
                     for pc, (op, a) in sorted(instructions.items()) if op in (0x65, 0x66) and a is not None]
            actors.append(dict(actor=index, entries=[hex(base + p) for p in entry],
                               loads=loads, progress_writes=progression, bit_writes=flags))
        result.append(dict(**scene_info(scene), actors=actors))
    payload = dict(executable_sha256=digest, archive_entries=archive_count, scenes=result, world2=world(2))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(f"Validated ARC1 archive ({archive_count} entries), {len(result)} scenes, and all decoded instruction boundaries.")
    print(f"Research report: {args.output.resolve()}")


if __name__ == "__main__":
    main()
