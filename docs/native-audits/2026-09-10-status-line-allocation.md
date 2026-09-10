# StatusBar line allocation, 0.2.8

The player reported the remaining top-menu missing-label-marker error on 0.2.7.
The log `2026-09-10 19.46.19 ~ Chrono Trigger.txt` shows all 120 hooks activated
and one error at 14:47:38. This run selected Resume, so the deliberately
New-Game-only intro recorder did not arm.

## Native evidence

Claude owned the direct-call reachability audit from the classic top-menu builder
at RVA `0x1D0560`. Of 22 reachable label-factory call sites in that audit, one
was missing a marker: `0x22F2D7`, in the already hooked StatusBar formatter at
`0x22F160`. Codex independently imported the supported executable in Ghidra,
decompiled the formatter, and checked the instructions with Capstone.

| Property | Verified value |
| --- | --- |
| Call / return RVA | `0x22F2D7` / `0x22F2DC` |
| Exact call bytes | `E8 D4 0D 01 00` |
| Target | UTF-8 label factory `0x2400B0` |
| Input string | Length zero, inline buffer zero, capacity 15 |
| Label position / font | `(10, -10)` relative to its parent; size 12 |
| Allocation condition | Requested line index equals existing label count |
| Actual line content path | Existing glyph-renderer call at `0x22F3B8` |

The source string is explicitly initialized empty at `0x22F290..0x22F2AC`,
then passed in EDX. The returned node is added to its parent and the formatter's
label collection. It later receives content through the existing formatting
path. Empty allocation contributes no text to announce.

This supports the remaining error's cause, but the log does not record this
specific call executing. One error is consistent with one newly allocated line.
The allocation happens when a particular StatusBar needs another label; the audit
does not establish that it can happen only once per game process or that the same
StatusBar survives every menu opening. Claude's broader lifetime inference is
therefore not used by the implementation.

## Implementation boundaries

- Add one exact `StatusBarEmptyLineLabelCallSite` marker, for both menu styles.
- Require an active owned StatusBar formatting scope, a non-null native return,
  readable string storage, and exactly empty text. Whitespace or other unexpected
  text remains an error. This is stricter than Claude's proposed unconditional
  text skip and prevents hiding content if the assumption stops holding.
- Preserve the existing UTF-16 rendered-line observations and output; do not
  append the empty allocation to menu text. Calls without a top-menu builder
  scope pass through normally, as this formatter is used elsewhere in the game.
- Keep the general missing-marker error. Add at most eight diagnostic label
  previews per failed build, each capped at 160 characters and JSON escaped.
  These previews are local log evidence, never spoken labels. Diagnostic failures
  cannot interrupt the native call or suppress its existing coverage error.

Primary hook-behavior reference: [Reloaded.Hooks assembly-hook documentation](https://reloaded-project.github.io/Reloaded.Hooks/AssemblyHooks/).
The existing marker infrastructure runs before the original five-byte call and
preserves native flags and caller-saved registers.

## Reproducible artifacts

Research directory:
`C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-intro`.

- `v028-remaining-label-audit-claude.md`, `v028-label-census-claude.txt`,
  `v028-site-disasm-claude.txt`, and `v028-path-claude.txt`, with their Java scripts.
- `ReviewFieldMenuLabelCodex.java`, `v028-status-format-codex.txt`, and
  `v028-status-format-ghidra.log` independently reproduce the formatter audit.
- `verify_v028_contracts.py` verifies all 121 catalog signatures against the
  installed supported executable; `v028-native-contract-verification.json`
  records the result.

The game SHA-256 remains
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
