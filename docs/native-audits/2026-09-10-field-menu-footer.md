# Field menu missing label calls

The 0.2.6 player log `2026-09-10 17.14.53 ~ Chrono Trigger.txt` records four
unmarked Label calls while opening the field menu at 12:17:20. The hook had
treated every scoped label as one of the previously enumerated row/status fields.

A fresh Ghidra import of the supported executable confirms additional calls in
the classic root builder, RVA `0x1D0560`, after the captured play time and money:

| Call RVA | Return RVA | Native branch |
| --- | --- | --- |
| `0x1D0D73` | `0x1D0D78` | One rendered footer line |
| `0x1D0DB7` | `0x1D0DBC` | First of two rendered footer lines |
| `0x1D0E00` | `0x1D0E05` | Second of two rendered footer lines |
| `0x1D0ECE` | `0x1D0ED3` | Conditional context line following the split text |

All four call the same UTF-8 label factory at `0x2400B0`. The native builder
splits its resolved text on backslash and renders either one line or its first
two lines. A separate conditional branch resolves and renders another line.
The reader now captures the exact rendered strings, without assigning a guessed
semantic category, and preserves the one-line/two-line branch grammar. A missing
second line, mixed branches, duplicate observations, or unexpected ordering still
rejects the snapshot.

The fifth added marker covers `StatusBar` initialization at `0x22EFA1`, return
`0x22EFA6`. Its owner is hidden before this call, and its result is unconditionally
hidden by a virtual `setVisible(false)` call immediately afterward. This is an
internal label and contributes no visible text. The reader accounts for the call
and still verifies a non-null native return; it does not announce its contents.

The alternate initializer's call at `0x22ECE6`, return `0x22ECEB`, can create a
visible initial caption in the touch menu. It is captured before row construction
when nonblank. This particular caption can be empty; unreadable memory remains an
error. Other visible labels retain their nonblank requirements.

No general unmarked-label fallback was added. Six exact five-byte call-site
contracts account for these branches. Native originals still run once, and all
new rendering observations are restricted to the owned builder and thread.

## Evidence

The supported executable SHA-256 is
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Research files under
`C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-intro`:

- `field-menu-label-audit-codex.txt`: both root builders and label factory.
- `field-menu-child-label-audit-codex.txt`: StatusBar initialization, row builder,
  and classic card formatting.
- `field-menu-card-audit-codex.txt`: hidden-label creation and unconditional hide.
- `ReviewFieldMenuLabelCodex.java` and corresponding Ghidra logs reproduce the audit.

The new footer capture tests first failed against the old code with an unknown
return-address error. After the change, the full suite passed 777 tests: Core 56,
Native 431, Prism 8, Mod 282. The player still needs to retest the in-game menu;
the four-call log did not itself record their individual addresses or strings.
