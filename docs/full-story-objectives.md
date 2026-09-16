# Story navigation through the ending

Version 0.3.23 extends the existing opening, fair, Middle Ages, trial, prison and future-area objectives from story counter77 through Lavos. FullStoryObjectives supplies the main chain; ClassicOptionalObjectives and BonusStoryObjectives supply optional chains. Optional actions remain in People, Interactable Objects or Exits. They appear at their relevant locations and travel hubs when their native prerequisites are met.

The guides establish the sequence and plain-language descriptions. Installed PC event scripts establish scene identities, interaction actors, spatial triggers, possession checks and completion flags. Ghidra verifies the native opcode layouts and captured state. The supported executable SHA256 is 8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7.

## Main chain

The chain covers Medina and Heckran Cave; the bridge rations and Zombor; Denadoro, Tata and the broken Masamune; Ioka, the Forest Maze and Reptite Lair; reforging the sword; Magus's Keep; Laruba and the Tyranno Lair; Zeal and charging the pendant; Keeper's Dome and the Epoch; Algetty and the Mountain of Woe; the Ocean Palace; the Blackbird escape; North Cape; the time egg; and the approach through Lavos's shell to the final battle.

A chapter can contain several steps. Its current objective advances when the game's own state records the preceding action. Examples:

- Zenan Bridge: ask the captain, ask the cook, meet the cook while leaving the castle, then deliver the rations. GlobalA9 masks04,08,10 distinguish the handoffs.
- Masa and Mune: approach their encounter trigger, win the fight, then pick up the sword. The item actor is not the encounter marker.
- Frog: speak to him before examining the sparkling chest. The hilt pickup is separate from the conversation.
- Reforging: speak to Melchior, follow him into the workshop, then collect the repaired sword upstairs.
- Magus's Keep: inspect both wings, return to the entrance light, defeat Slash and Flea, then use the newly opened passage to pursue Ozzie.
- Ocean Palace: both upper switches, the central switch, both lower wall switches, the bridge switch, then the Golem Twins. The machine cutaways change the story counter without completing these switches.
- Blackbird: discover the air duct, recover all three equipment sets, money and items, then reach the left-wing encounter. The ceiling, supplies and equipment each have separate native flags.

Scene-specific objectives keep movement pointed forward inside Lavos and during the Blackbird's initial cell sequence. Story navigation does not answer dialogue, operate a puzzle's button sequence, select a battle command or make optional story choices.

## Binding and availability

A StoryGoal names an exact scene plus native actor indexes or stable target IDs. Multiple goals are alternatives for the same action. Ordered actions are separate objectives. An empty actor/target list is reserved for an audited arrival event or a scene with a current encounter/progress trigger.

FullStoryTargets binds those identities to captured positions and collision-checked approaches. SceneConnectionRouter selects the next available passage when the action is in another room. Runtime exit destination records override static MapJump destinations. Script floor regions retain their actual coordinate bounds; contact-only switches use a point on the switch, while ordinary conversations use adjacent approaches.

Guards use captured story globals, extended cells, local variables, inventory, money and party membership. Unknown state does not count as completed. Optional chains retire from their own flags, rather than assuming the main counter proves they are finished. A missing connection is reported; the resolver does not manufacture a coordinate or silently advance the quest.

See [classic optional chains](classic-optional-objectives.md), [bonus chains](bonus-story-objectives.md), [native navigation](whole-game-native-navigation.md), and [vehicles](vehicle-navigation.md).

## Validation limits

Mechanical tests check scene and actor identities, prerequisite transitions, route binding, optional categories and completion-state regressions. Native replay tests cover captured movement rules and vehicle geometry. These checks do not establish a complete live playthrough of every quest or prove timing behavior in every moving-platform and scripted encounter sequence. This release retains explicit diagnostics for unavailable state and blocked movement.

## Reference guides

- [The supplied GameFAQs PC guide](https://gamefaqs.gamespot.com/pc/233789-chrono-trigger/faqs/64344)
- [Thonky walkthrough](https://www.thonky.com/chrono-trigger/)

Research artifacts under artifacts/research/full-story-0323 retain native script dumps, extracted metadata, Ghidra output, flag audits and test results. Guide prose and game dialogue are not embedded in the navigation catalog.
