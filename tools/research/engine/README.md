# Full game decompilation tools

These tools export the installed PC game's complete analyzed native programs and resource archive for local accessibility research. They do not patch the game, alter saves, or produce the original developer source. The current corpus is under `artifacts/research/engine-0332`; its `README.md` and `verification.json` record the actual result.

Ghidra 12.1.2 and Python 3 were used. The main executable is identified by SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`. Every library has its own hash in `libraries/native-libraries.json`. Keep the extracted proprietary files and decompiler output local; only the research tools and documentation belong in Git.

## Export and recovery

1. Import the executable into a separate Ghidra project, or copy an existing project while no writer is using it. Run full automatic analysis, followed by `ExportGameEngine.java <output> [expected SHA-256]`. Use `-analysisTimeoutPerFile 1800`; omit `-noanalysis` on the initial export. Ghidra's function decompilation timeout is 60 seconds per function. Export failures are retained explicitly.
2. Run `Export-NativeLibraries.ps1` with `-GameRoot`, `-OutputRoot` (the corpus `libraries` folder), `-ProjectRoot`, and `-GhidraHeadless`. It inventories the top-level DLLs, analyzes each in a separate project, and exports them. The September 26 installed game has 12 such libraries. Do not include unrelated mod-loader DLLs in a future corpus without identifying them separately.
3. On idle projects, run `RetryDecompilation.java <module output>` with `-noanalysis -readOnly`. It retries only failed functions with 300-second limits and documented decompiler simplification profiles. It preserves initial failures and an append-only attempt log. A successful result replaces only that function's C and rebuilds the combined C in order. It does not change native bytes.
4. The main executable's function at RVA `16E7A0` needs `RepairAuditedSwitch.java <native output>` before retrying. Run this on a separate writable research project. It checks all seven original table entries at RVA `16E904` and annotates the two verified indirect jump sites. No binary patch is made.
5. To find functions missed by automatic discovery, run `RecoverFunctionGaps.java <recovered output> [expected SHA-256]` on a separate project copy. `Recover-NativeLibraryGaps.ps1` performs this for all inventoried libraries, creating separate copies under the library project root's `recovered` folder. The candidates come from existing isolated instruction flows; explicit references and unreferenced candidates are distinguished in `candidates.tsv`. Padding and branches belonging to existing functions are excluded. These generated boundaries are analysis candidates, not recovered original function declarations.

Each primary module export contains `game.c`, individual `functions/<RVA>.c`, `game.asm`, `functions.tsv`, `calls.tsv`, `symbols.tsv`, `defined-data.tsv`, memory blocks, unassigned instructions, undefined executable ranges, failures, and a manifest. Assembly covers all Ghidra-defined instructions, including instructions outside known function boundaries. Unclassified executable ranges may contain code, data, or alignment; the full original binaries must also be preserved under `binaries` so no bytes are lost.

Do not run writers against a project that another Ghidra process is using. A decompiler failure is not a reason to alter game bytes or pretend a guessed translation is verified C. The project copies used for the current corpus are under `C:\Users\buu42\Documents\ct-engine-0332`.

## Resource scripts

From the repository root:

```powershell
py -3 tools/research/engine/export_resources.py --game-root 'X:\SteamLibrary\steamapps\common\Chrono Trigger' --output artifacts/research/engine-0332/resources
py -3 tools/research/engine/export_world_scripts.py --raw-root artifacts/research/engine-0332/resources/raw --decoder-source '<CTViewer checkout>\src\world_script\world_script_decoder.rs' --output artifacts/research/engine-0332/resources/world-scripts
```

The resource exporter reuses the existing checked ARC1 extractor and native-audited field decoder in `tools/research/future_story`. It extracts every archive entry with a per-file hash. Field listings follow every declared actor function and startup continuation, retain raw operands, and print any bytes outside those flows without inventing instructions.

World listings use the explicit operand reads from [CTViewer](https://github.com/GitExl/CTViewer), recording the exact decoder source hash. For this PC build, opcode `53` has **two operand bytes**, verified in native dispatcher RVA `268510` and handler `26F030`. The community decoder reads three and loses synchronization in `Event_0006`; the native correction allows all eight world files to decode to their ends. Keep the raw archive files authoritative.

## Verify and search

After all exports and recoveries finish:

```powershell
py -3 tools/research/engine/index_engine.py artifacts/research/engine-0332
py -3 tools/research/engine/query_engine.py --root artifacts/research/engine-0332 --rva 16E7A0
py -3 tools/research/engine/query_engine.py --root artifacts/research/engine-0332 --search 'ClassicMenuNodeTech' --limit 8
py -3 tools/research/engine/query_engine.py --root artifacts/research/engine-0332 --module libcocos2d.dll --search 'AutoPolygon' --limit 8
```

The index builder checks preserved binary hashes, every function file and combined C membership, instruction counts, recovered candidates, every extracted resource's size and hash, and script listing counts. It creates `engine.sqlite` with full-text search over native C, symbols, defined data, field/world scripts, and English text, plus separate function and call tables. `verification.json` reports actual successes and failures; `verified` means the export passed those consistency checks, not that all inferred C semantics or accessibility behavior were tested in play.

The query tool opens the native path with SQLite's `query_only` enabled so UNC paths work on Windows. `--module` chooses the module for an RVA lookup; full-text queries search the entire corpus. Search assembly directly with `rg` in the relevant module's `game.asm` when an address falls inside a function rather than at its entry.

Ghidra's [decompiler API](https://ghidra.re/ghidra_docs/api/ghidra/app/decompiler/DecompInterface.html) and the source/API shipped with the installed version are the references for export and recovery. Compiler types, indirect calls, variable names, and candidate function boundaries still require native evidence before being used in the mod.
