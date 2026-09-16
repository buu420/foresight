# Lost Sanctum, Dimensional Vortex, and Time's Eclipse objectives

`BonusStoryObjectives.Build(scene, state)` selects the next actionable steps from
the installed PC scripts. `All` exposes the same immutable catalog for binding
and coverage checks. `FullStoryObjectives` aggregates this provider. The optional
entries use People for conversations, Objects for items and mechanisms, and
Exits for entrances and Gates. A sequence is represented by successive state
selections; multiple goals on one entry are alternative ways to perform that
same step.

The implementation was researched against executable SHA-256
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`
and its installed `resources.bin`. The primary evidence is the decoded native
Atel scripts, their dialogue and item operations, and targeted Ghidra/Capstone
inspection. Broad quest ordering and names were cross-checked with the
[Lost Sanctum overview](https://www.chronowiki.org/wiki/Lost_Sanctum),
[Dimensional Vortex overview](https://www.chronowiki.org/wiki/Dimensional_Vortex),
and [Wonder Rock entry](https://www.chronowiki.org/wiki/Wonder_Rock). The web
guides do not supply runtime flags or positions; the installed PC data does.

## State and positions

In the tables below, `E12:08` means extended cell 0x12 has bitmask 0x08 set.
`E18=3` means the complete cell equals 3. All cell, item, and bitmask numbers are
hexadecimal unless a decimal scene or actor is explicitly named. Extended cells
are captured as full native integers, not truncated bytes. Unknown inventory is
different from a known empty inventory: a fetch step that depends on absence
requires `ItemCount(item) == 0`.

Hand-in completion flags are checked before inventory. Consuming the Golden
Hammer, building materials, vines, lunch, Waystone, Rusted Blade, or Lumicite
Shard therefore cannot restart the completed fetch step. Independent quest lines
can remain available together. Forest encounter latches are not substituted for
the persistent quest-clear bits.

Actor goals bind to the current native actor identity, activation/contact
binding, and captured position. Spatial goals bind to exact inclusive leader
coordinate predicates decoded from the scripts. Off-room goals use the native
scene connection router. Treasure goals use the native chest identity. An
unavailable optional binding is omitted; no guide coordinate or unpositioned
People/Objects entry is invented.

Arrival goals are limited to scripts that actually advance on entry: the first
Sanctum visit, Nu Master encounter, bridge rescue/work/completion scenes, lit
cave transition, fortress encounter and village reports, tower idol room, lab
party transition/alarm chamber, and automatic Shade sequences. Once already in
such a scene, the script or its captured contact encounter supplies progression.

## Lost Sanctum coverage

The provider runs in the Sanctum rooms and, from story point D2, the world hubs
496 through 502. The D2 boundary is the flying Epoch stage; it does not defer all
optional work until point D4. Actual world entrances remain gated by the native
world frame: world1 entry74 leads to scene654, and world3 entries25/26 to653.

| Quest / phase | Native selection and completion evidence | Bound action |
|---|---|---|
| First visit | Entry sets E1F:01 | Scene653 startup |
| Prehistoric Millennia Wood | Persistent group bits E16:04/08/10/20/40/80; completion E12:08 | Scene587 actors10/11/12,13,22 and two controller encounter regions; bit40 is set by startup |
| Prehistoric forest report | Village report sets E11:08 | Scene583 controller approach region |
| Millennia Wood, 600 AD | E18=1 request; E1C:01/02/04/08/10 groups; E18=2 clear; E18=3 report | Scene585 request/report regions; scene589 actors9/11,13,17 and two encounter regions |
| Golden Hammer | E10:01 request; Golden Sand5019; E12:10 sapling planted; E1B:08 introduction and :10 Goldhammer caught; E10:02 hand-in | Scene583 actor9;576 actor9;587 actor48;589 separate introduction and fight regions |
| Prismastone | Requires Hammer completion; E10:04 request; E1F:20 Nu Guardian defeated; E17:80 Prismastone collected; E10:08 shown | Scene583 actor10;532 actor9;535 actor9; item501B |
| Saintstone | E10:10 accepted; E1F:40/80 prehistoric altar occupancy; E1B:02/04 future copies; E18=4 Reptmark request; E1A:80 future shrine open; E10:20 shown | Scene583 actor14;584 altars8/9;585 elder8;589 Reptmark8;586 altars8/9; item501C |
| Nu and broken ladder | Requires Prismastone shown and middle forest clear; E19:04 request; E1D:01 Nu moved; E15:40 broken ladder inspected; E1E:80 vines tried; E1B:20 vines hung; E15:80 Nu Master met; E19:08 report | Scene585 actor9;536 actor18 and ladder region;577 actor8;532 vine region;538 startup |
| Bridge materials | E19:01 request; E1A:01 Godwood, :02 Hammer, :04 Steel partial deliveries; E19:02 all delivered | Scene585 builder14;587 woodcutter49;583 lender9;581 chest301 |
| Missing bridge builder | E19:02 and :08 required; re-entry sets E1A:20; E19:10 request; E16:02 Nu conversation; E1D:02 rescue; E1B:40 help arranged; E15:08 Nu at bridge | Scene654 entry;585 actor12;538 actor9;556 startup |
| Lunch and Nu food | Hearty Lunch5022 hand-in sets E1B:80; Sweet Banana401E sets E1D:80 satisfaction; other fruit does not finish it | Scene585 actor9;556 builder9 and Nu8;576 sweet-fruit encounter region |
| Complete bridge | Requires Nu satisfied, lunch delivered, and E11:02 village defended; E1A:40 bridge complete; E19:20 report | Scene556 startup;585 report region |
| Waystone and Lightless Cave | E31:01 hint; Saintstone placement consumes501C and sets E12:20; Waystone501E pickup E1F:04; E10:40 cave request; cave entry consumes501E and ultimately sets E13:01 | Scene583 actors12/13;535 placement region;539 actor8;580 startup to579 |
| Primeval Fortress discovery | First fortress encounter sets E14:01; village meeting sets E11:01 | Scene540 startup, then583 startup |
| Defend the village | Enemy leaders set E14:02; village report sets E11:02 | Scene555 startup, then583 startup |
| Tower of the Ancients | E19:40 request; E1F:08 guardians beaten; E1B:01 idol room visited; E19:80 report | Scene585 request/report regions;575 top approach;652 startup |
| Prehistoric smith | E14:10 request; Rusted Blade5023 consumed, E14:20 completion | Scene583 actor11;570 chest286 |
| Smith in 600 AD | E14:40 request; Lumicite Shard5024 consumed, E14:80 completion | Scene585 actor11; local native Wonder Rock actors537/15,587/24,589/29 |

The Saintstone offer itself requires the held Prismastone. The player can place
it in the prehistoric shrine before accepting the offer. The provider detects
this reversible branch and points to the occupied altar to recover it first:
scene583 actor14 tests item501B at packet offset0AF8 before setting E10:10 at0B1A;
scene584 actor8/9 retrieval clears the altar bits and returns item501B at031B or
0373. After acceptance, the selected empty prehistoric altar depends on which
opposite altar contains the original stone; the future copy is selected from its
own occupancy bits.

The bridge depends on several independently completed errands. Food does not
replace village defense, and village defense does not replace food. The material
selector accepts partial hand-ins and never asks for a material already recorded
in E1A. The prehistoric smith temporarily has defense dialogue while E11:01 is
set and :02 is clear, so that trade is suppressed during that interval.

The Steel Ingot is native treasure301 in scene581 at tile25,2; the Rusted Blade
is treasure286 in scene570 at tile21,7. The objective binds `chest:301` or
`chest:286`; the native treasure provider owns its availability and position.
Other bonus-area chests remain ordinary native treasure entries rather than
duplicated quest steps.

Wonder Rock is native enemy263 (0107) in the installed English monster table.
Its random appearance cannot support a guaranteed off-room fetch destination.
The three local hunt entries require an accepted, unfinished smith request,
known absence of Lumicite, the corresponding current scene, and a usable live
actor target. They do not route to a random spawn in another room. A Shard gained
elsewhere still selects the smith hand-in. Scene603 also contains a Wonder Rock,
but its contact script has no audited activation action in this provider; no
quest-specific coordinate is synthesized for it.

## Dimensional Vortex and Time's Eclipse

The three Vortices can be cleared in any order. `E0A` is the overall post-Vortex
and Eclipse sequence; it is not an era selector or a sequential clear count.
Actual Shade victories are separate bits in `E22`. Grotto/return bits in E21
must not be used as victories.

| Phase | Native state | Bound action |
|---|---|---|
| Enter present / future / antiquity Vortex | Global1FB masks01/04/40 respectively; unfinished E22 mask04/02/01 | Scenes595/661/662 forward controller region sets E28=0; actor9 is the return Gate |
| Shuffled rooms | E20 chooses native destinations; no fixed route is assumed | Native forward connections toward Nameless Cave601/663/664 actor10 |
| Antiquity | Frozen Cliffs602..607,648; Marle required for the Grotto sequence | Scene666 Grotto approach;645 Alabaster Shade approach region |
| Present volcano | E23:01 entrance platform switch; E23:02 ladder switch | Scene608 and613 switch regions |
| Present Grotto | Dalton victory E21:20; Crono required afterward | Scene624 Dalton approach, then forward Grotto approach |
| Future laboratory | E2A:01 first security release; rescue transition sets :02 and clears :01; alarm completion :10; administrator registration :04; second release restores :01 | Scene620 console8;618 contact marker13;617 startup;622 startup and console8;620 console8 again |
| Future Grotto | Lucca required for the Grotto sequence | Scene665 Grotto approach;647 automatic encounter sequence |
| Alabaster / Crimson / Steel Shade victories | E22:01 / :02 / :04 | Scene645 actor8 f6 at035B;647 actor8 f6 at0328;646 actor8 f6 at0333 |
| All three cleared | Each victory checks E22=7, then assigns E0A=1 | Gaspar464 actor28 assigns E0A=2 |
| Time's Eclipse | Gate requires E0A>=2; arrival assigns3; initial Magus scene assigns4 | End of Time464 actor9, then591 actor16 Fight portal |
| Dream Devourer and ending | Battle592 followed by593/594; E0A=4 was set before this battle | No quest entry during the battle/ending rooms |

The Grotto coordinates are supported by the native spatial effects at the same
forward trigger. Their objective category is Exits. The required party member is
included in the instruction, while the native guarded target controls whether
that particular transition is available. An ending or party-change animation is
not represented as a free-standing interaction at a guessed character location.

### Native first-clear entrance predicate

Zero-valued E0A and E22 also occur before the first game clear. They alone cannot
make a Vortex entrance available. This was checked in the world scripts and
native memory transfer:

* World0 System1BB2:01 calls the entrance-enable routine for entry80, scene595.
* World2 System1BB2:04 enables entries20..23, scene661.
* World6 System1BB2:40 enables entries7..10, scene662.
* Native RVA270720 copies actor globals starting at actor+11870 (Global1F0) to
  world memory starting at +2FBA7 (System1BA7). Thus System1BB2 is Global1FB,
  stored at actor+1189C. RVA2707F0 performs the inverse transfer.
* Native clear-save setup at RVA2B214E writes actor+1189C=45.

`vortex-unlock-disassembly.txt` records the relevant native instructions. The
provider uses the corresponding Global1FB bit for world/End of Time entry
objectives; being inside a Vortex is itself evidence that its entrance was
reached. The routing layer still uses the live enabled entrance.

### Eclipse completion boundary

E0A=4 is written in scene591 before fighting the Dream Devourer. It must not
retire the challenge as completed. The final native FF9C handler switches the
ending mode at actor+12194 to0E. Ending records exist separately around
actor+6938 and are not part of `FieldStoryState`; a persistent Dream Devourer
defeat bit has not been established from the captured fields. The provider keeps
the unlocked Eclipse challenge available for replay and suppresses it during
scenes592..594. It does not claim that E0A or the ending cutscene cell proves a
saved victory. Capturing an audited ending record would be required to label a
first clear separately from a replay.

## Reproduction and checks

Native research artifacts are in
`artifacts/research/full-story-0323/bonus/`. `inspect.py` decodes the installed
packet and optional English dialogue, for example:

```powershell
py -3 artifacts/research/full-story-0323/bonus/inspect.py 583 14 --text
py -3 artifacts/research/full-story-0323/bonus/inspect.py 584 8 9 --text
```

`scene-*.txt` contains the retained script extracts. `decomp-flags.txt` and
`decomp-victory.txt`, with their runner commands, record the bounded Ghidra
research. `vortex-unlock-disassembly.txt` records the entrance proof.

The general script walker has finite path budgets. Audited overrides for
scenes587,589,598, the lab618 contact marker, and lab622 console are in
`tools/research/future_story/bonus_regions.py`. Their exact native bounds,
preconditions, controller identities, call slots, and packet offsets are listed
in `artifacts/research/full-story-0323/bonus/spatial-overrides.md`. The overrides
retain complete pre-action predicates without expanding unrelated subsequent
party cinematics; compiler warnings remain visible in the research audit.

Focused checks:

```powershell
dotnet test tests/ChronoTriggerAccessibility.Mod.Tests/ChronoTriggerAccessibility.Mod.Tests.csproj --no-restore --filter FullyQualifiedName~BonusStoryObjectivesTests --verbosity quiet
```

These tests cover partial deliveries, consumed items, unknown inventory,
reversible altar placement, persistent forest and smith completion, bridge food
and defense prerequisites, all eight Vortex-clear combinations, first-clear
entrance gating, the laboratory's second security release, and the pre-battle
meaning of Eclipse stage4. They also require every declared actor and spatial
target identity to exist in the embedded native catalog and check the audited
forest, Goldhammer, and lab-action predicates. Live end-to-end playthroughs of
these bonus quest lines have not been performed by this research task.
