using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class BonusAreaCatalogTests
{
    [Theory]
    [InlineData(559, "Tower of the Ancients")]
    [InlineData(576, "Great Southern Swamp")]
    [InlineData(616, "Temporal Research Lab")]
    public void NativeSoftLineBreaksDoNotInventCommas(int scene, string name)
    {
        Assert.Equal(name, GameNavigationCatalog.ForScene(scene)!.Name);
    }
}
