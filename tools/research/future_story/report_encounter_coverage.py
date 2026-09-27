"""Compare catalogued battle destinations with independent native entrypoint walks.

This checks structural extraction coverage, not live playthrough coverage. Every
D8 site is retained in the report, including automatic/cinematic sites and sites
whose first reachable activation has not been classified.
"""
import argparse
import hashlib
import json
from pathlib import Path

import assets
from audit import EXE_SHA256
from decode_verified import actor_ops, instruction, packet, walk, CONDITIONAL


def first_battles(code, actors, start):
    pending, seen, sites = [(start, ())], set(), set()
    while pending:
        pc, stack = pending.pop()
        while 0 <= pc < len(code) and (pc, stack) not in seen:
            seen.add((pc, stack))
            op, a = instruction(code, pc)
            if a is None:
                break
            following = pc + 1 + len(a)
            if op == 0xd8:
                sites.add(pc)
                break
            if 0xdc <= op <= 0xe1:
                break
            if op in (0, 0xb2):
                if not stack:
                    break
                pc, stack = stack[-1][0], stack[:-1]
                continue
            if op in (0x10, 0x11):
                pc += 1 + (a[0] if op == 0x10 else -a[0])
                continue
            if op in CONDITIONAL:
                pending.append((pc + len(a) + a[-1], stack))
            if op in (2, 3, 4) and a[0] // 2 < len(actors) and len(stack) < 4:
                target = actors[a[0] // 2][a[1] & 15]
                if target != start and all(frame[1] != target for frame in stack):
                    stack += ((following, target),)
                    pc = target
                    continue
            pc = following
    return sites


def report(game, catalog_path):
    assert hashlib.sha256((game/'Chrono Trigger.exe').read_bytes()).hexdigest().upper() == EXE_SHA256
    assets.RESOURCE = game/'resources.bin'
    catalog = json.loads(catalog_path.read_text())
    scenes = []
    missing = []
    for scene in catalog['Scenes']:
        if scene['Id'] in catalog['MissingScripts']:
            continue
        code, actors, base = packet(scene['Id'])
        all_sites = set()
        direct, startup = [], []
        catalog_actors = {a['Id']: a for a in scene['Actors']}
        for actor, entries in enumerate(actors):
            ops = actor_ops(code, entries)
            all_sites.update(pc for pc, (op, _) in ops.items() if op == 0xd8)
            init = walk(code, [entries[0]])
            physical = any(op in (0x81, 0x82, 0x83, 0x8b, 0x8d) for op, _ in init.values())
            party = any(op in (0x57,0x5c,0x62,0x68,0x6a,0x6c,0x6d,0x80) for op, _ in init.values())
            for function in (1, 2):
                sites = first_battles(code, actors, entries[function])
                if not sites:
                    continue
                found = any(a['Kind'] == 'Encounter' and a['Touch'] == (function == 2)
                            for a in catalog_actors.get(actor, {}).get('Actions', []))
                row = dict(Actor=actor, Function=function, Sites=[base+p for p in sorted(sites)],
                    PhysicalInitialization=physical, PartyInitialization=party, Catalogued=found)
                direct.append(row)
                if physical and not party and not found:
                    missing.append(dict(Scene=scene['Id'], **row))
            ends = [pc for pc, (op, _) in init.items() if op == 0]
            if ends:
                sites = first_battles(code, actors, min(ends)+1)
                if sites:
                    startup.append(dict(Actor=actor, Sites=[base+p for p in sorted(sites)]))
        if not all_sites:
            continue
        signal = [a['Id'] for a in scene['Actors'] if any(x.get('Controller', -1) >= 0 for x in a['Actions'])]
        direct_sites = {p for x in direct for p in x['Sites']}
        startup_sites = {p for x in startup for p in x['Sites']}
        scenes.append(dict(Scene=scene['Id'], Name=scene['Name'], NativeBattleSites=[base+p for p in sorted(all_sites)],
            DirectEntries=direct, StartupEntries=startup, SignalActors=signal,
            OtherFunctionOrPostBattleSites=sorted({base+p for p in all_sites}-direct_sites-startup_sites)))
    return dict(Scope=__doc__, CatalogSha256=hashlib.sha256(catalog_path.read_bytes()).hexdigest().upper(),
        ScenesWithBattleCode=len(scenes), NativeBattleSites=sum(len(s['NativeBattleSites']) for s in scenes),
        PhysicalDirectEntries=sum(r['PhysicalInitialization'] and not r['PartyInitialization'] for s in scenes for r in s['DirectEntries']),
        UncataloguedPhysicalDirectEntries=missing,
        OtherFunctionOrPostBattleSites=sum(len(s['OtherFunctionOrPostBattleSites']) for s in scenes), Scenes=scenes)


if __name__ == '__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--game-root', type=Path, required=True)
    p.add_argument('--catalog', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    a=p.parse_args()
    result=report(a.game_root, a.catalog)
    a.output.write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps({k:v for k,v in result.items() if k not in ('Scenes','Scope')}, indent=2))
    raise SystemExit(bool(result['UncataloguedPhysicalDirectEntries']))
