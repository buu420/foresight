"""Audited spatial paths whose unrelated loop branches exceed the general budget.

Installed PC Atel script offsets and all guards are recorded in
artifacts/research/full-story-0323/bonus/spatial-overrides.md. Bounds are leader
coordinate comparisons after opcode 22, never actor placements. The actor field
is the native controller running the path, not an enemy's guessed location.
"""
from script_navigation import guard


def extra(index, value, op=6, expected=True):
    return guard('Extra', index, op, value, expected)


def local(index, value=0):
    return guard('Local', index, 0, value)


def encounter(bounds, value, guards):
    left, top, right, bottom = bounds
    return dict(Actor=0, Kind='Encounter', Destination=-1, Value=value,
                Left=left, Top=top, Right=right, Bottom=bottom, Guards=guards)


def for_scene(scene):
    if scene == 587:
        return [
            encounter((22, 23, 29, 31), 0x0082,
                      [extra(0x17, 4, expected=False), local(0x0D), extra(0x12, 8, expected=False)]),
            encounter((22, 23, 29, 31), 0x0002,
                      [extra(0x17, 4, expected=False), local(0x0D), extra(0x12, 8)]),
            encounter((8, 52, 15, 61), 0x0002,
                      [extra(0x17, 0x10, expected=False), local(0x0E), extra(0x12, 8)]),
            encounter((9, 15, 18, 22), 0x8080,
                      [extra(0x17, 0x20, expected=False), local(0x0F),
                       extra(0x12, 8, expected=False), local(0x11, 1)]),
            encounter((9, 15, 18, 22), 0,
                      [extra(0x17, 0x20, expected=False), local(0x0F), extra(0x12, 8)]),
        ]
    if scene == 589:
        return [
            encounter((8, 53, 15, 60), 0x0082,
                      [extra(0x1C, 8, expected=False), local(0x0E), extra(0x18, 1, 5)]),
            encounter((8, 53, 15, 60), 0x0002,
                      [extra(0x1C, 8, expected=False), local(0x0E), extra(0x18, 1, 5, False)]),
            encounter((8, 18, 16, 24), 0x0082,
                      [extra(0x1C, 0x10, expected=False), local(0x0F), extra(0x18, 1, 5)]),
            encounter((8, 18, 16, 24), 0x0002,
                      [extra(0x1C, 0x10, expected=False), local(0x0F), extra(0x18, 1, 5, False)]),
            dict(Actor=0, Kind='Switch', Destination=-1, Value=8, Source='Extra', Index=0x1B, Set=True,
                 Left=14, Top=10, Right=31, Bottom=14,
                 Guards=[extra(0x12, 0x10), extra(0x1B, 0x10, expected=False), extra(0x1B, 8, expected=False)]),
            encounter((18, 47, 34, 51), 0x4080,
                      [extra(0x1B, 0x10, expected=False), extra(0x1B, 8), extra(0x18, 2, 4)]),
        ]
    if scene == 598:
        # Actor0 calls actor34 functions3..9 each update. Each checks a distinct
        # room-local encounter latch before reading leader coordinates. The
        # portal is actor8's existing touch action and needs no override.
        return [encounter(bounds, value, [local(latch)]) for bounds, value, latch in [
            ((55, 55, 58, 58), 0x0002, 0x0D),
            ((1, 52, 8, 58), 0x0000, 0x0E),
            ((2, 4, 8, 12), 0x0002, 0x0F),
            ((52, 4, 58, 10), 0x0002, 0x10),
            ((11, 38, 14, 43), 0x0002, 0x11),
            ((48, 38, 51, 41), 0x0000, 0x12),
            ((30, 30, 35, 36), 0x0000, 0x13),
        ]]
    return []


def actor_actions(scene, actor):
    """Audited paths lost to bounded expansion or non-singleton signal waits."""
    if (scene, actor) == (173, 11):
        # Relative Atel offsets: actor11 init1C1..1D8 shows the first Flea
        # only before57&04, afterA3&02, and beforeA3&08. Confirm1DA sets0A=1.
        # Controller12 waits for0A!=0 at233/238 only beforeDF&01; the other
        # branch warps to460 at22D. It then calls D8 C092 at253. This producer is
        # audited separately from the conservative singleton signal rule.
        return [dict(Kind='Encounter', Touch=False, Controller=12, Value=0xc092,
                     Guards=[guard('Global', 0x57, 6, 4, False),
                             guard('Global', 0xa3, 6, 2),
                             guard('Global', 0xa3, 6, 8, False),
                             guard('Global', 0xdf, 6, 1, False), local(0x0a)])]
    if (scene, actor) == (465, 10):
        # Atel0284: 0993 requires point>=4D; 09EE calls actor8 fn3.
        # 04EC..0549 redirects Marle/Lucca/Frog without magic to teaching;
        # local18==1 also takes that branch. 056E tests E1&02 (introduction
        # already heard), then the player's choice at0577 can call actor0 fn3
        # at0587 and D8 E080 at02CD. No choice is made by this metadata.
        from itertools import product
        gates = [guard('Global', 0, 4, 0x4d), guard('Global', 0xe1, 6, 2),
                 guard('Local', 0x18, 0, 1, False)]
        return [dict(Kind='Encounter', Touch=False, Value=0xe080, Guards=gates + list(party))
                for party in product(*[(guard('ActiveParty', member, 0, 1, False),
                                        guard('Global', 0x1e0, 6, 1 << member)) for member in (1, 2, 4)])]
    if (scene, actor) == (618, 13):
        # The local78 branch only animates the door; both paths join at0635.
        # E2A&01 is the sole gate to063F..067D (lockdown announcements).
        return [dict(Kind='Talk', Touch=True, Guards=[extra(0x2A, 1)])]
    if (scene, actor) == (622, 8):
        # 04F5 returns if E2A&08 is set. All character dialogue branches join
        # at0585 and set E2A&04 at058E, before the party restoration branches.
        return [dict(Kind='Switch', Touch=False, Source='Extra', Index=0x2A,
                     Value=4, Set=True, Guards=[extra(0x2A, 8, expected=False)])]
    return []
