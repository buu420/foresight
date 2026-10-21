using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>What the 256x192 field shows at the camera's edges in the Ocean Palace
/// (artifacts/research/bug-reports-20261020/claude-ocean/findings.md).</summary>
public sealed class OceanPalaceSightTests
{
    // Atel_0222 loads both Golems with 8E 00, beneath every background layer, until
    // Dalton's summon (fn3) raises them with 8E 22.

    [Fact]
    public void GolemsWaitingBeneathTheBackgroundAreNotPeople()
    {
        // 23.16.18 19:59:47: actor 15 at (0,4336) entered the camera window; people=1.
        var frame = Synthetic(414, 16, 32, Story(0xC6), (2048, 6271), new(0, 4336, 4224, 7536),
            Actor(15, 0, 4336, 5, 0x95) with { RenderPriorityPacked = 0 });
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("actor:15:", StringComparison.Ordinal));
    }

    [Fact]
    public void GolemRoomEntryShowsNoOne()
    {
        // 10-18 11:38:47: Dalton (2048,3328) stands 112 px above the field's top edge; the
        // Steam playthrough shows him only as his first line opens.
        var frame = Synthetic(414, 16, 32, Story(0xC6), (2048, 7423), new(0, 5120, 4224, 8320),
            Actor(14, 2048, 3328, 6, 0xA1), Actor(12, 6144, 2560, 7, 0),
            Actor(15, 0, 4336, 5, 0x95) with { RenderPriorityPacked = 0 });
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.People);
    }

    [Theory]
    [InlineData(0x2020, 0x95)]   // raised by the summon
    [InlineData(0, 0x94)]        // another sprite
    public void OnlyTheWaitingGolemIsConcealed(int priority, int visual)
    {
        var frame = Synthetic(414, 16, 32, Story(0xC6), (2048, 6271), new(0, 4336, 4224, 7536),
            Actor(15, 0, 4336, 5, visual) with { RenderPriorityPacked = priority });
        Assert.Contains(frame.Targets, t => t.Id == $"actor:15:5:{visual}");
    }

    // Every c415 (Mune) and c414 (Masa) frame is opaque at least 24 pixels above the foot
    // point that 17AD30 places on screen. The field shows 192 rows: TryViewport's window
    // reaches one 8-pixel row lower, so a head stays visible up to 16 pixels below it.

    [Theory]
    [InlineData(404, 9, 5, 0x98, 7408, 4096)]      // 10-18 11:09:14, foot 7 px below the window
    [InlineData(404, 9, 5, 0x98, 7408, 4000)]      // 10-19 16:18:28, foot 13 px below
    [InlineData(404, 9, 6, 0x98, 7408, 4096)]      // enemy sprites can load as class 6
    [InlineData(404, 9, 5, 0x98, 7551, 4096)]      // the highest opaque row on the last field row
    [InlineData(409, 33, 5, 0x97, 30704, 27392)]   // Masa: the same sheet on the lower stair
    public void ATwinWhoseHeadIsOnTheFieldIsShown(int scene, int slot, int type, int visual, int footY, int top)
    {
        var twin = Twin(slot, type, visual, footY);
        Assert.True(OceanPalaceSight.TwinHeadShown(scene, twin, Camera(twin, top)));
    }

    [Theory]
    [InlineData(404, 9, 5, 0x98, 7552, 4096)]      // every row below the field
    [InlineData(404, 9, 5, 0x98, 7408, 1024)]      // 10-19 16:18:14, arriving at the top
    [InlineData(404, 9, 5, 0x98, 4000, 4096)]      // above the top edge: the body is higher still
    [InlineData(404, 9, 5, 0x97, 7408, 4096)]      // Masa's sprite in Mune's slot
    [InlineData(404, 10, 5, 0x98, 7408, 4096)]
    [InlineData(409, 33, 5, 0x98, 30704, 27392)]   // Mune's sprite in Masa's slot
    [InlineData(401, 16, 5, 0x97, 7408, 4096)]     // Zeal Palace children use Masa's sprite
    public void OtherwiseNoTwinIsShown(int scene, int slot, int type, int visual, int footY, int top)
    {
        var twin = Twin(slot, type, visual, footY);
        Assert.False(OceanPalaceSight.TwinHeadShown(scene, twin, Camera(twin, top)));
    }

    [Fact]
    public void ATwinBesideTheFieldIsNotShown() =>
        Assert.False(OceanPalaceSight.TwinHeadShown(404, Actor(9, 4224, 7408, 5, 0x98), new(0, 4096, 4224, 7296)));

    // Mune (Atel_0220 actor 9) stands on the entrance pedestal at (2048,7408). He has no
    // Confirm handler; his scene starts once the leader's row passes 0x19.

    [Theory]
    [InlineData(2432, 6143, 4096)]   // 10-18 11:09:14, loaded at the save point
    [InlineData(2336, 5960, 4000)]   // 10-19 16:18:28; People was empty at 16:18:53
    public void MuneIsListedOnceHisHeadIsOnScreen(int x, int y, int top)
    {
        var frame = Synthetic(404, 16, 32, Story(0xBD), (x, y), new(0, top, 4224, top + 3200),
            Actor(9, 2048, 7408, 5, 0x98));
        var mune = Assert.Single(frame.Targets, t => t.Id == "actor:9:5:152");
        Assert.Equal("Mune", mune.Label);
        Assert.Equal(NavigationCategory.People, mune.Category);
        Assert.True(mune.Visible);
        Assert.False(mune.GuideAvailable);
    }

    [Fact]
    public void MuneBelowTheFieldIsNotListedOnArrival()
    {
        // 10-19 16:18:14: arriving by the upper teleporter, camera rows 4..16.
        var frame = Synthetic(404, 16, 32, Story(0xBD), (2048, 2856), new(0, 1024, 4224, 4224),
            Actor(9, 2048, 7408, 5, 0x98));
        Assert.DoesNotContain(frame.Targets, t => t.Id == "actor:9:5:152");
    }

    // Mune stands at X 2048 and Masa at X 5504, each in the middle of a 4224-wide camera.
    private static FieldActorSnapshot Twin(int slot, int type, int visual, int footY) =>
        Actor(slot, slot == 33 ? 5504 : 2048, footY, type, visual);

    private static FieldViewport Camera(FieldActorSnapshot twin, int top) =>
        new(twin.FineX - 2048, top, twin.FineX + 2176, top + 3200);

    private static FieldStoryState Story(int point) => new(point, true)
    {
        Globals = new Dictionary<int, int> { [0] = point },
        Locals = new Dictionary<int, int> { [8] = 0 },
    };

    private static NavigationFrame Synthetic(int scene, int width, int height, FieldStoryState story,
        (int X, int Y) player, FieldViewport viewport, params FieldActorSnapshot[] actors)
    {
        var lead = Actor(1, player.X, player.Y, 0, 0) with { IsPartyMember = true };
        var all = new[] { lead }.Concat(actors).ToArray();
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, all.Length, scene, true,
            1, 0, 1, 1, lead, all, [1]);
        var cells = width * height;
        var map = new FieldMapSnapshot(width, height, new byte[cells], new byte[cells],
            Enumerable.Repeat((byte)1, cells).ToArray(), 1, false, width, height,
            Enumerable.Repeat((byte)128, cells).ToArray());
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, viewport, [], story);
    }

    private static FieldActorSnapshot Actor(int index, int x, int y, int classTag, int visual) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 1, 1, 1, visual, classTag, 0x2020,
            0, 128, false, true, true, true) { CurrentScriptOpcode = 0xB2 };

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
