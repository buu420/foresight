# Tyranno Lair terrace routes and doorway labels

This update addresses the tester's `2026-10-13 06.15.45 ~ Chrono Trigger.txt` report. At story point 0x98, the guide repeatedly returned from the indoor rooms to the same exterior section, then selected a closed doorway. Several distinct rooms share one scene ID, so a route based only on scene connections skipped the passages and controls needed to continue.

## Native evidence

The installed executable still has SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`. Fresh read-only Ghidra exports verified the native floor selection, layer acceptance, and Confirm scan at RVAs 178C50, 178FF0, and 17D230. The installed map tables, exit records, and Atel scripts establish the connections below. Ghidra output is decompiled pseudocode, not recovered game source.

Scene 298 is the exterior. Its four disconnected walking sections have different onward passages:

| Section | Available passage identities before opening the exterior gates |
|---|---|
| Northwest terrace | Exits 0 and 1 |
| Northeast terrace | Exits 2 and 3 |
| Middle terrace | Exits 4 and 6 |
| Southern terrace | Exits 7 and 9 |

Exits 5 and 8 remain in the live exit grid while their doors are closed. They have no compatible approach points. Atel_0164 applies the opening floor copies on load only when global 0xED bits 0x80 and 0x40 are set. The captured log has both bits clear.

Scene 303 has two separate skull rooms. Atel_0167 establishes floor switches in the eastern room, a Confirm skull interaction in the western room, and a wall switch inside each skull. The wall switches toggle the exterior gates; repeating an already completed switch would close its gate. The guide therefore checks the current gate and skull state before choosing an interaction. Equivalent floor switches remain choices without announcing their concealed consequences.

The native continuation from the captured northwest terrace goes through scene 305, its exit 0, and the western small room in scene 302 to the middle terrace. The middle and southern terraces then use their own skull rooms and gated passages. The small rooms in scene 302 have separate exit identities despite sharing one scene ID. Scene 304's native encounter and scene 300's native story sequence control the later passages.

The [original game background map](https://www.snesmaps.com/maps/ChronoTrigger/ChronoTrigger65mTyranoLairBG.html) was reviewed for the visible doorways, skulls, switches, and separated courtyards. Installed PC data and the captured floor remain the authority for routes and availability.

## Player behavior

Doorway names distinguish their terrace, position, and connected passage where verified. Skull-room controls receive appearance labels. These labels avoid revealing unvisited boss names or which puzzle switches cause traps or battles.

The existing story objective uses the next native passage or interaction in the current section. Manual guidance and automatic walking bind to the same captured approach points. After a scene change, opened skull, completed encounter, or gate change, a fresh capture supplies the next leg. The mod never presses Confirm, changes game flags or terrain, or plays battles.

The two exterior gates are omitted when unseen or unremembered and without a usable approach. A visible or remembered gate remains readable with an explicit restriction. This rule is limited to the verified Tyranno gates; automatic doors elsewhere retain their established staged handling.

## Verification limits

The exterior and warp-room regressions use unchanged live captures from the tester's log. Future rooms use installed native floors, scripts, and their verified tile copies; these checks are not live traversal. In particular, scenes 300, 303, and 304 still need tester confirmation of walking, controller behavior, and spoken interactions. The per-load skull state is checked again after re-entering a room.

The earlier prison reveal and visible Kino interaction remain in place. Existing recorded descriptions, movie narration, separately installed packs, saves, and settings are preserved.
