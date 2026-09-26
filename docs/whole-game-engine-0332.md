# Engine and full-game accessibility pass, 0.3.32

This release integrates engine-wide navigation repairs with the existing guide
objectives through Lavos, the classic optional quests, Lost Sanctum, Dimensional
Vortex and Time's Eclipse. Minigame destinations and entry points are included;
their internal controls are deferred at the user's request.

## Menus and endings

The ending-result reader follows the game's ending number, title, completion
count, first-clear result and displayed reward notice. It also reads the native
Yes/No save prompt, including its default selection, then the saving, completion
or failure notice. It uses the live controls and localized text rather than
predicting the ending or changing a save.

Save, Load, Bookmark and Resume notice windows now share explicit menu ownership.
A notice replaces the active presentation, and its native exit retires that
owner so a later menu cannot reuse its text. The Screen Size restart notice is
also read before the game exits. New tests cover stale and reused owners, nested
prompts, destruction during construction, invalid bindings and later recovery.
The save list reads its native StatusBar instruction with the page title,
including the unavailable-save instruction. It does not repeat that instruction
for every file selection. Empty, hidden, foreign or changing StatusBars reject
the capture through the existing retry path.
Independent review found that a late ending-dialog focus callback could publish
after hooks were disabled. Four unload-interleaving regressions reproduced it;
publication and failure reporting now require the captured active epoch and
current confirmation owner. All 73 ending-dialog tests pass after the repair.

Extras now reads Movies, Music and Illustrations, including list focus, native
titles, instructions, playback status and the ending-review controls. Movies and
ending replays retain their gallery owner while the native scene is pushed;
returning to that same node resumes narration. Illustration viewing and Music's
play, stop and natural-completion paths have explicit transitions. Available
menu text is read; images, videos and replay visuals are not described.

Rapid Extras inputs retain each native deferred transition in order, even when
an earlier transition has removed the originating page. Returning from a movie
resumes whichever Gallery page actually exited. Ending Log uses its native
immediate activation behavior, including Back; queued ending choices build
from the native selection at dispatch time, including resets caused by rebuilding
the Log between inputs and dispatch. The final 135 Extras regressions
cover these transitions, retirement, invalid requests and unload behavior.

Claude owns these UI implementations and the Extras extension in this pass;
Codex owns navigation, the independent resource audits, composition and release
verification, including the ending-focus review repair. The navigation and UI
changes received independent read-only reviews. Native evidence and replay
results are retained alongside the corpus.

## Navigation and story

The script compiler now joins equivalent branches without forgetting repeated
actor requests. It retains the active call stack, preserves gate-relevant writes,
and follows delegated menu interactions. Independent regressions reproduce the
previous branch-budget loss and the repeated-call regression before the repairs.

Position checks now constrain where the player approaches an interaction, rather
than hiding the destination while the player is elsewhere. Both room-controller
coordinates and coordinates read by the interaction itself retain their native
provenance. This includes the Cathedral organ and Geno Dome's switches. Native
Geno Dome actors 10, 11, 17 require X>27, X>25, X<24 respectively; the organ requires
X<=17. The entire arrival tolerance must satisfy the relevant predicate.

Alternative destinations remain available across camera boundaries. A story
action combining two touch actors allows contact with either selected actor and
retains unrelated actor collision. A reachable offscreen alternative no longer
disappears because another candidate happens to be visible.

The path search charges its limit once per position, while retaining incoming
headings for turn minimization. Longer arrivals at the same position are pruned;
equal-distance headings remain. This fixes actual search-limit failures in Site 16,
Southern Glade and Frozen Cliffs without removing the bounded search limit.

Spekkio's lesson now selects each clockwise checkpoint from locals 09..0E and
then returns to Spekkio. Marker positions come from live actors 16..20. Native
opcode 8B at RVA 16C040 establishes X+128/Y+255 placement; contact dispatch at
178980 uses radius 224 in scene 465. The west marker lies inside a wall, so its
route ends at a reachable standing point followed by an audited westward contact.
The final contact waits for the native objective to advance. Automatic contact
stops after 1.5 seconds or half a tile without advancement; manual guidance gives
the direction without moving the player. It does not write lesson progress.

## Whole-game coverage checks

The existing guide-to-implementation table remains in
[the story and optional coverage audit](whole-game-coverage-0328.md). It maps the
supplied [vinheim/Bkstunt guide](https://gamefaqs.gamespot.com/pc/233789-chrono-trigger/faqs/64344)
and [Thonky walkthrough](https://www.thonky.com/chrono-trigger/) to the early,
future, main-story, classic-optional and bonus providers. The late-story and
optional providers contain 310 named actions; the opening through the first End
of Time uses the earlier providers and is not included in that count.

Fresh resource checks establish:

- 669 scene records, 1,020 static exits and 240 world entrance records.
- All 343 native treasure records assigned to scenes; 124 direct item/money
  interaction actors all have an item classification.
- 4,732 native actor identities, 143 scripted item actors, 4,827 guarded actions
  and 1,728 spatial region clauses. The reduced region count reflects equivalent
  branch absorption, not a count of removed destinations.
- An independent audit starts from every native actor, rather than only actors
  already in the generated catalog. It finds 199 item/money/menu entry closures:
  194 physical interaction entries all have destinations, and 5 are party or
  unpositioned controller entries. Automatic rewards are not invented as separate
  pickups. This is branch-insensitive entry coverage, not promised reward availability.
- 54 optional-guide actor identities and 44 optional exits match installed data.

The route audit extracts 667 initial collision maps and examines 507 scenes with
exit-neighborhood seeds. Of those, 443 have a reachable exit from an admitted
starting component, yielding 1,375 route searches. All 1,375 pass after the search
repair. Observed median search time was 0.022 ms, 95th percentile 10.36 ms, maximum
108.55 ms on this machine. Scenes without an actual exit search are not counted
as route-tested scenes.

The [CTViewer map decoder](https://raw.githubusercontent.com/GitExl/CTViewer/2e5a206e09f0028fd5a1ca6cb9a9ed18bfc64fea/src/filesystem/maps.rs)
documents short tile-property payloads. The offline exporter records rather than
pads two such resources: map 29/scene 378 (Dream Project staff room), and map 98/
scene 447 (before the final Magus battle). Runtime navigation reads the complete
live collision planes, so these are offline replay exclusions, not disabled areas.

## Evidence and remaining validation

The Release suite passes 2,108 tests: Core 172, Native 851, Mod 1,074,
three footstep audio timing tests run separately, and Prism 8. All 170 hook
signatures match the exact supported executable, and composition prepares each
once. The Python decoder/compiler suite passes 29 tests. Release metadata tests
pin the reviewed 0.3.32 manifest and its exact packaged bytes, using the
repository's required LF line endings for reproducible checkouts.

Classic Bookmark no longer publishes the obsolete unsupported-page announcement
after its supported save confirmation has opened. Its Touch counterpart retains
the explicit unsupported boundary because it uses a different native reader.

The complete local decompilation and searchable index are described in
[full-game-decompilation.md](full-game-decompilation.md). Runtime binaries and
guide prose are not redistributed as source with the mod.

`artifacts/research/engine-0332/implementation/` contains the independent entry
audit, resource report, optional bindings, semantic comparisons, route fixtures,
route results, test failures before repairs and passing results afterward.
Reproduction tools are in `tools/research/future_story/` and
`tools/research/NavigationAudit/`.

Three bounded script expansions remain explicitly reported: Spekkio's large
reward flow and the startup controllers in Millennia Wood and Ancient Keep.
Spekkio is bound through his live actor; the two bonus controllers have separately
audited encounter-region definitions. Eight unused/stub scene scripts remain
listed. Neither list is silently treated as a successful general expansion.

The semantic comparison found five non-coordinate removals: a Hero's Grave
region whose switch was already set, and repeated prison switch writes that
set an already-set bit or cleared an already-clear bit. Independent native review
found no lost destination in those changes. Coordinate changes are checked as
approach constraints, not compared as independent old scratch-cell values.

These checks do not constitute a live playthrough of every quest, altered map,
moving platform or collectible. The keyboard interface is the implemented menu
surface; alternate touch-style submenu readers and audiovisual descriptions of
movies remain separate coverage limits. This release does not label those as
tested or complete.
