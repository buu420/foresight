# First Telepod demonstration navigation, 0.3.16

The September 13, 22:23 log records three failed routes to "Try the left Telepod" in scene 8. The player positions are `(2069,2058)`, `(637,1783)`, and `(1106,2989)`, all on layer 1. Story point is 10; global 0x56 is 1, arming the demonstration without marking it complete. Actor 12 is the bound class 7 marker at `(1152,1279)` with script binding 128.

## Cause and repair

Touch landmarks previously used their tile center as the route goal. Here that produced `(1152,1152)`, inside blocked terrain. It could not become a collision-valid approach point, so both spoken guidance and automatic walking failed before movement started.

For this specific Telepod marker, the source now targets eight pixels below its live position, rounded to the existing four-pixel route lattice: `(1152,1408)` in the recorded scene. This is a reachable point on the floor bordering the pad. Native collision and exit checks still filter the point. Other touch landmarks keep their existing goals. The marker remains in Story Events, and does not become an object requiring confirm. Its script binding must be present and global 0x56 bit 0 must be set. When bit 1 marks the demonstration complete, Story Events selects Marle and cancels an active route to the pad.

The installed scene 8 script is `Atel_0028.dat`, actor 12. Its touch entry at file offset `0x06DF` checks `global[0x56] & 1`; completion at `0x0711` sets mask 2. The earlier 0.3.15 pendant change concerns actor 11 at story point 12 and remains separate. Guide prose supplies context, while the installed script, live actor coordinates, and collision data determine the route. The pinned [CTViewer actor source](https://github.com/GitExl/CTViewer/blob/2e5a206e09f0028fd5a1ca6cb9a9ed18bfc64fea/src/scene/actor.rs) was consulted alongside the native audit.

Claude independently checked the native map and script evidence with Ghidra and an offline collision replay. [The spatial audit](telepod-demo-native-audit.md) records that work and the root review of its limits.

## Verification

Three new regressions failed on the old source because the marker had no approach point. All twelve demonstration tests pass after the repair. They cover I/P from the recorded positions, completion, unavailable script state, invalid or retired markers, collision, and unrelated exit cells. Existing offscreen Story Events and pendant regressions also pass. The full Release suite passed 1,212 tests: Prism 8, Core 128, Native 541, Mod 535.

Six offline replays use the real field source and navigation controller against the installed executable's movement dispatcher at RVA `0x175E90` in Unicorn. They use the three recorded starts, each at movement increments 16 and 32, and the native collision planes from `MapTable_0071.dat`. The emulator contains the non-solid Telepod marker at its recorded position; the native contact selector at `0x178980` reports actor 12 (encoded id 24) before arrival in every run. Every run reaches the contact approach with one plan and no blocked stop. The final feet are `(1157,1434)`, `(1141,1418)`, `(1149,1431)`, `(1149,1431)`, `(1122,1421)`, and `(1138,1421)`.

These replays verify terrain movement and native marker contact, not the entire cinematic or crowd movement. The demonstration still needs a live gameplay check after installation. The game was not controlled during this repair. Local replay programs and results are under `artifacts/research/telepod-demo-0316`; native audit artifacts are under `artifacts/research/telepod-demo-0315`; release proof is under `artifacts/research/release-0316`. Original game assets are not included in the package.
