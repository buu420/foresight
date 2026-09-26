# Full game decompilation reference — September 26, 2026

The local reference covers the complete analyzed executable, all 12 native libraries present in the installed game, the complete resource archive, all field event files, and all eight world scripts. This is the foundation for researching engine behavior across the game. It is not a claim that every accessibility surface or route has been implemented or tested.

Open the generated reference at:

`X:\SteamLibrary\steamapps\common\Chrono Trigger\accessibility-mod\artifacts\research\engine-0332\README.md`

The same folder is available through the workspace's UNC path under `\\desktop-pem349m\g\SteamLibrary\steamapps\common\Chrono Trigger\accessibility-mod`. It contains `verification.json` and the searchable `engine.sqlite`. Reproduction commands and recovery details are in [the research tools documentation](../tools/research/engine/README.md).

## Coverage

| Component | Export |
| --- | --- |
| Main executable | 16,975 original analyzed functions plus 1,362 recovered function candidates; all produced C |
| Native libraries | 70,660 original analyzed functions plus 3,876 recovered candidates; nine original Cocos functions remain assembly only |
| Combined native reference | 92,873 entries; 92,864 produced C |
| Resource archive | All 9,509 entries, 472,465,191 decoded bytes, each with its original path and SHA-256 |
| Field events | All 648 files: 645 decoded actor-script files and three stubs |
| World events | All eight files decode through their final byte |
| Search | Native C, symbols, defined data, field/world scripts, English text, function lookups and call relationships |

Original executable and library bytes are preserved in the corpus's `binaries` directory. Assembly includes instructions outside known functions. Separate indexes retain executable bytes not classified as instructions or defined data; these can include alignment, data, and undiscovered code. The 5,238 recovered entries are explicitly labeled as Ghidra-generated function candidates.

## Limits and evidence

Ghidra output is inferred C, not the original source tree, and is not directly rebuildable as the game. Names, signatures, indirect-call relationships, and generated boundaries still require native evidence before being used in an accessibility hook.

The only remaining C decompiler failures are in `libcocos2d.dll`, at RVAs `170D0`, `174E0`, `17CC0`, `18010`, `18710`, `18C10`, `193D0`, `19740`, and `29C6BF`. Their instruction bytes and assembly are present. `decompile-failures.tsv` and `recovery-attempts.tsv` record the actual Ghidra errors and unsuccessful alternate profiles. The first eight encounter a variable-hash failure; `AutoPolygon::expand` encounters a forced variable-merge failure. Nothing substitutes guessed C for these functions.

The executable's last failed function, RVA `16E7A0`, was recovered by annotating two native jump-table sites after checking the original table bytes. The research project annotations are reproducible with `RepairAuditedSwitch.java`; the executable itself was not patched.

Field actor entry flows decode 472,159 of 474,893 code bytes. The remaining 2,734 bytes are printed as uninterpreted hex in their listings and retained in the raw files. The three actor-table stubs are `Atel_0320.dat`, `Atel_0321.dat`, and `Atel_0322.dat`. World opcode `53` was corrected to two operand bytes from native dispatch and handler code, resolving a community decoder mismatch in the seventh world file.

`verification.json` reports export consistency: module identities, file membership, counts, resource hashes, and script coverage. It does not certify full game accessibility or runtime correctness. The installed mod remains version 0.3.31; this decompilation pass itself requires no game-file deployment or restart.

Claude Opus 5.5 at maximum effort also examined engine UI ownership in this session. Its menu audit and proposed hook work are separate from the decompilation corpus and are not represented here as installed or verified runtime support.
