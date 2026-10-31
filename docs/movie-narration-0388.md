# Native film descriptions in 0.3.88

The existing exact-hash narration pipeline now covers 005.dat and 008.dat, using
eight and nineteen brief visual descriptions respectively. Scripts and placement
windows are in movie-narration-0388.json. Appearance labels preserve customized
character names and avoid adding unseen motives or backstory. No original film,
music or effect recording is replaced or distributed.

Both films were decoded privately with the audited native XOR transform and
reviewed as sequential timestamped local frames. Descript imported the selected
films into a private scratch project, but Agent Underlord's rendered images were
unavailable or black. Its responses do not provide visual verification. Local
frames supply the visual evidence for these cues.

The existing approved Fish model generated the narration through the documented
[Fish TTS API](https://docs.fish.audio/api-reference/openapi.json). The key and
voice-model ID stay private. Every existing voice recording and cue is retained.
New masters are mono, 24 kHz, 16-bit PCM: 913711 samples for 005 and 3837432 for
008. Text transcription, non-silence, clipping, cue windows and sample-exact
placement checks apply to all 27 clips. Those checks are not audible listening.

| Film | Decoded original SHA-256 | Bytes | Seconds |
|---|---|---:|---:|
| 005 | 8C78387E529CDBE1FB5BFC65374A4ECFC2A6891B0986D984C2A598549D996F5A | 24280981 | 38.0713 |
| 008 | 4DAD7B7E5EE63B776E88A3F862ABBCE20CCF08B0D8267B321BB8B5BECC8A054D | 102062569 | 159.893 |

Ghidra/native evidence: 005 is scheduled at EXE RVA 0x15D0FD during the native
Dactyl Nest transition. The literal 008.dat is selected at 0x297A19 through
SpecialEventScene action 3. The gallery maps row n to 00(n+1).dat. Native 0x2AEDF0
only switches 007 to its localized variant; 005 and 008 each use one original.
Both use libcocos2d 0x287FAA decoding, 0x287578 topology construction, 0x28762D events
and 0x287361 closure. No new hook or event subscriber is added.

Supported EXE SHA-256: 8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7.
Supported libcocos2d SHA-256: 8A53EF5BFFD345D4EEE0E2D91152C539FCEB71725DB44A72D2E7513DF55ECD2F.
Native locks, film timing, skipping and media ownership remain unchanged. The
opening 001 keeps its existing Fish timeline. This is specific film coverage,
not an end-to-end review of all dialogue and story animations.
