# Engine accessibility coverage

The complete native and script export is finished; see [the decompilation report](../../full-game-decompilation.md). The user's latest instruction authorizes implementing and deploying the full-game accessibility work described here. Minigame destinations and entry points are required; minigame internal controls are the explicitly deferred scope.

The user wants ordinary play to remain accessible across the entire PC game without having to report each missing menu or route. The existing controls, counted footsteps, guide categories, and native game behavior remain the interaction contract. The user has authorized Ghidra research, use of their guides, Claude Opus 5.5 at maximum effort, implementation, and deployment.

The deliverable is broader engine coverage backed by a reproducible inventory and tests. Catalog counts do not establish that a scene works, and decompiling a function does not establish that its semantics have been correctly understood.

## Architecture

Retain the existing version-checked hook/capture/semantic-event/narrator architecture. Native state supplies control ownership, current selection, displayed values, positions, collision, and action availability. Share verified mechanisms between surfaces instead of introducing guessed universal readers. Ordinary movement input performs navigation; the mod does not teleport, change flags, pick dialogue answers, or perform combat decisions.

Create a searchable native engine atlas containing function boundaries, callers/callees, strings, dispatch evidence, and decompilation status. Index all game resource families and reachable event opcodes independently of the existing accessibility hook list. Tie each supported surface and movement mechanism to its native owner and verification evidence; record uncovered and unverified states explicitly.

UI work covers normal keyboard startup, setup, menus, dialogue, battle, shops, save/load, and ending transitions. Inventory the alternate interface separately and implement it only with verified ownership/selection contracts. Blank control-only dialogue pages, empty lists, disabled entries, nested owners, and teardown are normal states where supported by native code, not inferred failures.

Navigation work covers scene and world movement, actor interactions, field treasure and script pickups, live exits, script terrain changes, disconnected landings, and vehicles. Audit all resource scripts for omitted interactions and ambiguous/incomplete expansion. Route candidates must be available in the current game state and reach the native activation boundary. Route validation must use the chosen destination or intermediate passage's contact rules, preserve barriers for unrelated targets, and update when terrain or actors change.

## Verification

Use independent native instruction/decompiler evidence, resource and control-flow audits, native-motion replay where practical, and transition regressions. Include negative cases that would otherwise expose unavailable targets or announce stale information. Keep live gameplay verification distinct from replay and static proof. Do not require the user to visit every screen just to discover which readers exist.

Package and install the verified result when the game is closed. Preserve an installation backup, compare package and installed bytes, and keep the executable unchanged. Report concrete behavior changes and remaining limitations without calling an unplayed whole game verified.
