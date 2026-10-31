# Bonus-area names in 0.3.88

The debug-map string bank ends at scene495. Bonus field scenes instead read the
name that the game's top menu builds at EXE RVA 0x0B1D10. The current game owner at
image + 0x41B4C4 supplies scene + 0x120C0 and the unpacked MapMsgDataTable pointer + 0xFE28,
with count + 0xFE2C. The scene-indexed pointer identifies a record whose DWORD + 4 is a one-based
map.txt name row. Zero,255, missing rows and invalid counts provide no name.

Bank 0x22 in the loaded text manager supplies localized MSG_FIELD_* text. Keys
are named, such as MSG_FIELD_MENU_36, rather than numeric row IDs. The reader
removes the key and native soft line break, resolves the current saved names
for NAME_CRO/LUC/MAG, and rejects unknown tokens. Owner, scene, table, record,
name and text-bank ownership are checked for consistency. The change is confined
to the bonus scenes; existing base-area and special room labels remain.

The offline catalog removes soft line breaks from map.txt fallback names.
Exactly 30 Name fields change; all 669 scene connections, interactions, guards,
treasures and all other catalog fields are unchanged. Debug map titles retain
their floor/section separator. Native discovery flags at + 0x688C still select
the game's localized unknown-name row when a reused area name is unlearned.
This includes the Truce Canyon alias in bonus scene 634. A corpus-anchored test
checks scene 614, row 139, key MSG_FIELD_MENU_45. The supported executable/resource hashes and
native battle-name findings are recorded with the release evidence and battle
interface audit.
