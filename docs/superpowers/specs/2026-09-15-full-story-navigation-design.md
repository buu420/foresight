# Whole-game story and optional navigation

The user authorized completing the story, all optional material in its appropriate
category, and all reachable pickups. This extends the repeatedly approved category,
manual guidance, and automatic walking design. No further control-design approval
is needed. The work uses the existing isolated navigation worktree.

## Behavior

Story Events continues from the first End of Time through Lavos. Its current
objective changes with native progress and quest flags. It offers the next local
interaction or exit toward that objective. Routes end at room/era transitions;
the player still operates Confirm, dialogue choices, battles and puzzle inputs.
Instructions describe needed actions and the next area when the movement engine
cannot itself perform the transition. They must not silently substitute a note
for a known routable local target.

People, Exits and Interactable Objects expose active guide destinations across
the whole game without camera discovery. People and objects retain their actual
category. Optional quests do not replace the main objective. Sealed boxes,
switches, shops, scenery interactions, chests and non-chest pickups remain
available only according to their native state; collected/retired actors vanish.
No unopened reward contents, hidden enemy statistics or unearned items are exposed.

The existing U/O, J/L, K, I, P and F8 controls remain. Manual directions announce
one leg at a time and recalculate after deviation. Movement uses the native
collision map and current actor positions, with no invented coordinates.

## Components

1. A reproducible, hash-checked installed-resource audit records scene exits,
   actor identities, interaction types, story transitions and pickup coverage.
   Generated navigation metadata contains short labels and numeric facts, not
   extracted dialogue, graphics, or a copied guide.
2. A whole-game metadata catalog names exits and verified interaction markers.
   It cannot activate actors or override collision. Existing audited early
   labels take precedence.
3. A late-story catalog defines stages and quest predicates from the PC scripts,
   with guide references for the intended sequence. A scene connection router
   binds each stage to a current native actor or exit; it avoids unrelated era
   shortcuts and checks local reachability before selecting an exit.
4. Native context captures the additional globals used by those predicates,
   retaining coherence checks and unknown-state behavior.
5. Optional chapter and bonus-area handling uses the same active scene metadata;
   conditional goals are checked separately from static route candidates.

## Research and acceptance

Use the user's recovered GameFAQs guide and the second available guide, community
PC format research, installed resources, and fresh Ghidra evidence. Claude owns a
bounded coverage/native-fact audit; root owns implementation and verification.
Community SNES offsets are evidence to compare, never PC runtime addresses.

Acceptance requires full chapter and side-quest coverage records, no artificial
story-point 77 cutoff, native-bound pickup and interaction targets, regression
coverage for stage changes/unknown flags/retirement/closed passages/bonus scene
IDs, and an installed tested package. Report offline verification separately from
live playthrough evidence. Do not claim that tests amount to a complete playthrough.
