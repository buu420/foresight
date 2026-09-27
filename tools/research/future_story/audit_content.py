"""Whole-game item/object/NPC coverage audit, from installed scripts to navigation rules.

Run from the repository root (read-only; nothing is written to the game):
  py -3 -X utf8 tools/research/future_story/audit_content.py [catalog.json]

For every scene script and every actor it records what the native script does:
  * identity exactly as tools/research/future_story/build_catalog.py derives it
    (loads from init opcodes 81/82/83, marker = no load, positioned, interactive);
  * the Confirm handler (function 1) and touch handler (function 2) effects, following
    actor-to-actor requests (opcodes 02/03/04) the way script_navigation.effectful does;
  * whether initialisation hides the sprite (opcode 91), and whether any script can
    show it again (its own 90 in a reachable function, or another actor's 7C);
  * loop-driven Confirm interactions: an actor loop that tests the Confirm/any key
    (2D/31/3C) and then talks, gives an item or opens a menu;
  * shop (C8 80-BF) and inn (F8/F9/FA heal) handlers, and the text indexes of the
    Confirm handler, for duplicate-marker detection.

It then applies the gating rules of FieldNavigationSource (HEAD) to that identity and the
chosen catalog (default: git HEAD game-navigation.json) and classifies each interactive
actor. The runtime proof is the C# FieldContentCoverageTests, which runs the real
FieldNavigationSource over the fixture this script writes with --fixture.
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
GAME = REPO.parent
sys.path.insert(0, str(REPO / "tools" / "research" / "future_story"))
import assets  # noqa: E402

assets.RESOURCE = GAME / "resources.bin"
from assets import read_asset  # noqa: E402
from connections import names  # noqa: E402
from decode_verified import walk  # noqa: E402

# FieldVisualLabels.Objects (HEAD) and Describe(), reproduced exactly.
OBJECT_VISUALS = {
    93, 94, 95, 97, 98, 99, 100, 101, 102, 104, 105, 106, 107, 108, 110, 111, 112, 113, 114, 115,
    117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 134, 135, 136,
    137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151, 153, 154, 155, 156,
    157, 158, 159, 160, 161, 162, 163, 166, 167, 168, 169, 172, 173, 174, 175, 176, 177, 178, 179,
    180, 181, 185, 188, 190, 192, 193, 194, 200, 201, 202, 203, 206, 207, 208, 212, 219, 221,
    231, 232, 233, 234, 235, 236, 237, 238,
}
VISUAL_LABELS = {
    **dict.fromkeys((99, 166), "Door"), 100: "Gravestone", **dict.fromkeys((101, 157, 161), "Opening"),
    **dict.fromkeys((102, 129, 130, 175, 194), "Statue"), 115: "Barrel",
    **dict.fromkeys((145, 160, 168, 193), "Rock"), 146: "Sword", 169: "Paper",
    **dict.fromkeys((174, 176), "Device"), **dict.fromkeys((159, 203, 231, 232, 233, 234), "Balloon"),
    **dict.fromkeys((94, 95, 143), "Swirling light"),
    **dict.fromkeys((119, 120, 134, 140, 178, 201, 202, 235, 236, 237, 238), "Sparkle"),
}


def describe(cls, visual):
    kind = cls & 0x7F
    if kind in (5, 6):
        return "Creature", "People"
    index = visual + (7 if kind == 4 else 0)
    if kind < 0 or kind > 4 or not 0 <= index <= 262:
        return "Interactable", "Objects"
    if index in (66, 182, 183, 184, 197):
        return "Cat", "People"
    if index in (63, 75, 84, 103, 133, 165, 171, 191, 217):
        return "Creature", "People"
    if index not in OBJECT_VISUALS:
        return "Person", "People"
    return VISUAL_LABELS.get(index, "Object"), "Objects"


TEXT = (0xBB, 0xC0, 0xC1, 0xC2, 0xC3, 0xC4)
EFFECTS = {
    **dict.fromkeys(TEXT, "Talk"),
    **dict.fromkeys((0xC7, 0xCA, 0xCD), "Item"),
    0xC8: "Menu", 0xD8: "Battle",
    **dict.fromkeys(range(0xDC, 0xE2), "Warp"),
    **dict.fromkeys((0xE4, 0xE5), "Terrain"),
    **dict.fromkeys((0x3A, 0x45, 0x46, 0x65, 0x66), "Switch"),
    0x5A: "Progress",
    **dict.fromkeys((0xD0, 0xD1, 0xD3, 0xD4, 0xD6), "Party"),
    **dict.fromkeys((0xF8, 0xF9, 0xFA), "Heal"),
}
CONFIRM_TESTS = {0x2D, 0x31, 0x3C}
CONDITIONS = {0x12, 0x13, 0x14, 0x15, 0x16, 0x18, 0x1A, 0x27, 0x28, 0x2D, 0x30, 0x31, 0x6E, 0xC9, 0xCC, 0xCF, 0xD2}


def scene_packet(scene):
    try:
        header = read_asset(f"Game/field/Mapinfo/mapinfo_{scene}.dat")
    except KeyError:
        return None
    script = struct.unpack_from("<H", header, 16)[0]
    try:
        raw = read_asset(f"Game/field/atel/Atel_{script:04}.dat")
    except KeyError:
        return None
    n = raw[0]
    base = 1 + n * 32
    if n == 0 or len(raw) < base:
        return None
    entries = [[v - n * 32 for v in struct.unpack_from("<16H", raw, 1 + i * 32)] for i in range(n)]
    return script, raw[base:], entries, base


def menu_kind(ui):
    return "Shop" if 0x80 <= ui <= 0xBF else "Save" if ui in (0x02, 0x40) else \
        "Load" if ui in (0x01, 0x41) else "Rename" if 0xC0 <= ui <= 0xC7 else "PartyMenu" if ui == 0 else "Menu"


def effects_of(code, actors, entry, cache, visiting=frozenset()):
    """(effect kinds, text indexes) reachable from one function, following requests 02/03/04."""
    if entry in cache:
        return cache[entry]
    if entry in visiting:
        return set(), set()
    found, texts = set(), set()
    for op, a in walk(code, [entry]).values():
        if a is None:
            continue
        kind = EFFECTS.get(op)
        if kind == "Menu":
            kind = menu_kind(a[0])
        if op == 0x56 and a[1:] == b"\0\0":
            kind = "Progress"
        if kind:
            found.add(kind)
        if op in TEXT:
            texts.add(a[0])
        if op in (2, 3, 4) and a[0] // 2 < len(actors):
            more, more_texts = effects_of(code, actors, actors[a[0] // 2][a[1] & 15], cache, visiting | {entry})
            found |= more
            texts |= more_texts
    cache[entry] = (found, texts)
    return found, texts


def init_and_loop(code, functions):
    init = walk(code, [functions[0]])
    returns = [pc for pc, (op, a) in init.items() if op == 0]
    loop = walk(code, [min(returns) + 1]) if returns else {}
    return init, loop


def committed_catalog(path=None):
    source = Path(path) if path else REPO / 'src/ChronoTriggerAccessibility.Mod/Navigation/Data/game-navigation.json'
    return json.loads(source.read_text(encoding="utf-8-sig"))


def main():
    catalog_path = next((a for a in sys.argv[1:] if not a.startswith("--")), None)
    catalog = committed_catalog(catalog_path)
    scenes = {s["Id"]: s for s in catalog["Scenes"]}
    scene_names = names()
    rows = []
    loop_confirms = []
    for scene_id in sorted(scenes):
        packet = scene_packet(scene_id)
        if packet is None:
            continue
        script, code, actors, base = packet
        cache = {}
        catalog_actors = {a["Id"]: a for a in scenes[scene_id]["Actors"]}
        # Who can show whom: another actor's 7C, or a request into a function that runs 90.
        shows = defaultdict(set)
        requests = defaultdict(set)
        for index, functions in enumerate(actors):
            init, loop = init_and_loop(code, functions)
            every = dict(init)
            every.update(loop)
            for f in range(1, 16):
                every.update(walk(code, [functions[f]]))
            for op, a in every.values():
                if a is None:
                    continue
                if op == 0x7C and a[0] // 2 < len(actors):
                    shows[a[0] // 2].add(("7C", index))
                if op in (2, 3, 4) and a[0] // 2 < len(actors):
                    requests[(a[0] // 2, a[1] & 15)].add(index)
        for index, functions in enumerate(actors):
            init, loop = init_and_loop(code, functions)
            loads = sorted({(op - 0x7E, int.from_bytes(a[:2] if op == 0x83 else a[:1], "little"))
                            for op, a in init.values() if op in (0x81, 0x82, 0x83) and a is not None})
            has_load = any(op in (0x57, 0x5C, 0x62, 0x68, 0x6A, 0x6C, 0x6D, 0x80, 0x81, 0x82, 0x83) for op, a in init.values())
            tiles = [(a[0], a[1]) for pc, (op, a) in sorted(init.items()) if op == 0x8B and a is not None]
            fine = [(struct.unpack_from("<H", a, 0)[0], struct.unpack_from("<H", a, 2)[0])
                    for pc, (op, a) in sorted(init.items()) if op == 0x8D and a is not None]
            positioned = bool(tiles or fine)
            confirm, confirm_texts = effects_of(code, actors, functions[1], cache)
            touch, _ = effects_of(code, actors, functions[2], cache)
            party_script = not loads and (has_load or not positioned)
            # A loop that polls Confirm and then acts is an interaction too (save checkers,
            # some scenery). Only effects in the loop itself or its requests count.
            if loop and any(op in CONFIRM_TESTS for op, a in loop.values()):
                effects = set()
                for op, a in loop.values():
                    if a is None:
                        continue
                    kind = EFFECTS.get(op)
                    if kind == "Menu":
                        kind = menu_kind(a[0])
                    if kind:
                        effects.add(kind)
                    if op in (2, 3, 4) and a[0] // 2 < len(actors):
                        effects |= effects_of(code, actors, actors[a[0] // 2][a[1] & 15], cache)[0]
                if effects:
                    loop_confirms.append(dict(scene=scene_id, actor=index, loads=loads,
                                              tile=tiles[0] if tiles else None, effects=sorted(effects),
                                              party=party_script,
                                              coordinateTests=sorted({hex(op) for op, a in loop.values()
                                                                      if op in (0x21, 0x22, 0x23, 0x24, 0x25, 0x26)})))
            if not (confirm or touch):
                continue
            if party_script:
                continue
            hides = [pc for pc, (op, a) in init.items() if op == 0x91]
            conditional_init = any(op in CONDITIONS for op, a in init.values())
            shown_by = sorted(shows[index])
            for f in range(1, 16):
                if any(op == 0x90 for op, a in walk(code, [functions[f]]).values()):
                    if f in (1, 2) or requests.get((index, f)):
                        shown_by.append((f"fn{f}", sorted(requests.get((index, f), []))))
            if any(op == 0x90 for op, a in loop.values()):
                shown_by.append(("loop", []))
            entry = catalog_actors.get(index)
            marker = not loads
            if marker:
                identity = dict(kind="marker", label=None, category="Objects")
            else:
                cls, visual = loads[0]
                label, category = describe(cls, visual)
                identity = dict(kind=f"class{cls}", visual=visual, label=label, category=category)
            actions = entry["Actions"] if entry else []
            kinds = sorted({a["Kind"] for a in actions})
            if marker:
                gate = "listed" if entry and entry["Marker"] and actions else "filtered:marker-not-catalogued"
            elif identity["category"] == "People":
                gate = "listed"
            else:
                gate = "listed" if actions else "filtered:object-without-catalog-action"
            visibility = "marker" if marker else \
                "hidden-by-init" if hides and not conditional_init else \
                "conditionally-hidden" if hides else "shown"
            rows.append(dict(scene=scene_id, name=scenes[scene_id]["Name"] or scene_names.get(scene_id),
                             script=script, actor=index, loads=loads,
                             tile=tiles[0] if tiles else None, fine=fine[0] if fine else None,
                             confirm=sorted(confirm), touch=sorted(touch), confirmEntry=functions[1],
                             texts=sorted(confirm_texts),
                             catalogActions=kinds, catalogMarker=entry["Marker"] if entry else None,
                             catalogTouch=entry["Touch"] if entry else None,
                             identity=identity, gate=gate, visibility=visibility,
                             shownBy=[list(s) for s in shown_by]))

    out = REPORTS / "content-audit.json"
    out.write_text(json.dumps(dict(actors=rows, loopConfirms=loop_confirms), indent=1), encoding="utf-8")
    print(f"interactive actors: {len(rows)} in {len({r['scene'] for r in rows})} scenes")
    print("gate:", Counter(r["gate"] for r in rows).most_common())
    print("visibility:", Counter(r["visibility"] for r in rows).most_common())
    never = [r for r in rows if r["visibility"] == "hidden-by-init" and not r["shownBy"]]
    print(f"hidden by init and never shown by any script: {len(never)}; effects",
          Counter(tuple(sorted(set(r['confirm']) | set(r['touch']))) for r in never).most_common(8))
    print("filtered (any visibility) by effect set:")
    kinds = Counter((r["gate"], tuple(sorted(set(r["confirm"]) | set(r["touch"]))))
                    for r in rows if r["gate"] != "listed")
    for (gate, effect), value in kinds.most_common(40):
        print(f"  {value:4} {gate} {effect}")
    print("listed labels:", Counter(r["identity"]["label"] for r in rows if r["gate"] == "listed").most_common(20))
    shops = [r for r in rows if "Shop" in r["confirm"]]
    inns = [r for r in rows if "Heal" in r["confirm"]]
    print(f"shop actors: {len(shops)}; inn/heal actors: {len(inns)}")
    print(f"loop-driven Confirm interactions: {len(loop_confirms)};",
          Counter(tuple(l['effects']) for l in loop_confirms).most_common(12))


if __name__ == "__main__":
    main()
