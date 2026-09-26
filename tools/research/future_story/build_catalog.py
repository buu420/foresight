"""Generate short navigation facts from the user's installed PC resources.

No dialogue, reward contents, textures, or guide prose are emitted. The resulting
metadata identifies destinations; runtime capture still supplies active positions.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct

import assets
from connections import names, scene_info
from decode_verified import actor_ops, packet, walk
from audit import EXE_SHA256
from script_navigation import paths, region_actions, simplify, coordinate_locals
from bonus_regions import for_scene as audited_bonus_regions, actor_actions as audited_bonus_actions
from palace_visits import refine as refine_palace_visits


def build(game):
    assert hashlib.sha256((game / 'Chrono Trigger.exe').read_bytes()).hexdigest().upper() == EXE_SHA256
    assets.RESOURCE = game / 'resources.bin'
    map_names = [line.partition(',')[2] for line in assets.read_asset('Localize/en/msg/map.txt').decode('utf-8-sig').splitlines()]
    name_table = assets.read_asset('Game/common/MapMsgDataTable.dat')
    scene_ids = sorted(int(re.search(r'mapinfo_(\d+)\.dat$', p)[1])
                       for p in assets.directory() if '/Mapinfo/mapinfo_' in p)
    output = []
    skipped = []
    incomplete = []
    for scene in scene_ids:
        info = scene_info(scene)
        # Preserve map connections even for unused scenes with missing scripts.
        name_index = struct.unpack_from('<H', name_table, 6 + scene * 4)[0]
        displayed_name = map_names[name_index-1] if 0 < name_index <= len(map_names) else None
        entry = dict(Id=scene, Name=clean_name(info['name']) or clean_name(displayed_name),
                     Exits=[dict(Id=e['id'], Destination=e['destination'], X=e['arrival'][0], Y=e['arrival'][1])
                            for e in info['exits']], Actors=[], Regions=[])
        try:
            code, actors, base = packet(scene)
        except (KeyError, struct.error, IndexError):
            skipped.append(scene)
            output.append(entry)
            continue
        coordinates = coordinate_locals(code, actors)
        for actor, functions in enumerate(actors):
            all_ops = actor_ops(code, functions)
            errors = [base+pc for pc,(op,a) in all_ops.items() if a is None]
            if errors:
                raise ValueError(f'Unverified instruction boundary in scene {scene} actor {actor}: {errors}')
            init = walk(code, [functions[0]])
            talk = walk(code, [functions[1]])
            touch = walk(code, [functions[2]])
            loads = sorted({(op-0x7e, int.from_bytes(a[:2] if op==0x83 else a[:1], 'little'))
                            for op,a in init.values() if op in (0x81,0x82,0x83)})
            # Party loads are never generic invisible scenery markers.
            has_load = any(op in (0x57,0x5c,0x62,0x68,0x6a,0x6c,0x6d,0x80,0x81,0x82,0x83) for op,a in init.values())
            positioned = any(op in (0x8b,0x8d) for op,a in init.values())
            if not loads and (has_load or not positioned):
                # Party/control scripts are expanded when called or when their
                # startup contains a spatial trigger, not as selectable actors.
                continue
            meaningful = {0x3a,0x45,0x46,0x65,0x66,0xbb,0xc0,0xc1,0xc2,0xc3,0xc4,0xc7,0xc8,0xca,0xcd,0xe4,0xe5,*range(0xdc,0xe2)}
            talk_action = any(op in meaningful for op,a in talk.values())
            touch_action = any(op in meaningful for op,a in touch.values())
            actions = []
            for function in (1, 2):
                found, complete = paths(code, actors, functions[function], initial_coordinates=coordinates)
                if not complete:
                    incomplete.append(dict(Scene=scene, Actor=actor, Function=function, Start=functions[function]))
                    continue
                for action in found:
                    action['Touch'] = function == 2
                    if action not in actions:
                        actions.append(action)
            for action in audited_bonus_actions(scene, actor):
                if action not in actions:
                    actions.append(action)
            # Some scenery handlers delegate the interaction to another script
            # function. Follow the audited calls before classifying the marker.
            talk_action |= any(not a['Touch'] for a in actions)
            touch_action |= any(a['Touch'] for a in actions)
            marker = not has_load and positioned and (talk_action or touch_action)
            if not loads and not marker:
                continue
            transitions = sorted({a['Destination'] for a in actions if a['Kind'] == 'Warp'})
            gives = any(a['Kind'] == 'Item' for a in actions)
            label = 'Item pickup' if marker and gives else 'Passage' if marker and transitions else 'Interactable scenery' if marker else None
            entry['Actors'].append(dict(Id=actor, Loads=[dict(Class=c, Visual=v) for c,v in loads],
                                        Marker=marker, Touch=marker and not talk_action and touch_action,
                                        Label=label, GivesItem=gives, Destinations=transitions, Actions=actions))
        region_incomplete = []
        entry['Regions'] = simplify(region_actions(code, actors, region_incomplete) + audited_bonus_regions(scene))
        refine_palace_visits(entry)
        incomplete.extend(dict(Scene=scene, **issue) for issue in region_incomplete)
        output.append(entry)
    worlds=[]
    for world in range(8):
        raw=assets.read_asset(f'Game/world/EventTable/EventTable_{world:04}.dat')
        count=raw[0]
        for index in range(count):
            x,y,name,destination,facing,dx,dy=struct.unpack_from('<BBBHBBB',raw,1+index*8)
            worlds.append(dict(WorldId=world, Id=index, Destination=destination, X=x & 127, Y=y & 63,
                               Enabled=(x & 128)!=0, NameIndex=name))
    return dict(ExecutableSha256=EXE_SHA256, Scenes=output, Worlds=worlds,
                ExtractionWarnings=incomplete, MissingScripts=skipped), skipped


def clean_name(value):
    if not value or value.startswith(('(Unused)', '(Event)', 'Map Not Specified', '???')):
        return None
    return value.replace('\\', ', ').replace('->', 'to').replace('Ventillation', 'Ventilation')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-root', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args=parser.parse_args()
    catalog, skipped=build(args.game_root)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix('.tmp')
    temporary.write_text(json.dumps(catalog, separators=(',', ':'), ensure_ascii=True)+'\n', encoding='utf8')
    temporary.replace(args.output)
    print(f'Catalog: {len(catalog["Scenes"])} scenes, {sum(len(s["Exits"]) for s in catalog["Scenes"])} exits, '
          f'{sum(len(s["Actors"]) for s in catalog["Scenes"])} actor identities. Unused/missing scripts: {skipped}')
    print(f'Incomplete path expansions: {len(catalog["ExtractionWarnings"])}')


if __name__=='__main__':
    main()
