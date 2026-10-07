# Site 32 bike race: implementation evidence, October 4, 2026

Foresight 0.3.43 implements the race reader described below. A full live race test remains outstanding; generated memory fixtures and native-code audits do not establish in-game usability.

## Report and log

The supplied October 4 Chrono Trigger log has SHA-256 `812A4B68C5B11982A465EF1B5AA8F2F677D0F086D58D3D809C35965B18DD0B90`. It contains Johnny's introduction and the riding-instructions question. The player selects Yes, meaning they say they already know how to ride. At 16:50:08 field navigation stops as controller input becomes unavailable. No race status is announced before loss dialogue resumes at 16:50:55. This supports missing race coverage; the log does not contain the race's underlying positions or input samples.

Version 0.3.42 handled field/world movement and ordinary menus, but had no dedicated `SceneSpecialRace` observer. The race is a separate special-event scene, so adding more field-navigation directions cannot by itself provide race feedback.

The previous Story Events advice promised an on-foot crossing. In this log, following that objective crosses a trigger and starts a race instead. The updated hint warns about the race and introduces its controls. No new walking bypass is claimed.

## Controls and visible information

The installed PC controller-text resource `Localize/en/msg/cmes1_pad.txt`, message `FLD_CMES1_014`, explicitly describes automatic acceleration, up/down steering, the normal Dash action for boosting, three boost uses, and a gauge that must refill between boosts. It also describes a visible finish-position display. The alternate touch-text resource has different button instructions and must not be used for keyboard/controller labels.

The installed messages also describe two race modes: one with boosts and one without boosts but with shoulder-button camera rotation. Instructions name the configured Dash action, rather than assume a fixed keyboard or console button. The mod does not change that binding.

The independently authored [GCGX bike-race analysis](https://gcgx.games/ct/bike.html) corroborates automatic acceleration and up/down positioning. It explains that being ahead at the finish wins, and describes blocking and collisions as scoring mechanics. These gameplay observations inform cue design; PC offsets and control mappings must come from the installed game.

## Ghidra evidence

References are from the existing complete export under `artifacts/research/engine-0332`, for executable SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`. Addresses below are RVAs, not runtime pointers.

- `SceneSpecialRace` vtable at `3B49C8`, with matching RTTI; the class owns the dedicated race surface.
- `2EAFF0`: initializes race assets and special-event resources.
- `2EBB90`: creates touch boost and other race interface nodes. The presence of the touch controls is conditional.
- `2EC620`: race update collects input, invokes `2ED720`, and advances the shared special-event logic at `2D89D0`. Field input hooks are not a substitute for this update path.
- `2ED720`: transfers special-event state into race render fields. Bike screen positions are populated at scene offsets `4E44/4E48` and `4E78/4E7C`; these are projected display coordinates and must not automatically be treated as control-space coordinates when the camera changes.
- `2ED720` and `2EDE60`: the former computes remaining distance at `4F04`, rendered in case 11, and nonnegative score at `4EB8`, rendered in case 12. These are sampled as displayed integers; distance is not renamed meters or tiles.
- `2EAF60` and `1AF420`: `4EFC` is boost count (0–3), `4F00` recharge (0–128). Boost sets recharge to 128. A filled indicator is shown only with boosts remaining and recharge zero. Scene `C10 == 0` enables boosts; mode 1 hides them.
- `2D89D0`/`2D8DB0`: scene `C04 == 1` means initialization cleared the race VM state. The current engine pointer must match scene `+4` and global `EXE+41B4BC`.
- `1AEE10`: the main race loop increments `ushort D+2E0BC`; the reader uses this to announce Go. No numeric countdown was verified, so none is synthesized.
- `1AE7F0`/`1AEE10`: result low byte `D+2E380` is 1 for player victory, 2 for loss. The high byte contains award flags. The loss path does not immediately set `D+2E0B0`; using only that flag would miss losses.
- `1AAC90`/`1AB880`: pause byte `D+2E392` suspends the VM loop. Paused races stop position cues.
- `1B47C0`/`1B4850`/`1B4920`: track-overview Y increases with lateral position. Johnny `ushort D+2E073` below player `D+2E04E` means above. Native assembly `1B0BDE..1B0D69` uses absolute lateral difference below `0x1000` as the overlap band; exact boundaries are outside. Tone alignment follows that band, independent of camera rotation.
- `2EC620` takes only ECX and returns AL; `2EC450` is a void non-deleting destructor. Neither has the generic cocos update/destructor signature. Exact prologue bytes are registered in `GameVersionCatalog` and verified before hooking.

## Feedback and checks

The user approved speech plus short position tones. The dedicated observer announces instructions, Go, stable lead changes, boosts/readiness, displayed-distance milestones and native results. High/low/middle tones mean Johnny above/below/aligned. Tones last 70 ms and update at most every 350 ms. Ordinary speech updates coalesce over 1.6 seconds; lead changes must remain stable for 600 ms. K or R3 requests fresh status. All native steering, boost and pause inputs pass through unchanged. Field navigation is suspended when the race opens.

The tone worker has a one-request queue, expiry and cancellation checks, with its own waveOut voice. Capture errors are retried and announced; audio-device failures are announced. Phase, HUD and lane diagnostics are bounded to one entry per second. A real race is still required to verify cue timing, tone balance, whether the player can finish, and return to ordinary navigation. Both boost/no-boost modes and camera rotation need live testing.
