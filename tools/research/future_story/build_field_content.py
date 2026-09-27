"""Generate Navigation/Data/field-content.json from the installed scene scripts (read-only).

  py -3 -X utf8 tools/research/future_story/build_field_content.py [--write]

Script-proven facts that the navigation catalog does not record:

StandIns  Actors the game uses to receive Confirm on behalf of a sprite, because the keeper
          behind a counter is outside the native Confirm range (FieldInteractionRange):
            Call    the whole Confirm handler (function 1) forwards to one sprite's Confirm
                    handler or service (opcode 02/03/04), optionally behind the usual
                    re-entry lock (12 test, 75/77 set/clear of a local);
            Twin    an interaction marker (no sprite) within three tiles of the sprite whose
                    Confirm handler is byte-identical to the sprite's;
            Forward an interaction marker or object within three tiles of a person or
                    creature whose Confirm handler forwards to that sprite's Confirm handler on
                    some paths but also acts on its own (Truce's "Market goods" counter).
                    Handlers that can start a battle (D8) are never stand-ins.
          Call and Twin stand-ins are the same destination as their sprite; Forward only
          extends where the sprite can be reached from.
Services  The Confirm handler (or a stand-in's forwarded function) opens a shop (C8 80-BF),
          or heals the party (F8/F9/FA) after taking gold (CE): an inn. Gold-free healing
          is not labelled; beds and machines cannot be told apart from the script.
Groups    Adjacent interaction markers whose Confirm handlers are byte-identical, or a marker
          that only forwards to an adjacent marker: one feature drawn in the map (a
          two-tile sign, a bar counter), offered once.
"""
import hashlib
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
from decode_verified import walk  # noqa: E402
from audit_content import describe  # noqa: E402
from script_navigation import guard  # noqa: E402

EXE_SHA256 = "8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7"
OUTPUT = REPO / "src" / "ChronoTriggerAccessibility.Mod" / "Navigation" / "Data" / "field-content.json"
CALLS = (0x02, 0x03, 0x04)
# Operations that neither act nor show anything: return, jumps, the re-entry lock.
NEUTRAL = {0x00, 0x10, 0x11, 0x12, 0x75, 0x76, 0x77}


def scene_ids():
    return sorted(int(p.split("mapinfo_")[1].split(".")[0]) for p in assets.directory() if "/Mapinfo/mapinfo_" in p)


def scene_packet(scene):
    try:
        header = assets.read_asset(f"Game/field/Mapinfo/mapinfo_{scene}.dat")
        script = struct.unpack_from("<H", header, 16)[0]
        raw = assets.read_asset(f"Game/field/atel/Atel_{script:04}.dat")
    except KeyError:
        return None
    n = raw[0]
    base = 1 + n * 32
    if n == 0 or len(raw) < base:
        return None
    entries = [[v - n * 32 for v in struct.unpack_from("<16H", raw, 1 + i * 32)] for i in range(n)]
    return raw[base:], entries


def loads(code, functions):
    return sorted({(op - 0x7E, int.from_bytes(a[:2] if op == 0x83 else a[:1], "little"))
                   for op, a in walk(code, [functions[0]]).values() if op in (0x81, 0x82, 0x83) and a is not None})


def tile(code, functions):
    """First initialisation position, as a tile (8B tile, or 8D fine coordinates >> 8)."""
    for pc, (op, a) in sorted(walk(code, [functions[0]]).items()):
        if op == 0x8B and a is not None:
            return a[0], a[1]
        if op == 0x8D and a is not None:
            return struct.unpack_from("<H", a, 0)[0] >> 8, struct.unpack_from("<H", a, 2)[0] >> 8
    return None


def reachable(code, actors, entry, visiting=frozenset()):
    """All ops reachable from a function, following calls into other actors."""
    if entry in visiting:
        return []
    ops = list(walk(code, [entry]).values())
    for op, a in list(ops):
        if op in CALLS and a is not None and a[0] // 2 < len(actors):
            ops.extend(reachable(code, actors, actors[a[0] // 2][a[1] & 15], visiting | {entry}))
    return ops


def service(ops):
    shop = any(op == 0xC8 and a is not None and 0x80 <= a[0] <= 0xBF for op, a in ops)
    inn = any(op in (0xF8, 0xF9, 0xFA) for op, a in ops) and any(op == 0xCE for op, a in ops)
    return "Shop" if shop else "Inn" if inn else None


def body(code, entry):
    """Bytes of a function whose reachable code is one contiguous block starting at its entry."""
    ops = walk(code, [entry])
    if not ops or min(ops) != entry or any(a is None for op, a in ops.values()):
        return None
    end = max(pc + 1 + len(a) for pc, (op, a) in ops.items())
    return bytes(code[entry:end])


def near(a, b, limit):
    return a is not None and b is not None and max(abs(a[0] - b[0]), abs(a[1] - b[1])) <= limit


def build():
    stand_ins, services, groups = [], [], []
    for scene in scene_ids():
        packet = scene_packet(scene)
        if packet is None:
            continue
        code, actors = packet
        identity = [loads(code, f) for f in actors]
        where = [tile(code, f) for f in actors]
        handlers = [f[1] if f[1] != f[0] and 0 <= f[1] < len(code) else None for f in actors]
        bodies = [body(code, h) if h is not None else None for h in handlers]
        scene_stand_ins = []
        pairs = []
        for index, entry in enumerate(handlers):
            if entry is None:
                continue
            ops = walk(code, [entry])
            if any(a is None for op, a in ops.values()):
                continue
            calls = {(a[0] // 2, a[1] & 15) for op, a in ops.values() if op in CALLS and a[0] // 2 < len(actors)}
            others = {op for op, a in ops.values() if op not in CALLS}
            pure = len(calls) == 1 and others <= NEUTRAL
            for target, function in sorted(calls):
                if target == index or handlers[target] is None:
                    continue
                forwards_confirm = actors[target][function] == handlers[target]
                if not identity[target]:
                    # Two markers of one feature: one only forwards to its neighbour.
                    if pure and not identity[index] and near(where[index], where[target], 1):
                        pairs.append({index, target})
                    continue
                if pure and (forwards_confirm or service(reachable(code, actors, actors[target][function]))):
                    scene_stand_ins.append(dict(Scene=scene, StandIn=index, Target=target, Kind="Call"))
                elif (not pure and forwards_confirm and 0xD8 not in others and near(where[index], where[target], 3)
                      and (not identity[index] or describe(*identity[index][0])[1] == "Objects")
                      and describe(*identity[target][0])[1] == "People"):
                    scene_stand_ins.append(dict(Scene=scene, StandIn=index, Target=target, Kind="Forward"))
        # Twins: a counter marker running a byte-identical copy of a nearby keeper's handler.
        for index in range(len(actors)):
            if identity[index] or bodies[index] is None or bodies[index] == b"\x00":
                continue
            for target in range(len(actors)):
                if identity[target] and bodies[target] == bodies[index] and near(where[index], where[target], 3) \
                        and not any(s["StandIn"] == index and s["Target"] == target for s in scene_stand_ins):
                    scene_stand_ins.append(dict(Scene=scene, StandIn=index, Target=target, Kind="Twin"))
        for binding in scene_stand_ins:
            binding['Guards'] = forward_requirements(scene, binding['StandIn'], binding['Target']) \
                if binding['Kind'] == 'Forward' else []
        stand_ins.extend(scene_stand_ins)
        # Services, including those reached only through a stand-in.
        found = {}
        for index, entry in enumerate(handlers):
            kind = service(reachable(code, actors, entry)) if entry is not None else None
            if kind:
                found[index] = kind
        for s in scene_stand_ins:
            kind = found.get(s["StandIn"])
            if kind and s["Kind"] in ("Call", "Twin"):
                found.setdefault(s["Target"], kind)
        services.extend(dict(Scene=scene, Actor=a, Kind=k) for a, k in sorted(found.items()))
        # Groups: adjacent markers with byte-identical handlers (same contact behaviour too),
        # joined with marker-to-marker forwarding pairs.
        links = defaultdict(set)
        markers = [i for i in range(len(actors)) if not identity[i] and bodies[i] not in (None, b"\x00") and where[i]]
        for i in markers:
            for j in markers:
                if i < j and bodies[i] == bodies[j] and near(where[i], where[j], 1) and \
                        (actors[i][2] == actors[i][1]) == (actors[j][2] == actors[j][1]):
                    links[i].add(j)
                    links[j].add(i)
        for pair in pairs:
            i, j = sorted(pair)
            links[i].add(j)
            links[j].add(i)
        seen = set()
        for start in sorted(links):
            if start in seen:
                continue
            component, pending = set(), [start]
            while pending:
                m = pending.pop()
                if m not in component:
                    component.add(m)
                    pending.extend(links[m] - component)
            seen |= component
            groups.append(dict(Scene=scene, Actors=sorted(component)))
    return dict(ExecutableSha256=EXE_SHA256,
                Source="tools/research/future_story/build_field_content.py",
                StandIns=stand_ins, Services=services, MarkerGroups=groups)


def forward_requirements(scene, stand_in, target):
    """Native branch conditions for each non-exclusive forwarding interaction.

    Relative Atel offsets: scene17 counter11 tests Point<27 at193, Local06 at19B,
    and G142&10 at1A5 before calling actor8 at1A0 or actor9 at1AA. Scene39 counter8
    tests G1A1&80 at3D and G1A2&10 at4C; only both-clear reaches the choice that
    can call actor9 at61. The player still makes that choice.
    """
    if (scene, stand_in, target) == (17, 11, 8):
        return [guard('Global', 0, 3, 0x27, False), guard('Local', 6, 0, 0)]
    if (scene, stand_in, target) == (17, 11, 9):
        return [guard('Global', 0, 3, 0x27, False), guard('Local', 6, 0, 0, False),
                guard('Global', 0x142, 6, 0x10)]
    if (scene, stand_in, target) == (39, 8, 9):
        return [guard('Global', 0x1a1, 6, 0x80, False), guard('Global', 0x1a2, 6, 0x10, False)]
    raise ValueError(f'Forward binding has no audited conditions: {scene}/{stand_in}->{target}')


def main():
    exe = hashlib.sha256((GAME / "Chrono Trigger.exe").read_bytes()).hexdigest().upper()
    assert exe == EXE_SHA256, exe
    data = build()
    print(f"stand-ins {Counter(s['Kind'] for s in data['StandIns'])}; services "
          f"{Counter(s['Kind'] for s in data['Services'])}; marker groups {len(data['MarkerGroups'])} "
          f"({sum(len(g['Actors']) - 1 for g in data['MarkerGroups'])} extra rows)")
    if "--write" in sys.argv:
        OUTPUT.write_text(json.dumps(data, indent=1) + "\n", encoding="utf-8")
        print("wrote", OUTPUT)
    return data


if __name__ == "__main__":
    main()
