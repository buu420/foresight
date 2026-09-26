"""Extract the entire installed ARC1 archive and index every field event script.

Research output only. Does not change the game or accessibility catalog. Script
listings retain raw operands; unsupported or unreachable bytes remain in raw files.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path, PurePosixPath
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'future_story'))
import assets
from audit import EXE_SHA256
from decode_verified import actor_ops, CONDITIONAL


NAMES = {0: 'return', 2: 'request', 3: 'request_wait', 4: 'request_end',
         0x10: 'jump_forward', 0x11: 'jump_backward', 0x12: 'compare_local',
         0x16: 'compare_global', 0x18: 'compare_story_point', 0x21: 'actor_tile_position',
         0x22: 'player_tile_position', 0x56: 'write_global', 0x5a: 'set_story_point',
         0x65: 'global_bit_on', 0x66: 'global_bit_off', 0x6e: 'compare_extended',
         0x7c: 'show_actor', 0x7d: 'hide_actor', 0x7e: 'remove_actor',
         0x81: 'load_object', 0x82: 'load_person', 0x83: 'load_enemy',
         0x8b: 'set_tile_position', 0x8d: 'set_precise_position', 0xb2: 'end',
         0xbb: 'dialogue', 0xc0: 'choice', 0xc1: 'dialogue_top', 0xc2: 'dialogue_bottom',
         0xc3: 'choice_top', 0xc4: 'choice_bottom', 0xc7: 'give_item_from_local',
         0xc8: 'open_menu', 0xc9: 'check_item', 0xca: 'give_item', 0xcb: 'remove_item',
         0xcc: 'check_gold', 0xcd: 'give_gold', 0xce: 'take_gold', 0xd8: 'battle',
         0xe4: 'copy_tiles', 0xe5: 'copy_tiles_wait', **{i: 'change_scene' for i in range(0xdc, 0xe2)}}


def field_listing(raw):
    """Decode every declared actor function plus its startup continuation."""
    count = raw[0] if raw else 0
    base = 1 + count * 32
    if not count or len(raw) < base:
        return 'No actor table; raw bytes: ' + raw.hex(' ') + '\n', dict(status='stub', actors=count, instructions=0)
    code = raw[base:]
    actors = [[v - count * 32 for v in struct.unpack_from('<16H', raw, 1 + i * 32)] for i in range(count)]
    ops = {}
    owners = {}
    failures = []
    rows = [f'Actors: {count}; code begins at file offset 0x{base:04X}.',
            'Addresses below are original file offsets. Raw hex operands are authoritative.', '']
    for actor, entries in enumerate(actors):
        rows.append(f'Actor {actor}: ' + ' '.join(f'fn{i}=0x{base + p:04X}' for i, p in enumerate(entries)))
        try:
            found = actor_ops(code, entries)
        except (IndexError, struct.error) as error:
            failures.append(dict(actor=actor, error=str(error)))
            continue
        for pc, value in found.items():
            ops[pc] = value
            owners.setdefault(pc, []).append(actor)
    rows.extend(['', 'offset  opcode operands                       operation / control flow'])
    covered = set()
    for pc, (opcode, operands) in sorted(ops.items()):
        if operands is None:
            failures.append(dict(offset=base + pc, opcode=opcode, error='Undecoded instruction'))
            rows.append(f'{base+pc:06X}  {opcode:02X}  UNDECODED; consult raw bytes')
            continue
        covered.update(range(pc, pc + len(operands) + 1))
        detail = NAMES.get(opcode, f'op_{opcode:02X}')
        if opcode in (0x10, 0x11):
            detail += f' -> 0x{base + pc + 1 + (operands[0] if opcode == 0x10 else -operands[0]):04X}'
        elif opcode in CONDITIONAL:
            detail += f' true=0x{base + pc + 1 + len(operands):04X}, false=0x{base + pc + len(operands) + operands[-1]:04X}'
        elif opcode in (2, 3, 4):
            target, fn = operands[0] // 2, operands[1] & 15
            detail += f' actor={target}, fn={fn}'
            if target < count:
                detail += f', entry=0x{base + actors[target][fn]:04X}'
        elif 0xdc <= opcode <= 0xe1:
            detail += f' scene={int.from_bytes(operands[:2], "little") & 1023}'
        rows.append(f'{base+pc:06X}  {opcode:02X} {operands.hex(" "):29} {detail}; actors={owners[pc]}')
    # Account for the entire payload without inventing instructions in bytes
    # not reached by a declared actor entry or its startup continuation.
    unvisited = sorted(set(range(len(code))) - covered)
    if unvisited:
        rows.extend(['', 'Bytes outside decoded entry flows (raw hex; not interpreted as instructions):'])
        start = previous = unvisited[0]
        ranges = []
        for offset in unvisited[1:]:
            if offset != previous + 1:
                ranges.append((start, previous + 1))
                start = offset
            previous = offset
        ranges.append((start, previous + 1))
        for start, end in ranges:
            for offset in range(start, end, 16):
                rows.append(f'{base+offset:06X}  {code[offset:min(offset+16,end)].hex(" ")}')
    return '\n'.join(rows) + '\n', dict(status='decoded' if not failures else 'partial', actors=count,
        instructions=len(ops), codeBytes=len(code), decodedBytes=len(covered), unvisitedBytes=len(unvisited), failures=failures)


def export(game, out):
    game = game.resolve()
    out = out.resolve()
    digest = hashlib.sha256((game / 'Chrono Trigger.exe').read_bytes()).hexdigest().upper()
    if digest != EXE_SHA256:
        raise ValueError('Unsupported executable hash')
    assets.RESOURCE = game / 'resources.bin'
    assets.directory.cache_clear()
    assets.read_asset.cache_clear()
    entries = assets.directory()
    raw_root = out / 'raw'
    raw_root.mkdir(parents=True, exist_ok=True)
    (out / 'field-scripts').mkdir(exist_ok=True)
    scripts = []
    total = 0
    with (out / 'resources.tsv').open('w', encoding='utf8', newline='') as stream:
        rows = csv.writer(stream, delimiter='\t')
        rows.writerow(['path', 'archive_offset', 'archive_bytes', 'decoded_bytes', 'sha256'])
        for i, (name, (offset, size)) in enumerate(sorted(entries.items()), 1):
            path = PurePosixPath(name)
            if path.is_absolute() or '..' in path.parts or '\\' in name or ':' in name:
                raise ValueError(f'Unsafe resource path {name!r}')
            destination = raw_root.joinpath(*path.parts)
            if not destination.resolve().is_relative_to(raw_root):
                raise ValueError(f'Resource path escapes output {name!r}')
            raw = assets.block(offset, size)
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(raw)
            sha = hashlib.sha256(raw).hexdigest().upper()
            rows.writerow([name, offset, size, len(raw), sha])
            total += len(raw)
            if name.startswith('Game/field/atel/'):
                listing, result = field_listing(raw)
                (out / 'field-scripts' / (path.stem + '.txt')).write_text(listing, encoding='utf8')
                scripts.append(dict(path=name, sha256=sha, **result))
            if i % 250 == 0:
                stream.flush()
                message = f'Extracted {i}/{len(entries)} resources; {total} decoded bytes'
                (out / 'progress.txt').write_text(message + '\n', encoding='utf8')
                print(message, flush=True)
    result = dict(executableSha256=digest, archiveSha256=hashlib.sha256(assets.RESOURCE.read_bytes()).hexdigest().upper(),
        resourceCount=len(entries), decodedBytes=total, fieldScripts=len(scripts),
        worldScripts=sum(name.startswith('Game/world/esl/') for name in entries),
        fieldScriptStatuses={status: sum(s['status'] == status for s in scripts) for status in ('decoded', 'partial', 'stub')},
        exportComplete=True, scripts=scripts)
    (out / 'manifest.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf8')
    summary = {k: v for k, v in result.items() if k != 'scripts'}
    (out / 'progress.txt').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf8')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-root', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    export(args.game_root, args.output)
