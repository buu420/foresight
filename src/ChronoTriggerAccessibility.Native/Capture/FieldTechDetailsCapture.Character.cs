namespace ChronoTriggerAccessibility.Native.Capture;

public sealed partial class FieldTechDetailsCapture
{
    private sealed record CharacterState(uint Manager, uint Key, uint Category, uint Character,
        uint Roster, uint RosterEnd, uint Icons, uint IconsEnd, uint Control,
        uint Panel, uint Cards, uint CardsEnd, uint Card);

    private FieldSubmenuSnapshot? CaptureCharacter(nuint image, nuint node)
    {
        var state = ReadCharacterState(image, node);
        if (state is null) return null;
        var lines = rendered.Read(state.Card);
        var title = messages.Get(image, 0x23, 0x22);
        // The three icon tabs built at 1CD1D0 represent the game's localized
        // Single/Dual/Triple Tech categories, bank 23 entries 54..56.
        var category = messages.Get(image, 0x23, 0x54 + (int)state.Category);
        var prompt = messages.Get(image, 0x23, 0xB1);
        if (lines is null || lines.Count is 0 or > 32 || string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(prompt) ||
            ReadCharacterState(image, node) != state) return null;
        // Native character cards place colons after groups and split HP/MP
        // fractions across Labels. Preserve the finished card's displayed values.
        var card = RenderedCharacterCardSpeech.Format(lines).TrimEnd('.');
        return new("Tech", title, $"tech:character:{state.Key}:{state.Character}:{state.Category}",
            $"{card}. {category}. {prompt.TrimEnd('.')}");
    }

    private CharacterState? ReadCharacterState(nuint image, nuint node)
    {
        // 1CABE0 builds the roster, 1CB230 builds matching portrait buttons,
        // and 1CBCA0 rebuilds one rendered card per roster entry under +2EC.
        if (!Word(node, out var vt) || vt != image + FieldSubmenuCapture.ClassicTechNodeVtableRva ||
            !Visible(node) || !Word(node + 0x308, out var rowManager) || rowManager != 0 ||
            !Word(node + 0x2E8, out var manager) || !Under(node, manager) ||
            !Word(manager, out var mt) || mt != image + FieldSubmenuCapture.ManagerVtableRva ||
            !Byte(manager + 0x290, out var disabled) || disabled != 0 ||
            !Word(manager + 0x2C4, out var key) || !Word(node + 0x2E4, out var selected) || selected != key ||
            !Word(node + 0x2FC, out var category) || category >= 3 ||
            !Word(node + 0x2C8, out var roster) || roster == 0 ||
            !Word(node + 0x2CC, out var rosterEnd) || rosterEnd <= roster ||
            (rosterEnd - roster) % 4 != 0 || (rosterEnd - roster) / 4 > 7 || key >= (rosterEnd - roster) / 4 ||
            !Word(roster + key * 4, out var character) || character >= 7 ||
            !Word(node + 0x2D4, out var icons) || icons == 0 ||
            !Word(node + 0x2D8, out var iconsEnd) || iconsEnd < icons || iconsEnd - icons != rosterEnd - roster ||
            !Word(icons + key * 4, out var icon) || !Under(node, icon) ||
            !Word(icon, out var iconVt) || iconVt != image + 0x3A4364 ||
            !FindControl(image, manager, key, out var control) || control != icon ||
            !Word(node + 0x2EC, out var panel) || !Under(node, panel) ||
            !Word(panel + 0x160, out var cards) || cards == 0 ||
            !Word(panel + 0x164, out var cardsEnd) || cardsEnd < cards || cardsEnd - cards != rosterEnd - roster ||
            !Word(cards + key * 4, out var card) || !Under(node, card) ||
            !Word(card + 0x16C, out var parent) || parent != panel) return null;
        return new(manager, key, category, character, roster, rosterEnd, icons, iconsEnd, control, panel, cards, cardsEnd, card);
    }
}
