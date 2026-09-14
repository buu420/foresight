using ChronoTriggerAccessibility.Core.Menus;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Menus;

public sealed class MenuContentNarratorTests
{
    private static readonly MenuOwner Inventory = new("field-submenu", 1), Save = new("save-slots", 2);
    [Fact] public void ReadsExactSelectionWithoutInventingAPositionAndDeduplicatesUpdates()
    {
        var narrator = new MenuNarrator();
        var start = narrator.Apply(new MenuContentPresented(Inventory, "Inventory", "Potion x3. Restores HP."));
        Assert.Equal(["Inventory.", "Potion x3. Restores HP."], start.Select(a => a.Text));
        Assert.Empty(narrator.Apply(new MenuContentChanged(Inventory, "Potion x3. Restores HP.")));
        Assert.Single(narrator.Apply(new MenuContentChanged(Inventory, "Potion x2. Restores HP.")));
    }
    [Fact] public void ParentOrStaleSubmenuEventsCannotOverwriteTheNewMenu()
    {
        var narrator = new MenuNarrator();
        narrator.Apply(new MenuContentPresented(Inventory, "Inventory", "Potion"));
        narrator.Apply(new MenuContentPresented(Save, "Save", "File 2. Empty."));
        Assert.Empty(narrator.Apply(new MenuExited(Inventory)));
        Assert.Empty(narrator.Apply(new MenuContentChanged(Inventory, "Stale item")));
        Assert.Single(narrator.Apply(new MenuContentChanged(Save, "File 3. Empty.")));
    }
}
