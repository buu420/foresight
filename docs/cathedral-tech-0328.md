# Cathedral navigation and Tech selection, 0.3.28

## What failed

The user's September 21 log contains two failed routes to the inner Cathedral
door, from scene 130 positions (10368, 6139, 1) and (10368, 3291, 1). Both the Story
alias and exit 5 inherited nine approach nodes that the collision graph could
not reach. The doorway itself was reachable: the only legal positions were
on the edge of its native exit cells, which `ExitApproach` excluded.

Approaches now include the whole four-pixel lattice within an exit cell.
Arrival additionally requires the player's actual position to be in the same
native terminal as the goal. The controller keeps steering if its proximity
cursor advances past the last waypoint before the player enters that cell.
Story aliases inherit this rule through the graph. Collision checks, closed
doors and the rule against routing through an unrelated exit remain active.

The installed Cathedral map reproduces both reported failures in C# tests.
Exits 0, 1, 2, 5 become routable; the obstructed exit 3 remains unreachable; the
chapel exit continues to work. A separate offline survey of 457 native scenes
found 86 exits with the same defect and confirmed that the expanded goals
repair all 86. That survey uses one inferred start per scene and is not a
complete live-route census. Its report includes the scene and exit identities.

## Cathedral story order

The rear hall now supplies one current Story Event: the right wall switch,
then the organ, then the northern passage. The optional left switch remains
an ordinary object. Previously the list included a generic note, both switches,
and the organ together, even while the organ's approach was closed.

This order agrees with [Thonky's Cathedral walkthrough](https://www.thonky.com/chrono-trigger/queen-is-gone)
and the supplied GameFAQs guide, section vb505. Native scene 131 actor 24 copies
the central spike tiles at packet 0CB3 and sets Global FF:02 at 0CBD. Actor 36
sets Global FF:04 at 0F6A and opens the passage. Actor 46 sets FF:08 for the
optional left switch; that bit is not required for the story passage.

## Tech character selection

Two read-only captures identified the silent state as Tech's character
selector. Its MenuNodeBase manager stack is empty. Initializer 1CABE0 builds
the character-ID vector at +2C8; builder 1CB230 builds the corresponding
portrait buttons at +2D4; 1CBCA0 builds their separate text cards under +2EC.
Function 1CCE50 creates the active character manager at +2E8 and focuses
the committed character index +2E4. These buttons contain portraits rather
than Labels, so the old generic reader had neither its manager nor its text.

The reader now correlates manager focus, committed index, character and
button vectors, selected control and the matching visible card. It announces
that card, the selected Single/Dual/Triple category, and the native selection
prompt. All identities and parent relationships are checked again after
reading. Manager +2E8 is now recognized by the existing refresh hook, so moving
between characters updates speech.

Function 1CDD70 replaces character selection with row manager +308. That path
continues reading the selected row, description, components and cost/requirements
panel. A failed row capture never falls back to an old character selection.
No new native hooks or game inputs were added.

The capture fixture explicitly distinguishes a single Slurp-row snapshot from
a composite replay: the character phase captured at 23:46:35 uses its unchanged
owned cards captured separately at 23:51:46 in the same process and menu node.
The composite is not represented as one atomic live capture. Synthetic tests
also cover hidden/detached cards, mismatched vectors, disabled managers and
selection changes during capture. The final live screen-reader check remains
for the installed release.

Ghidra checked these functions against executable SHA256
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Research, red/green runs, native captures, the exit survey and hook verification
are retained under `artifacts/research/cathedral-tech-0328/`.

See [the whole-game coverage audit](whole-game-coverage-0328.md) for what the
broader check establishes and its remaining limits.

## Release validation

All 1,856 Release tests pass: Core 149, Native 831, Mod 865, footsteps 3,
and Prism 8. The executable-byte verification passes for all 151 native hooks.
These results are retained in `release-tests/` and `hook-byte-proof.json` in
the research directory above. They do not replace the final live checks.

## Installed release

Version 0.3.28 was deployed on September 22, 2026 from implementation commit
`17159be`. All 25 deployment checks passed, including the existing launch
redirect. Hash comparison verified all 28 mod files, 45 loader/shared-hook
files and both native launcher files. The game executable is unchanged.

Installed mod DLL SHA256:
`A05CE7C24C0AFF62636EC795E851CC6BCD2D53F88EB435BC592FC52A17DCC848`.
Installed manifest SHA256:
`0A074AD00EB05DDBE6BEA9157F5E95267F8237C1F2A3A3F35B20D0FC9C75BD17`.
The previous installation is retained under the game's
`Accessibility/Backups/20260922-001423/` directory. Full file hashes and release
checks are recorded in `deployment-proof.json` in the research directory above.
