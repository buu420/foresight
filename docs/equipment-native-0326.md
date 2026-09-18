# Version 0.3.26: Equipment replacement list

Opening a weapon, helm, armor or accessory replacement list now follows that list's native
input manager. The reader announces the selected item, its quantity and list position, its
displayed attributes, and the character stat preview. Stat labels and numbers are paired, with increases
and decreases spoken explicitly. The same reader serves Equipment opened from a shop.

The September 18 read-only capture reproduces the reported silence: Bronze Blade is selected
at key zero, while both the character selector and equipped-slot managers are disabled. The
previous reader inspected those two disabled managers and returned no selection. No game
input, memory writes, purchases or equipment changes were performed to capture this state.

## Native evidence

All addresses are RVAs in the supported executable, SHA-256
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
The recorded process used image base `00480000`.

- `205C00` installs the page's CharaEquipManager at `node+2F0`; its vtable is `3A8194`.
- `2088A0` clears and rebuilds the candidate MenuListView at `child+30C`, separate from
  the equipped-slot list at `child+308`. The list owns an input manager at `+280`.
  `2085E0` disables the slot manager and enables this candidate manager at byte `+290`.
- `child+304` identifies the equipment slot. `child+31C/+320` bounds the candidate
  vector: 12-byte records, with full encoded item ID at zero and quantity at four.
  `208000` filters native inventory entries for the character and selected slot;
  `20B0F0` transfers the vector. The third record word is not used by this reader.
- The manager's focus key must match the candidate cursor at `child+35C`, and selects
  the matching record and CustomButton. Its rendered
  name and quantity must agree with that record. Names use the existing loaded
  text bank and encoded-group table at `39906C`.
- `20A260` formats the selected item's attributes and effect text into the StatusBar
  at `child+2F0`. Its visible rendered labels are read separately from the character
  preview: Bronze Blade's item Attack is 7, while Crono's preview Attack is 10.
  The StatusBar's class, ancestry, owner and rendered strings must remain stable.
- The native list is created only for a positive candidate count. An empty vector
  leaves `+30C` null; a retained candidate cursor does not establish an active list.
- `2090F0` creates the ten ParameterLabels in `child+310/+314`. `20A800` writes their
  equipped baselines at `+278`. `20A900` renders a preview without replacing those
  baselines; `2326E0` uses the same original-versus-preview comparison for colors.
  The reader uses rendered numbers and translates their change into speech.
- The vector orders Strength, Accuracy, Speed, Magic, Evasion, Stamina, Magic Defense,
  Attack, Defense, maximum HP. The first nine captions use loaded bank `23` messages
  `33..39`, `31`, `32` (hexadecimal). Attack and Defense are represented by icons on
  this screen. `232790` displays two stars for a positive stat cap; these read as
  the displayed maximum, without exposing an above-cap value.

Visible ancestry, vtables, record bounds, focus, selected slot, and the complete selection
are checked again before speech. Unrelated or changing stat panels cannot suppress the
valid selected item. The old unordered traversal of the entire detail container is removed.
Existing focus/update hooks are reused; no additional hooks or synthesized controls are added.

Claude owned the Ghidra extraction. Codex checked it against the decompilation and recorded
memory, corrected interpretation errors concerning empty lists and baseline values, and
implemented the reader. Source evidence is under `artifacts/research/equipment-0326/`.

## Verification

The committed fixture retains only consumed memory and the fields needed by its mutations,
and replays the user's Bronze Blade candidate list. Regression cases
cover disabled and detached lists, ownership callbacks, quantity disagreement, slot/focus
changes during capture, the increased/decreased preview, maximum-stat markers, cancellation,
and the same Equipment page under synthetic shop ownership. Altered-state cases are tests
of the audited layout, not claims of additional live purchases or equipment changes.

The original saved screen failed before the repair and passes afterward. New-DLL speech
and an actual equip/cancel cycle still require a normal game launch after installation.

Release validation: **1,814 tests passed**, zero failures or skips: Core 145, Native 805,
Mod 856 (including three separately run footstep timing tests), Prism 8. All **151 native
hook signatures** match the supported executable. The fifteen new equipment cases use the
actual read-only capture or explicitly modified copies of its native layout.

## Installed build

Installed September 18, 2026 after the game had closed. All **25 deployment checks passed**.
The installed file sets and hashes match the reviewed package: **28 mod files, 45 loader/shared
hook files, and two native launcher/installer files**. The supported game executable is unchanged.

Package code commit: `c2ae6f179fbdecea7b3849fd4b1925dc7742f7c7`. The final Release build had zero warnings or errors.

DLL SHA-256: `23F872466CDFC1994AFDB65781382BA229C16C8EDF509D41188B379FF61D7096`.

Manifest SHA-256: `B5D79623055547324577599841C3DED7801E949C6C4A185A40EB7BB4A7DAA80C`.

Previous installation: `X:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20260918-123202`.

Launch normally and reopen the equipment replacement list to check speech with the installed DLL.
