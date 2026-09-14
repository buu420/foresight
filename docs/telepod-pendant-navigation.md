# Telepod pendant navigation, 0.3.15

The September 13, 21:44-21:46 log places the player in scene 8, Leene Square's Telepod exhibit, at story point 12. The pendant is present in that same area: actor 11, class 4, visual 99, drawn, loaded, with script binding 128 and live position `(1024,1448)`. Its activation byte is zero. Story Events consequently said the destination was not active in the current area, and neither I nor P could plan a route.

Version 0.3.14's earlier repair covered the fallen pendant in the rear plaza, scene 439. The same log confirms that pickup succeeded and the player progressed through the demonstration. This report concerns the separate pickup after Marle disappears.

## Repair

The installed scene table maps scene 8 to `Atel_0028.dat`. Actor 11 loads pendant visual 99 at file offset `0641`, checks story point 12 at `0648`, and hides the sprite outside that state. Its action and touch entries share the pickup handler at `0657`; that handler hides the pendant at `0660` before continuing the departure scene. These offsets are hexadecimal.

The object filter now admits that exact scripted pickup at story point 12, with a live script binding. Existing checks still require a usable, drawn actor inside a coherent current map. Coordinates and collision approaches come from native state. The rear-plaza pendant keeps its separate dropped/collected flag checks. Other sprites do not receive this exception.

The pendant is available under Interactable Objects and supplies the current Story Event without requiring discovery. The objective identifies the left Telepod, and arrival tells the player to face the pendant and use confirm. Both confirm and touch bind the pickup handler. Hidden, removed, out-of-area, and collected actors cannot retain a discovered route position. A missing destination now says that the mod cannot locate it; absence from the mod's candidates does not establish that it is absent from the area.

Claude independently audited the native opcode handlers and the pickup's immediate transition. The counter remains at 12 as the script jumps to scene 113, so the existing Truce Canyon objective has no progress gap. The Telepod demonstration trigger is disabled after Marle disappears and cannot restart the demonstration during the approach. See [the native audit](telepod-pendant-native-audit.md) for exact script offsets, function addresses, and its inference limits. The root review checked the actor's script separately and verified the activation-byte write and scan instructions against the installed executable.

## Guide

The user opened [A Tadeo's guide, Final version, February 17, 2001](https://gamefaqs.gamespot.com/pc/233789-chrono-trigger/faqs/8268) in Chrome. Its opening walkthrough directs the player into the left Telepod to retrieve the pendant after Marle disappears. That provides useful location context. It is a SNES guide hosted under the PC listing; the installed PC scripts and live actor state determine availability, coordinates, and transitions. Guide prose and original game assets are not redistributed with the mod.

## Verification

Two regressions reproduced the reported failure before the source change, covering I and P from the logged positions. Thirteen new tests cover both navigation modes, offscreen availability, retirement of a previously discovered pendant, the following area's objective, unknown story state, and exclusion of unrelated or inactive actors. The Release suite passed 1,200 tests: Prism 8, Core 128, Native 541, and Mod 523. All 128 hook contracts matched the installed executable.

The installed `MapTable_0071.dat` was decoded into its native collision planes. Its SHA-256 is `D8E1D8DB78F2D5E4AF1DDF2AD5CB59B65FA5BCF1E40FDC7CC802012E211C6CF5`. Six offline replays connected the real navigation controller to the executable's movement dispatcher in Unicorn, using the recorded starts `(1788,2548)`, `(2188,1044)`, and `(1798,1009)`, each at speeds 16 and 32. Every replay reached the pendant's southern approach with one route plan and no blocked stop. The goal was `(1024,1728,1)`, and the final foot positions were within the existing arrival tolerance.

These replays verify terrain movement. Dynamic actors, dialogue, actual pickup execution, and map transitions still need the next in-game check. The game was not controlled during this repair. Local research, the failure log, test results, and replay evidence are under `artifacts/research/telepod-pendant-0315`; deployment evidence is under `artifacts/research/release-0315`.
