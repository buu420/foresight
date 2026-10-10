"""Write the test fixtures for FieldContentCoverageTests from the installed resources (read-only).

  py -3 -X utf8 tools/research/future_story/export_content_fixture.py [tests/Navigation dir]

field-content-coverage-0335.json  every script-interactive actor (audit_content.py rows) with its
    installed initialisation position, plus the positions of every stand-in pair and marker
    group in Navigation/Data/field-content.json. Actors the field layer is not meant to offer
    are listed separately with the reason, so the test can hold both lists to account.
field-content-maps-0335.json      installed terrain for the scenes with real-map route tests
    (MapTable planes via export_route_fixtures.terrain; initial terrain, not live changes).
"""
import base64
import json
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
REPORTS = REPO / "artifacts" / "research" / "content-coverage-0335" / "claude"
REPORTS.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(REPO / "tools" / "research" / "future_story"))
import audit_content  # noqa: E402  (sets assets.RESOURCE)
import build_field_content  # noqa: E402
from connections import scene_info  # noqa: E402
from export_route_fixtures import terrain  # noqa: E402

TESTS = Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / "tests" / "ChronoTriggerAccessibility.Mod.Tests" / "Navigation"
ROUTE_SCENES = (12, 18, 19, 54, 119, 188)
# Script-special pickups whose availability EarlyStoryTargets decides from more than the
# catalog; they have their own tests (EarlyStoryTests, PendantStoryTests).
AUDITED_GATES = {(8, 11), (439, 15)}
# Contact triggers the story providers name only while their objective is current
# (EarlyStoryTargets.IsTouchLandmark, FutureAreaLabels.IsTouchLandmark).
STORY_TRIGGERS = {(8, 12), (28, 1), (120, 17), (122, 8), (122, 25), (122, 26), (464, 24),
                  (465, 16), (465, 17), (465, 18), (465, 19), (465, 20)}


def position(code, functions):
    for pc, (op, a) in sorted(audit_content.walk(code, [functions[0]]).items()):
        if op == 0x8B and a is not None:
            return a[0] * 256 + 128, a[1] * 256 + 255
        if op == 0x8D and a is not None:
            return struct.unpack_from("<H", a, 0)[0], struct.unpack_from("<H", a, 2)[0]
    return None


def main():
    audit = json.loads((REPORTS / "content-audit.json").read_text(encoding="utf-8"))["actors"]
    content = build_field_content.build()
    positions = {}
    for scene in sorted({r["scene"] for r in audit} | {s["Scene"] for s in content["StandIns"]}
                        | {g["Scene"] for g in content["MarkerGroups"]}):
        packet = audit_content.scene_packet(scene)
        if packet is None:
            continue
        _, code, actors, _ = packet
        for index, functions in enumerate(actors):
            identity = build_field_content.loads(code, functions)
            positions[(scene, index)] = (position(code, functions), identity[0] if identity else (7, 0))
    listed, excluded = [], []
    for r in audit:
        key = (r["scene"], r["actor"])
        where, (cls, visual) = positions[key]
        effects = set(r["confirm"]) | set(r["touch"])
        reason = "encounter-only" if effects <= {"Battle"} else "party-only" if effects <= {"Party"} else \
            "parked" if where is not None and (where[0] >= 0x8000 or where[1] >= 0x8000 or
                # Reptite Lair holes wait at solid tile (0,0) until a burrow or event places them.
                r["scene"] in (222, 284, 285, 286) and (cls, visual) == (4, 133) and
                where[0] >> 8 == 0 and where[1] >> 8 == 0) else \
            "audited-gate" if key in AUDITED_GATES else "story-trigger" if key in STORY_TRIGGERS else None
        if reason:
            excluded.append(dict(Scene=r["scene"], Actor=r["actor"], Reason=reason))
            continue
        # Listing does not depend on where an actor stands; one placed only by a later
        # movement opcode is tested at an ordinary floor position instead.
        x, y = where if where is not None else (8 * 256 + 128, 8 * 256 + 255)
        listed.append(dict(Scene=r["scene"], Actor=r["actor"], Class=cls, Visual=visual, X=x, Y=y))

    def place(scene, actor):
        where, (cls, visual) = positions[(scene, actor)]
        return None if where is None else dict(Actor=actor, Class=cls, Visual=visual, X=where[0], Y=where[1])

    stand_ins = []
    for s in content["StandIns"]:
        owner, stand_in = place(s["Scene"], s["Target"]), place(s["Scene"], s["StandIn"])
        if owner and stand_in:
            stand_ins.append(dict(Scene=s["Scene"], Kind=s["Kind"], Owner=owner, StandIn=stand_in))
    groups = []
    for g in content["MarkerGroups"]:
        members = [place(g["Scene"], a) for a in g["Actors"]]
        if all(members):
            groups.append(dict(Scene=g["Scene"], Members=members))
    (TESTS / "field-content-coverage-0335.json").write_text(json.dumps(dict(
        Source="tools/research/future_story/export_content_fixture.py",
        Actors=listed, Excluded=excluded, StandIns=stand_ins, Groups=groups), separators=(",", ":")) + "\n",
        encoding="utf-8")
    maps = {}
    for scene in ROUTE_SCENES:
        width, height, cells = terrain(scene_info(scene)["map"])
        exits = [128] * (width * height)
        for e in scene_info(scene)["exits"]:
            for n in range(e["length"]):
                x = e["source"][0] + (0 if e["vertical"] else n)
                y = e["source"][1] + (n if e["vertical"] else 0)
                if 0 <= x < width and 0 <= y < height:
                    exits[y * width + x] = e["id"]
        encode = lambda values: base64.b64encode(bytes(values)).decode("ascii")  # noqa: E731
        maps[str(scene)] = dict(Width=width, Height=height, CollisionShapes=encode(c[0] for c in cells),
                                TerrainFlags=encode(c[1] for c in cells), CollisionLayers=encode(c[2] for c in cells),
                                PlayerLayer=1, TransitionPending=False, ExitWidth=width, ExitHeight=height,
                                ExitCells=encode(exits))
    (TESTS / "field-content-maps-0335.json").write_text(json.dumps(maps, separators=(",", ":")) + "\n", encoding="utf-8")
    # Treasure: every scene's resolved TakaraDataTbl range (audit_treasures.py follows the
    # (0, 0) alias records exactly as 0x179690 does), with the installed map size, each
    # record's collision shape and layer-1 tile, for FieldEnvironmentCapture.TryTreasures.
    treasure_rows = json.loads((REPORTS / "treasure-audit.json").read_text(encoding="utf-8"))
    treasures = []
    for scene in sorted({r["scene"] for r in treasure_rows}):
        rows = sorted((r for r in treasure_rows if r["scene"] == scene), key=lambda r: r["record"])
        width, height, _ = terrain(scene_info(scene)["map"])
        treasures.append(dict(Scene=scene, Width=width, Height=height, First=rows[0]["record"], Last=rows[-1]["record"] + 1,
                              Records=[dict(Index=r["record"], X=r["x"], Y=r["y"], Shape=r["shape"] or 0, Tile=r["tile1"] or 0)
                                       for r in rows]))
    (TESTS / "field-treasures-0335.json").write_text(json.dumps(treasures, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"listed {len(listed)}, excluded {len(excluded)}, stand-ins {len(stand_ins)}, groups {len(groups)}, "
          f"maps {sorted(maps)}, treasure scenes {len(treasures)} records {sum(len(t['Records']) for t in treasures)}")


if __name__ == "__main__":
    main()
