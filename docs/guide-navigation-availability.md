# Guide navigation availability, 0.3.12

The player asked to follow Story Events without discovering each destination, and to put optional guide content in its proper category. This supersedes the earlier discovery restriction for guide targets. The supported story stretch remains the beginning through the first End of Time visit.

Story destinations now bind to active native actors, scenery, the entire current exit grid, and audited script transition regions. Their positions are available outside the camera. The controller accepts an explicit guide-availability flag without pretending the destination was seen. Inactive actors, retired FF,FF coordinates, missing native records, unknown progress flags, and collision retain their actual meaning. A missing native destination reports that it is not active; it never tells the player to discover it.

Optional guide entries appear under People, Interactable Objects, and Exits. Names include fair vendors and activities, Melchior, Gato, the lost-kitten girl, Truce tutors, ferry ticket sellers, Porre's mayor and pianist, Banta, Toma, merchants, innkeepers, and the cathedral nuns. Scenery includes Enertrons, switches, consoles, documents, and unopened treasures. The optional prisoner rescue, side-room switch, sign, document, and nun conversations are no longer duplicated as story requirements. Truce and Trann exploration stops now offer a route onward in Story Events; their optional visits remain in Exits.

Optional area labels cover 44 native exits in 29 scenes. World entrances come from the live enabled entrance table and use the current connected walking region. Story bindings share their 64-goal search budget across alternative stairs, so a large exit on another floor cannot consume every goal. Routes retain the existing one-leg manual directions, autowalk controls, countable footsteps, collision graph, and player-operated Confirm input. Routes remain within the current area and stop at transitions.

Unopened treasures include native non-chest pickups, labeled "Item pickup". They are available without discovery in the supported chapters, use the current active treasure grid and opened flags, and disappear after collection. No reward contents are disclosed. A sealed door or other physical obstacle can still prevent a route; opening it updates the native collision data used by subsequent route searches.

## Evidence

The requested [vinheim and Bkstunt guide](https://gamefaqs.gamespot.com/pc/233789-chrono-trigger/faqs/64344) was read from its previously captured reference in the user's Dropbox archive. Additional sequence reference: [Thonky, Beyond the Ruins](https://www.thonky.com/chrono-trigger/beyond-the-ruins). Names and routes were checked against the installed PC resources, using the [CTViewer format reference](https://github.com/GitExl/CTViewer) and the existing local decoder. No guide text or game dialogue is bundled with the mod.

Claude supplied 35 optional actor labels and an audit. Root checked the native tuples and dialogue/shop records, added Porre and 600 AD names, moved optional duplicates out of Story Events, and corrected two object labels to "Candy stall" and "Market goods". A label-only change was insufficient: offscreen actors were also excluded by the source, and the controller rejected undiscovered targets. Both layers now support guide availability.

Fresh Ghidra analysis used executable SHA256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`. RVA 179690 initializes the treasure grid and shared-scene aliases. RVA 179940, reached from 17D0C0, checks the same treasure identifiers and opened flags for chests and non-chest pickups; property bit 1 and the graphics byte select chest animation rather than pickup eligibility. The optional capture path therefore admits unopened non-chest records while the ordinary capture default still requires rendered closed chests. The appearance and exact actor-slot checks, map bounds, and activation checks remain in force.

Research reports are under `artifacts/research/story-discovery-0312`. The tracked `tools/research/future_story/verify_guide.py` rechecks actor load identities and optional exit definitions against the installed resources. It does not control the game.

## Validation

Regression checks cover starting manual guidance and autowalk before discovery; active offscreen actor movement and retirement; a closed passage becoming usable; alternative floors; optional NPC/object categorization and disabled interactions; connected world areas; treasure collection; scene coherence; and the existing navigation and footstep tests. The full Release suite passed 1,141 tests: Core 128, Native 537, Mod 468, Prism 8. The installed-resource verifier passed 54 optional actor identities and 44 exits. Package and deployment evidence is saved with the research reports. Live playthrough of this update remains unverified.
