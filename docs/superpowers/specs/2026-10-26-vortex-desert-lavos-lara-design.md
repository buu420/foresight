# Vortex routes, Desert exits and visible battle/password cues

The user approved all four areas after this proposal: repair verified vortex passages and missing interactables in both navigation modes; fix the Sunken Desert exit route; announce Lavos's currently visible boss imitation without giving battle answers; and read native password-selection controls and feedback in Lucca's past. Native locks, timers, combat and player Confirm remain authoritative. The approval was “Yes, cover all four areas.”

Use the existing native capture, Prism announcement and navigation pipeline. Reuse verified script identities and current captured state. Unknown visual owners or connections must withhold a route or cue with a useful status. No hidden identities, puzzle solutions before visible discovery, automatic passwords, Confirm, battle inputs, timer changes or state writes. Normal navigation uses the already authorized movement controller.

All three supplied logs identify0.3.77; compare against current0.3.78 before attributing failures to current code. The vortex log visits598/599/600/644/643. The Desert has30 blocked walks, mostly with exit:0 still selected. Its graph/steering correction currently supplies lateral floor correction only for chest targets. Original x86 movement in an offline emulator reproduces the logged blocked steps and allows appropriate diagonal corrections. Extend route steering without giving exits the chest arrival/hold policy.

Lavos and the password screen need renderer/state ownership evidence before implementation. Speak displayed identities, selected controls/characters, prompt and visible results. Read localization and current input glyphs where native presentation provides them. Do not infer a boss name from secret AI state or silently supply the password.

The existing native/state-backed approach fits the approved behavior. OCR and a replacement password UI add timing and ownership uncertainty; unrestricted fallback passages or direct progression writes would change native play. Root owns implementation inline in the canonical source checkout, retaining ignored native fixtures and the existing deployment boundary. Claude Opus5.5max owns bounded read-only research; a fresh whole-change review precedes final deployment.

Deliver a locally deployed, versioned tester ZIP and focused checklist. Public publication requires a release request. Preserve saves, settings, narration, other mods and historical catalog data. Separate offline/native-emulation evidence from live controller, audible speech and recipient-PC testing.
