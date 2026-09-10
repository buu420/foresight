# Name confirmation prompt window and choices

The 0.2.5 player trace establishes the root cause of the Accept failure: the
confirmation builds three CustomButtons under two managers. The first belongs
to a shared prompt window; the remaining two are the Yes/No choices. Earlier
audits mistook all scoped constructors for choices and the prompt-rendering loop
for a loop over the name's characters.

## Live evidence

Log: `C:\Users\buu42\AppData\Roaming\Reloaded-Mod-Loader-II\Logs\2026-09-10 16.56.06 ~ Chrono Trigger.txt`.
The trace at local 11:56:27 contains 20 entries, all on managed thread 12 with
matching scope, active epoch 1, and no errors or dropped entries.

| Role | Control | Manager and key | Captured rendered text |
| --- | --- | --- | --- |
| Prompt window | `0x29C4CB80` | `0x313C7048`, key 0, focus 0 | `Is "Crono" correct?` |
| First choice | `0x29C4A0B0` | `0x313C5908`, key 0 | `Yes` |
| Second choice | `0x29C4BA60` | `0x313C5908`, key 1, focus 1 | `No` |

The old temporal label correlation associated the prompt with the first control.
Native inspection shows that prompt labels are children of a separate layout
node, so they must not be modeled as labels on a choice button. Final counts:
3 controls, 3 labels, 3 bindings, 2 focus calls, 0 pending labels, 0 capture errors.
Nothing was missing from that sequence.

This run's image base is `0xD20000`: confirmation hook `0x00FE2F00`, RVA
`0x2C2F00`. Claude's research note carried a previous run's image base in its
header; the RVA analysis and the live control addresses above are verified.

## Native construction and text

Supported executable SHA-256:
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Claude owned the Ghidra investigation of the shared prompt factory and splitter.
Codex independently checked call bytes, delimiter, token, and initial focus
against the installed PE file.

- Builder `0x2C2F00..0x2C3974` calls shared window factory `0x23D520` at
  `0x2C303F`. That reaches `0x23D3D0` and constructs the first CustomButton at
  `0x23D49B`. It binds one control at key 0 and focuses key 0 through
  `0x23D638` and `0x23D641`, before the prompt lookup.
- Prompt and choice controls share their native type and allocation size. Type
  alone cannot identify roles. The first manager has exactly key 0; the separate
  choice manager has exactly keys 0/1 and initial focus 1.
- Direct Ope lookup `(0x23,0xDA)` occurs at `0x2C309A`. Helper `0xFC80`, called
  at `0x2C3146`, splits the template on byte `0x5C` (backslash). Each segment
  replaces only its first `<NAME>` occurrence, then renders one font-12 label at
  `0x2C3235`, in top-to-bottom order. This is a prompt-line loop.
- Each choice constructs its control, resolves localized text through
  TextManager::getMsg, and renders its label at `0x2C36F5`. Choice bindings and
  initial focus occur at `0x2C37F8` and `0x2C3810`.

## Capture contract in 0.2.6

Require the first control's exact one-key manager and authoritative focus 0.
Collect rendered prompt lines after the localized lookup and before the next
constructor, without requiring a pending control for each line. Require two
further controls with individually correlated rendered labels, a separate manager
with exactly keys 0/1, and initial focus 1. All three bindings must account for
the three constructed controls, with no shared control between the managers.
Only the choice manager becomes the active confirmation manager.

Join rendered prompt lines in observed order. Do not repeat the game's placeholder
replacement or apply the choices' text-processing rules to the prompt. Blank
total prompts, unreadable text, invalid label returns, missing choices, extra
bindings, and invalid focus continue to fail explicitly.

Research directory:
`C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-intro`.
Artifacts: `live-confirmation-v025-trace.txt`, `prompt-window-review-claude.md`,
`prompt-owner-claude.txt`, `prompt-owner2-claude.txt`, `split2-claude.txt`,
`v026-native-contract-verification.json`. Non-English runtime testing and the
windows' destruction/reuse paths remain unverified.
