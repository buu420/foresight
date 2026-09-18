using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>The native confirm handler at 17D230 stores the player's own X and Y at
/// engine+13124/+13128, then dispatches on facing: 17D4C0 up, 17D610 down, 17D760 left,
/// 17D8B0 right. Each of the four tests the gap along the faced axis against 0x1C0 and
/// the gap across it against 0xC0, and requires the actor to be on the faced side.
/// Disassembly: artifacts/research/shop-enemies-0325/navigation/confirm-disasm.txt.</summary>
public sealed class InteractionRangeTests
{
    [Fact]
    public void TheRangeConstantsAreTheOnesTheHandlerCompares()
    {
        Assert.Equal(0x1C0, FieldInteractionRange.Along);
        Assert.Equal(0xC0, FieldInteractionRange.Lateral);
    }

    [Theory]
    [InlineData(0, 400)]     // straight up the counter, inside 0x1C0
    [InlineData(0, -400)]    // and straight down
    [InlineData(400, 0)]     // along X instead
    [InlineData(-400, 0)]
    [InlineData(176, 432)]   // corner of the tall lobe
    [InlineData(432, 176)]   // corner of the wide lobe
    public void ActorsInsideEitherLobeCanBeConfirmed(int dx, int dy) =>
        Assert.True(FieldInteractionRange.Reaches(0, 0, dx, dy));

    [Theory]
    [InlineData(0, 448)]     // exactly 0x1C0 is already too far: the compare is a borrow
    [InlineData(448, 0)]
    [InlineData(192, 192)]   // 0xC0 on both axes leaves no facing that fits
    [InlineData(200, 400)]   // too wide for the tall lobe, too short for the wide one
    [InlineData(400, 200)]
    [InlineData(768, 0)]     // three tiles away
    [InlineData(0, 768)]
    [InlineData(544, 544)]
    public void ActorsOutsideBothLobesCannotBeConfirmed(int dx, int dy) =>
        Assert.False(FieldInteractionRange.Reaches(0, 0, dx, dy));

    [Fact]
    public void TheMarketCounterIsInRangeFromTheCustomerFloorButTheClerkBehindItIsNot()
    {
        // Scene 118. The nearest floor the player can stand on is row 9 at y 2432;
        // the counter actor sits at y 2032 and the clerk at y 1888.
        Assert.True(FieldInteractionRange.Reaches(14336, 2432, 14336, 2032));
        Assert.False(FieldInteractionRange.Reaches(14336, 2432, 14336, 1888));
    }

    [Fact]
    public void ArrivalToleranceCountsAgainstBothLimits()
    {
        // The controller calls it arrived within one eighth of a tile on each axis, so a goal
        // is only honest if the whole of that box is still inside the handler's range.
        Assert.True(FieldInteractionRange.ReachesWithin(0, 0, 0, 400, 32));
        Assert.False(FieldInteractionRange.ReachesWithin(0, 0, 0, 416, 32));
        Assert.True(FieldInteractionRange.ReachesWithin(0, 0, 100, 0, 32));
        Assert.False(FieldInteractionRange.ReachesWithin(0, 0, 160, 160, 32));
    }

    [Fact]
    public void WithNoToleranceTheMarginIsTheHandlersOwn()
    {
        Assert.True(FieldInteractionRange.ReachesWithin(0, 0, 0, 447, 0));
        Assert.False(FieldInteractionRange.ReachesWithin(0, 0, 0, 448, 0));
        Assert.Equal(FieldInteractionRange.Reaches(0, 0, 176, 432),
            FieldInteractionRange.ReachesWithin(0, 0, 176, 432, 0));
    }

    [Fact]
    public void RoundingTheActorUpwardMustNotBuyExtraRange()
    {
        // Fine Y 2016 rounds to 2048. Judged on the rounded value a goal at 2432 looks
        // like 384 and passes; against the actual actor it is 416, and 416 + 32 is 448.
        Assert.False(FieldInteractionRange.ReachesWithin(14336, 2432, 14336, 2016, 32));
        Assert.True(FieldInteractionRange.ReachesWithin(14336, 2368, 14336, 2016, 32));
    }
}
