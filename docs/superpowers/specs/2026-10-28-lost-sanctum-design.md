# Lost Sanctum navigation repair

The user approved correcting Lost Sanctum's current quest objectives and usable
passages, exposing the open cave entrance, and repairing guidance and auto-walk
to the cave, bridge, Nu and builder. Keep native quest choices, locks, battles,
timing and player Confirm. Use the existing native state and navigation pipeline.

The supplied 0.3.80 report is accompanied by Lost Sanctum Journey.zip, whose
gameplay log has SHA256 a23e0bb9ae9984c69c54e8d79232447f512b2cae8f537f908c747e06e50e3eda.
Research and retained captures live in artifacts/research/lost-sanctum-20261028
in the canonical checkout.

Correctness means that an available local interaction has an identifiable target
and a route to its native contact in both modes; room look-ahead must respect
disconnected sections. Story Events must track accepted quests and their actual
remaining steps instead of directing a Sanctum visit toward Lavos. Unreadable
state must not invent a route or reveal an unaccepted quest.

Approach: repair the audited metadata and narrow contact/room bindings at their
source, with captured-state regressions. Labels alone cannot repair these failed
routes; broad collision bypasses would violate native movement restrictions.
No game-state writes, automatic Confirm, narration regeneration or new hook is
part of this repair. Existing source providers remain responsible for existence,
visibility and collision. Live gameplay remains a separate tester check.

Build a versioned tester package and deploy locally after automated verification;
public publication is a separate user action. Preserve saves, configuration,
narration, original game assets, dependencies and other mods.
