using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class GameNavigationCatalogTests
{
    [Fact]
    public void RequirementsUseKnownInventoryRosterAndGold()
    {
        var unknown = new FieldStoryState(213, false);
        var known = unknown with
        {
            Inventory = new Dictionary<int, int> { [0x1001] = 1 }, Gold = 500,
            Party = [0, 1, 5, 4, 3, 255, 255, 255, 255],
        };
        GameNavigationCatalog.Requirement[] satisfied =
        [
            new("Item", 0x1001, 2, 0, true), new("Gold", 0, 4, 500, true),
            new("Recruited", 4, 0, 1, true), new("ActiveParty", 5, 0, 1, true),
        ];
        Assert.All(satisfied, r => Assert.True(r.Allows(known)));
        Assert.All(satisfied, r => Assert.False(r.Allows(unknown)));
        var activeRobo = new GameNavigationCatalog.Requirement("ActiveParty", 4, 0, 1, true);
        Assert.False(activeRobo.Allows(known));
        Assert.True(activeRobo.Allows(known, local: false));
        Assert.False(new GameNavigationCatalog.Requirement("Item", 0x1002, 2, 0, true).Allows(known));
    }
}
