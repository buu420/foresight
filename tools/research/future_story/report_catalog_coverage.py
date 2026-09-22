"""Summarize installed map and pickup coverage without decoding reward contents."""
import argparse
import hashlib
import json
from pathlib import Path
import struct

import assets
from audit import EXE_SHA256
from decode_verified import packet, walk


def report(game, catalog_path):
    assert hashlib.sha256((game / 'Chrono Trigger.exe').read_bytes()).hexdigest().upper() == EXE_SHA256
    assets.RESOURCE = game / 'resources.bin'
    catalog = json.loads(catalog_path.read_text(encoding='utf8'))
    assert catalog['ExecutableSha256'] == EXE_SHA256
    raw_offsets = assets.read_asset('Game/common/TakaraOffsetTbl.dat')
    count, = struct.unpack_from('<I', raw_offsets)
    offsets = struct.unpack_from(f'<{count}H', raw_offsets, 4)
    raw_treasures = assets.read_asset('Game/common/TakaraDataTbl.dat')
    total, = struct.unpack_from('<I', raw_treasures)
    assert len(raw_treasures) == 4 + total * 6
    rows = []
    claimed = set()
    for scene in catalog['Scenes']:
        index = scene['Id']
        first = offsets[index]
        last = offsets[index + 1] if index + 1 < count else total
        assert 0 <= first <= last <= total
        claimed.update(range(first, last))
        rows.append(dict(scene=index, name=scene['Name'], treasureRecords=list(range(first, last)),
                         exits=len(scene['Exits']), actors=len(scene['Actors']),
                         scriptedItemActors=[a['Id'] for a in scene['Actors'] if a['GivesItem']],
                         spatialRegions=len(scene['Regions'])))
    assert claimed == set(range(total))
    # Independently inspect each catalogued actor's native talk/contact entry
    # points. This catches omitted pickup/money interactions without embedding
    # the reward IDs or assuming the catalog's GivesItem flag proves itself.
    direct_interactions = 0
    missing_interactions = []
    for scene in catalog['Scenes']:
        if scene['Id'] in catalog['MissingScripts']:
            continue
        code, entries, base = packet(scene['Id'])
        for actor in scene['Actors']:
            functions = entries[actor['Id']]
            item_ops = [base + pc for pc, (opcode, _) in walk(code, [functions[1], functions[2]]).items()
                        if opcode in (0xC7, 0xCA, 0xCD)]
            if not item_ops:
                continue
            direct_interactions += 1
            if not actor['GivesItem']:
                missing_interactions.append(dict(scene=scene['Id'], actor=actor['Id'], offsets=item_ops))
    return dict(executableSha256=EXE_SHA256, catalogSha256=hashlib.sha256(catalog_path.read_bytes()).hexdigest().upper(),
                sceneCount=len(rows), staticExits=sum(r['exits'] for r in rows),
                worldEntrances=len(catalog['Worlds']), treasureRecords=total,
                scriptedItemActors=sum(len(r['scriptedItemActors']) for r in rows),
                actorIdentities=sum(r['actors'] for r in rows), spatialRegions=sum(r['spatialRegions'] for r in rows),
                directItemOrMoneyInteractionActors=direct_interactions,
                uncataloguedDirectItemInteractions=missing_interactions,
                missingScripts=catalog['MissingScripts'], extractionWarnings=catalog['ExtractionWarnings'],
                scenes=rows,
                scope='Resource coverage and direct interaction opcode audit only. The direct audit checks catalogued actors, not automatic rewards or every delegated script branch. Runtime uses active treasure aliases, open flags, live positions and collision. No live playthrough is claimed.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-root', type=Path, required=True)
    parser.add_argument('--catalog', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = report(args.game_root, args.catalog)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf8')
    print(json.dumps({key: value for key, value in result.items()
                      if key not in ('scenes', 'extractionWarnings', 'scope')}, indent=2))
