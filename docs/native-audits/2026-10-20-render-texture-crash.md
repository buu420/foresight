# Render-texture crash evidence and diagnostic tester build

The six supplied October 20 dumps identify one immediate native failure. They
do not yet establish why the allocation fails. Foresight 0.3.70 observes that
failure for the next tester report; it is not a crash fix.

## Images and exact callers

Audited local EXE SHA256:
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Audited local `libcocos2d.dll` SHA256:
`8A53EF5BFFD345D4EEE0E2D91152C539FCEB71725DB44A72D2E7513DF55ECD2F`.
Remote dump timestamp, image size and PDB identities match the audited images;
those metadata matches are not a remote whole-file hash comparison.

All six dumps have an access violation writing address 4 at Cocos RVA
`1A0452`, `cocos2d::Ref::retain`, instruction `INC [ECX+4]`, with ECX zero.
The immediate raw return word and corresponding native call instruction
identify the caller even though the optimized frame-pointer stack omits it.

| Dump PID | UTC on October 20 | EXE return RVA |
|---|---|---|
| 26804 | 16:50:22 | 188281 |
| 23124 | 19:13:12 | 187D52 |
| 26708 | 19:44:05 | 188081 |
| 21500 | 19:54:50 | 187D52 |
| 21748 | 20:02:53 | 188281 |
| 27068 | 20:12:32 | 187D52 |

These are three render-layer branches in EXE routine RVA `1879E0`. Each calls
the two-argument CDECL `RenderTexture::create(int,int)` at Cocos RVA `2CC31E`,
stores its result in engine offsets `33C`, `340` or `344`, then immediately
retains it. The factory explicitly returns null if object allocation or
initialization fails. The game does not check that null before using it.
Skipping retain alone would leave subsequent null dereferences.

Ghidra's audited initializer at RVA `2CC592` multiplies each requested integer
dimension by the Director's single-precision content scale, truncates to an
integer, and optionally rounds dimensions up to powers of two. It allocates
width times height times four bytes, then Texture2D objects. Verified false
paths include unsuccessful allocations. The dumps omit the native heap and
required engine fields, so oversized dimensions, memory fragmentation and
other allocation causes are still unresolved. Presence of graphics or overlay
modules is not causal evidence. Earlier jump-to-zero/CoreCLR signatures have
not been explained by these six dumps.

## Narrow observation

The new optional hook forwards the original CDECL call exactly once, returning
its pointer unchanged. Successful creation does not scan memory or log a
request. After a null result it records:

- Exact width and height passed to the native factory.
- Existing Director pointer at library RVA `87D47C`, then float at offset `E0`;
  raw float bits are retained even when invalid.
- Existing Configuration pointer at RVA `87CBFC`, then NPOT flag at offset `24`.
  The exported `supportsNPOT` getter shares address `16BED2` with other aliases.
- Derived scaled/padded dimensions, estimated four-byte pixel allocation and
  native 32-bit byte-count wrap, with absent or invalid data explicitly marked.
- Independently available process/system counters and a bounded VirtualQuery
  summary. An incomplete address-space scan must not claim a complete summary.

Scalar reads occur after the native failure and allocation sizes are estimates,
not intercepted allocator arguments. No singleton getter, graphics error query,
heap write, executable patch, layer bypass or game-state change is made.

The hook requires the audited DLL hash, x86 process and relocation-correct entry
bytes. It participates in the existing installer rooting and rollback. An
unavailable diagnostic feature leaves the accessibility readers available;
failed detour cleanup must propagate so the callback remains rooted.

Failure records are synchronously appended/flushed to
`Accessibility/Logs/render-texture.log` and copied to the Reloaded gameplay log.
Each run has a distinct session ID. A new launch appends a readiness record
without replacing earlier failure evidence. Diagnostic exceptions must not
replace the original result. The crash collector includes this file.

## Evidence and limits

Private artifacts: `artifacts/research/crash-dumps-20261020/`, containing input
hashes, CDB contexts, read-only Ghidra exports and documented minidump counters.
The counters do not prove global commit exhaustion and do not expose the
largest available native heap block. VirtualQuery's largest free address range
also is not a guarantee that a CRT allocation of that size can succeed.

Windows API contracts were checked against Microsoft's
[ReadProcessMemory documentation](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-readprocessmemory),
[VirtualQuery documentation](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualquery),
and [FlushFileBuffers documentation](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers).
Offline verification and the local tester package do not reproduce the user's
gameplay crash. A new matching gameplay report is needed to determine the
allocation failure cause.

Verification before packaging: 4,399 automated tests and 18 collector checks
passed; the Release build has zero warnings or errors. An isolated x86/private
.NET 9.0.20 probe verified installation and removal of the audited library hook,
100,001 unchanged successful synthetic CDECL calls without capture, and one
unchanged synthetic null return with one failure record. The real factory was
not invoked. Native x86 memory queries returned complete counters and the
expected 28/44/64/36-byte native layouts. This is ABI/API evidence, not a live
gameplay or crash-reproduction test.
