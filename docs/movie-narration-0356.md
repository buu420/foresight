# Movie narration in 0.3.56

The embedded narration covers movie 002 (Ayla's rescue), 003 (Frog opening the
Magic Cave), and 004 (the approach to Magus). The first two use the previously
reviewed ViddyScribe descriptions; 004 was reviewed from the selected native
film in a Descript scratch composition and checked against local frames.
Fish generated all three masters using the approved narrator voice. The exact
scripts and start times are in [movie-narration-0356.json](movie-narration-0356.json).
The opening and all 146 existing opening/story clips are unchanged.

Each narration master is mono, 24 kHz, 16-bit PCM. It includes silence between
cues and ends no later than its original film. The resource manifest pins its
size, digest, rate and sample count. The decoded originals are also identified
by full SHA-256:

| Movie | Decoded original SHA-256 |
|---|---|
| 002 | BA85684DC8586665C0A78B0AA7E8B704622ECA0A4993076AD319DFE84FDB1254 |
| 003 | 8F0FB77315D033D9B4C48831D4BE89CD64AA2A935D540E967810CA8BE32A58AF |
| 004 | 64734088FA2480B7559FF18C109D8D6A9B71662707A3A0AA3643B1F8F6854451 |

No game movie is shipped or replaced. An aggregate Media Foundation source
combines a second instance of the verified decoded film with the narration
WAVE source. The game's topology builder uses this aggregate in its own media
session, keeping the original music, effects and video. The player's original
source stays untouched for synchronous fallback: aggregating that original
source would subscribe to its events and prevent a usable rollback.
Microsoft documents the aggregate's role in combining separate media sources
in [MFCreateAggregateSource](https://learn.microsoft.com/es-es/windows/desktop/api/mfidl/nf-mfidl-mfcreateaggregatesource).

Existing verified separate narration tracks take precedence, so they are not
doubled. Movie 001 retains its previous opening timeline/readiness contract.
Movies 005–008 receive no descriptions. The new tracks are audio only and have
no in-game braille transcript.

The separately audited `libcocos2d.dll` has SHA-256
`8A53EF5BFFD345D4EEE0E2D91152C539FCEB71725DB44A72D2E7513DF55ECD2F`.
Entry bytes and Ghidra exports verify these functions:

| RVA | Native role |
|---|---|
| 0x287578 | Build the movie topology |
| 0x287361 | Close the player's media session and sources |
| 0x28762D | Handle a media event; thiscall, one argument, RET 4 |

The close hook activates first. Narration owns its aggregate sources until
the corresponding native player's CloseSession returns; unrelated closes do
not end another movie. Story narration clears held actions and cancels its
recording at movie boundaries, and suppresses field descriptions while a known
movie is active. Observer exceptions do not escape into native game code.

The native event handler releases each event and ignores failed event status.
The failure observer reads the event before forwarding it to the original
exactly once, preserving its return value and ownership. It never consumes the
session event queue or registers a second subscriber. A failure is announced
once per player session; native skip is required after an asynchronous failure.
Synchronous injection failure falls back to the original film and reports
unavailable descriptions. No safe asynchronous reopen was established.

Silent x86 probes used the actual library, production detours and real embedded
masters. They checked all three originals, six-node topology, pause/resume,
natural completion, skip, own-player cleanup, existing separate tracks and
opening compatibility. Constructed real Media Foundation failure events also
passed through the production handler with native results preserved and one
announcement. Those constructed events are not a naturally occurring pipeline
failure. No game session or audible listening test was performed.

The private generation artifacts, original reference recording, Fish model ID,
API key, upload URLs, decoded game films and tester logs are excluded from the
source release and public packages.
