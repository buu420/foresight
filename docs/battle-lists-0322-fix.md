# Battle Item and Tech capture, 0.3.22

The silent Item screen in the player's September 14 battle was an empty list. The native reader
required a positive count and returned no focus for zero, so entering the empty panel produced no
announcement. The fix reports the native Item label and "Empty" for this state. K can repeat it;
ordinary Active-mode refreshes do not repeat it automatically.

## Evidence

Supported executable SHA-256:
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

The read-only recorder captured six battle states from PID 52544, actual image base `0x009B0000`,
battle menu `0x271EA540`. It queried memory without input, suspension, save changes or memory writes.
Three minimal fixtures are embedded in `battle-native-0322.json`:

- `live-20260914-185401-700335.json`: Item mode and menu mirror both 2, phase 0, acting slot 0,
  cursor/page/count all zero. Before the fix the complete capture returned valid party data but no
  focus. It now returns `Crono: Item. Empty.`
- `live-20260914-185408-280820.json`: Tech mode and mirror both 1, phase 0, count 2. Both old and new
  readers return `Crono: Cyclone, 2 MP`.
- `live-20260914-185409-353003.json`: Cyclone target selection, highlighted slots 5, 6 and 9.

All required reads succeeded in replay. The log `2026-09-14 23.53.35 ~ Chrono Trigger.txt` independently
confirms the blank Item focus, Cyclone name/cost at 18:54:08, its description through the existing
battle-message reader, and its three targets at 18:54:09. The Potion was obtained after this battle
at 18:54:39; its later field use is a separate issue.

## Native contract and limits

Ghidra output in `artifacts/research/battle-0317/renderers2.txt` proves the classic Item renderer,
RVA `0x20B50`: an acting-slot sentinel closes the panel, a nonzero phase hides the list, and its row
loop draws six cells. Hash-verified disassembly of RVA `0x1EA70` proves that each row is blank if its
quantity or encoded id is zero (checks at RVAs `0x1EAB3` and `0x1EACE`). The count also bounds native
cursor movement (RVA `0x1D9BE`).

The empty announcement requires matching panel modes, phase zero, a valid acting member, cursor and
page zero, count zero, and six blank rendered cells. The count and cell bytes are rechecked. A stale
row, page, invalid count, or a closed list does not become an "Empty" announcement. Populated Item
rows and Tech capture retain their existing behavior.

Claude independently verified the list offsets. Its initial timing hypotheses were not confirmed
by these captures. The mode/coherence guards remain: the classic update mirrors the mode before
calling the post-HUD hook, and these observed panels have matching modes throughout capture.

The real empty-list replay and a session speech test failed before the repair. Regression coverage
also checks repeat/deduplication, stale rendered rows, closed/unsettled panels, count changes during
capture, and the observed Tech/target data. This replay is not an in-game test of the new DLL.
