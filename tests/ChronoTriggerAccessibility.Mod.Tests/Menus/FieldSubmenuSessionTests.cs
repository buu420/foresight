using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Mod.Menus;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Menus;

public sealed class FieldSubmenuSessionTests
{
    [Fact] public void SelectionUpdatesAndQuantityChangesSpeakOnceAndOldOwnerUpdatesAreIgnored()
    {
        var events = new List<AccessibilityEvent>();
        FieldSubmenuSnapshot? snapshot = new("Inventory", "Inventory", "potion", "Potion x3");
        var s = new FieldSubmenuSession(_ => snapshot, _ => "Inventory", _ => false, () => true, events.Add, _ => { });
        s.Enter(1); s.Refresh();
        Assert.Single(events);
        snapshot = snapshot with { Text = "Potion x2" };
        s.Refresh(); Assert.Equal(2, events.Count);
        Assert.Equal("Potion x2", Assert.IsType<MenuContentChanged>(events[1]).Text);
        s.Enter(2); events.Clear(); s.Refresh(1); s.Close(1);
        Assert.Empty(events); Assert.True(s.HasContext);
    }
    [Fact] public void SaveConfirmationCannotBeInterruptedBySlotUpdatesAndReturnsToFileSpeech()
    {
        var events = new List<AccessibilityEvent>(); var confirmation = false;
        var s = new FieldSubmenuSession(_ => new("SaveSlots", "Save", "file:1", "File 1. Empty"),
            _ => "Save", _ => confirmation, () => true, events.Add, _ => { });
        s.Enter(1); events.Clear(); confirmation = true; s.Refresh();
        Assert.Empty(events); confirmation = false; s.Refresh();
        Assert.IsType<MenuContentPresented>(Assert.Single(events));
    }
    [Fact] public void ReadFailureIsAudibleOnceAndCanRecoverWithoutDisablingOtherAccessibility()
    {
        var events = new List<AccessibilityEvent>(); var logs = new List<string>();
        FieldSubmenuSnapshot? snapshot = null; long now = 0;
        var s = new FieldSubmenuSession(_ => snapshot, _ => "Inventory", _ => false, () => true, events.Add, logs.Add, () => now);
        s.Enter(1); s.Refresh(); Assert.Empty(events); Assert.Single(logs);
        now = 500; s.Refresh(); s.Refresh(); Assert.Single(events); Assert.Single(logs);
        snapshot = new("Inventory", "Inventory", "potion", "Potion x3"); s.Refresh();
        Assert.Equal("Potion x3", Assert.IsType<MenuContentPresented>(events[1]).Text);
    }
    [Fact] public void BriefRedrawDoesNotWarnOrRepeatTheUnchangedSelection()
    {
        var events = new List<AccessibilityEvent>(); long now = 0;
        FieldSubmenuSnapshot? snapshot = new("Inventory", "Inventory", "potion", "Potion x3");
        var s = new FieldSubmenuSession(_ => snapshot, _ => "Inventory", _ => false, () => true,
            events.Add, _ => { }, () => now);
        s.Enter(1); events.Clear(); var selected = snapshot;
        snapshot = null; s.Refresh(); now = 100; s.Refresh();
        snapshot = selected; s.Refresh(); Assert.Empty(events);
        snapshot = null; now = 700; s.Refresh(); Assert.Empty(events);
        now = 1200; s.Refresh(); Assert.IsType<MenuContentPresented>(Assert.Single(events));
    }
    [Fact] public void LosingForegroundAndLeavingTheMenuDoNotReadStaleText()
    {
        var events = new List<AccessibilityEvent>(); var foreground = false; string? title = "Inventory";
        var s = new FieldSubmenuSession(_ => new("Inventory", "Inventory", "potion", "Potion"),
            _ => title, _ => false, () => foreground, events.Add, _ => { });
        s.Enter(1); Assert.Empty(events); foreground = true; s.Refresh(); Assert.Single(events);
        events.Clear(); title = null; s.Refresh();
        Assert.IsType<MenuExited>(Assert.Single(events)); Assert.False(s.HasContext);
    }
}
