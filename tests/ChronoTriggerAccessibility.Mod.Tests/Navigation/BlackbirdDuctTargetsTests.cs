using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Script facts behind the Blackbird duct rows, checked against the installed
/// catalog. The map here is an open 64x64 floor with the shaft's ten MapJump cells and
/// the one solid grate tile (54,33); FieldNavigationSource behaviour over the logged
/// shaft capture is covered by BlackbirdDuctNavigationTests.</summary>
public sealed class BlackbirdDuctTargetsTests
{
    [Fact]
    public void OnlyTheShaftsTwentyNineConfirmGratesAreGrates()
    {
        var shaft = GameNavigationCatalog.ForScene(BlackbirdDuctTargets.Shaft)!;
        var grates = shaft.Regions.Where(r => BlackbirdDuctTargets.IsGrate(shaft.Id, r)).ToArray();
        Assert.Equal(Enumerable.Range(8, 29), grates.Select(r => r.Actor).Order());
        Assert.All(grates, r => Assert.Equal("Warp", r.Kind));
        Assert.DoesNotContain(shaft.Regions, r => r.Kind != "Warp" && BlackbirdDuctTargets.IsGrate(shaft.Id, r));
        // The same records in any other scene are ordinary passages.
        Assert.All(grates, r => Assert.False(BlackbirdDuctTargets.IsGrate(BlackbirdDuctTargets.Cell, r)));
        Assert.DoesNotContain(GameNavigationCatalog.Scenes.Where(s => s.Id != shaft.Id),
            s => s.Regions.Any(r => BlackbirdDuctTargets.IsGrate(s.Id, r)));
    }

    [Fact]
    public void GratesAreNumberedAcrossTheWholeShaftWithoutNamingTheRoomBelow()
    {
        var shaft = GameNavigationCatalog.ForScene(BlackbirdDuctTargets.Shaft)!;
        var map = ShaftMap();
        string Label(int actor) => BlackbirdDuctTargets.GrateLabel(shaft.Id,
            shaft.Regions.Single(r => r.Actor == actor && r.Kind == "Warp"), map);
        Assert.Equal("Viewing grate 1", Label(10));   // (32,4), the top of the shaft
        Assert.Equal("Viewing grate 2", Label(8));    // (3,8)
        Assert.Equal("Viewing grate 3", Label(9));    // (17,11)
        Assert.Equal("Viewing grate 13", Label(20));  // (60,26)
        Assert.Equal("Viewing grate 17", Label(24));  // (44,33)
        Assert.Equal("Viewing grate 18", Label(27));  // (10,40): (54,33) is not counted
        Assert.Equal("Viewing grate 22", Label(26));  // compares (60,40), sits at (60,33)
        Assert.Equal("Viewing grate 28", Label(36));  // (32,60), the bottom
        var labels = shaft.Regions.Where(r => BlackbirdDuctTargets.IsGrate(shaft.Id, r) && r.Actor != 25)
            .Select(r => BlackbirdDuctTargets.GrateLabel(shaft.Id, r, map)).ToArray();
        Assert.Equal(28, labels.Distinct().Count());
        Assert.DoesNotContain(labels, l => l.Contains("Room") || l.Contains("Passage"));
    }

    [Theory]
    [InlineData(0, "Down to the Rearward Passage, lower opening")]
    [InlineData(1, "Down to the Rearward Passage, upper opening")]
    [InlineData(2, "Down to the Forward Passage")]
    [InlineData(3, "Down to Room 4")]
    [InlineData(5, "Down to Room 2, the cell")]
    [InlineData(9, "Down to Room 9")]
    public void ShaftOpeningsNameTheRoomBelow(int exit, string label) =>
        Assert.Equal(label, BlackbirdDuctTargets.ExitLabel(BlackbirdDuctTargets.Shaft, exit, ShaftMap()));

    [Fact]
    public void OtherScenesAndUnknownDestinationsKeepTheirOwnLabels()
    {
        var map = ShaftMap();
        Assert.Null(BlackbirdDuctTargets.ExitLabel(BlackbirdDuctTargets.Cell, 0, map));
        Assert.Null(BlackbirdDuctTargets.ExitLabel(BlackbirdDuctTargets.Shaft, 10, map));
        Assert.Null(BlackbirdDuctTargets.ExitLabel(BlackbirdDuctTargets.Shaft, 3, map with { ExitDestinations = null }));
    }

    [Fact]
    public void TheCellVentNamesAreTheTwoNativeVentActors()
    {
        Assert.Equal("Air duct", BlackbirdDuctTargets.MarkerLabel(BlackbirdDuctTargets.Cell, 10));
        Assert.Equal("Air duct entrance", BlackbirdDuctTargets.MarkerLabel(BlackbirdDuctTargets.Cell, 11));
        Assert.Null(BlackbirdDuctTargets.MarkerLabel(BlackbirdDuctTargets.Cell, 9));
        Assert.Null(BlackbirdDuctTargets.MarkerLabel(BlackbirdDuctTargets.Shaft, 10));
        Assert.Equal("Press Confirm to climb into the air duct.",
            BlackbirdDuctTargets.ArrivalInstruction(BlackbirdDuctTargets.Cell, 11));
        Assert.Null(BlackbirdDuctTargets.ArrivalInstruction(BlackbirdDuctTargets.Cell, 10));
    }

    [Fact]
    public void TheCellTouchIsOnlyFoldedIntoALiveCellOpening()
    {
        var map = ShaftMap();
        Assert.True(BlackbirdDuctTargets.ConcealedLandmark(BlackbirdDuctTargets.Shaft, 6, map));
        Assert.False(BlackbirdDuctTargets.ConcealedLandmark(BlackbirdDuctTargets.Shaft, 7, map));
        Assert.False(BlackbirdDuctTargets.ConcealedLandmark(BlackbirdDuctTargets.Cell, 6, map));
        var withoutCell = map with { ExitCells = map.ExitCells.Select(c => c == 5 ? (byte)128 : c).ToArray() };
        Assert.False(BlackbirdDuctTargets.ConcealedLandmark(BlackbirdDuctTargets.Shaft, 6, withoutCell));
    }

    private static FieldMapSnapshot ShaftMap()
    {
        var layers = Enumerable.Repeat((byte)1, 64 * 64).ToArray();
        layers[33 * 64 + 54] = 0;
        var exits = Enumerable.Repeat((byte)128, 64 * 64).ToArray();
        // Map jump cells of scene376 (MapJumpDataTbl), in exit order 0..9.
        (int X, int Y)[] cells = [(7, 51), (46, 33), (21, 3), (12, 46), (52, 45), (54, 38), (10, 26), (53, 16), (44, 7), (17, 7)];
        for (var id = 0; id < cells.Length; id++) exits[cells[id].Y * 64 + cells[id].X] = (byte)id;
        return new(64, 64, new byte[64 * 64], new byte[64 * 64], layers, 1, false, 64, 64, exits)
        {
            ExitDestinations = new Dictionary<int, int>
            {
                [0] = 368, [1] = 368, [2] = 369, [3] = 373, [4] = 372, [5] = 371, [6] = 442, [7] = 375, [8] = 445, [9] = 444,
            },
        };
    }
}
