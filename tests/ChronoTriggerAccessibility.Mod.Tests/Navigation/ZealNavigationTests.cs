using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class ZealNavigationTests
{
    [Theory]
    [InlineData(355, 10, 115, 4224, 6399, "A")]
    [InlineData(355, 11, 128, 7040, 3327, "B")]
    [InlineData(355, 13, 0, 1664, 2047, "C")]
    [InlineData(357, 11, 115, 3456, 4095, "A")]
    [InlineData(357, 12, 128, 896, 2815, "B")]
    [InlineData(357, 13, 136, 5760, 6399, "C")]
    public void BooksHaveStableLabelsAndNativeConfirmRoutesInBothModes(int scene, int slot,
        int visual, int x, int y, string letter)
    {
        var book = Actor(slot, x, y, visual == 0 ? 7 : 4, visual);
        var frame = Build(scene, [book]);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects);
        Assert.Equal($"Book {letter}, closed", target.Label);
        Assert.Contains("Confirm", target.ArrivalInstruction);
        Assert.DoesNotContain("Fire", target.Label);
        Assert.DoesNotContain("Water", target.Label);
        Assert.DoesNotContain("Wind", target.Label);
        foreach (var command in new[] { NavigationCommand.Guide, NavigationCommand.ToggleWalk })
        {
            var nav = new NavigationController();
            nav.Handle(NavigationCommand.NextCategory, frame, 0);
            nav.Handle(NavigationCommand.NextCategory, frame, 1);
            var result = nav.Handle(command, frame, 2);
            Assert.True(result.Guiding, string.Join(" ", result.Speech));
            Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
            var route = NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints);
            Assert.NotNull(route);
            var finish = route[^1];
            var facing = target.ConfirmAt(finish)!(finish);
            Assert.NotEmpty(facing);
            Assert.Equal(slot, FieldInteractionRange.ConfirmWinner(Field(scene, [book]).Actors, 1,
                finish.X, finish.Y, facing[0]));
        }
    }

    [Theory]
    [InlineData(355, 1)]
    [InlineData(357, 3)]
    public void ConcealedPassageNeverAppearsUntilNativeOpeningCompletes(int scene, int exit)
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(scene, []);
        var closed = source.Build(field, Map(scene), Viewport, [], State());
        Assert.DoesNotContain(closed.Targets, t => t.Id == $"exit:{exit}" || t.Label.Contains("Hidden Room"));
        var opened = source.Build(field, OpenedMap(scene), Viewport, [], State() with
        { Locals = Locals((14, 1)) });
        var passage = Assert.Single(opened.Targets, t => t.Id == $"exit:{exit}");
        Assert.Equal("Opening behind the bookcase", passage.Label);
        Assert.NotNull(NavigationPathfinder.Find(opened.Graph, opened.Player, passage.ApproachPoints));
        var closedAgain = source.Build(field, Map(scene), Viewport, [], State());
        Assert.DoesNotContain(closedAgain.Targets, t => t.Id == $"exit:{exit}");
        var unreadable = source.Build(field, Map(scene), Viewport, [], new(159, true));
        Assert.DoesNotContain(unreadable.Targets, t => t.Id == $"exit:{exit}");
    }

    [Fact]
    public void EnhasaWindSpriteAndItsMapMarkerAreOneBookEvenWhenSpriteIsDrawn()
    {
        var frame = Build(355, [Actor(12, 1664, 2047, 4, 136), Actor(13, 1664, 2047, 7, 0)],
            State() with { Locals = Locals((12, 1)) });
        var books = frame.Targets.Where(t => t.Label.StartsWith("Book C")).ToArray();
        Assert.Equal("Book C, open", Assert.Single(books).Label);
        Assert.Equal("landmark:13", books[0].Id);
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("actor:12:"));
    }

    [Fact]
    public void UnreadableBookStateKeepsTheBookIdentifiableWithoutGuessingOpenOrClosed()
    {
        var frame = Build(357, [Actor(11, 3456, 4095, 4, 115)], new(159, true));
        Assert.Equal("Book A", Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects).Label);
    }

    [Theory]
    [InlineData(336)] [InlineData(337)] [InlineData(352)]
    [InlineData(353)] [InlineData(354)] [InlineData(356)]
    public void NativeTeleportPadIsAnObjectAndTheOutdoorDoorRemainsAnExit(int scene)
    {
        var sky = scene is 337 or 354 or 356;
        var y = scene is 336 or 353 ? 5360 : scene == 337 ? 6640 : scene == 352 ? 1264 : 1776;
        // Use the installed initial floor; actor8 is the native pad contact marker.
        var marker = Actor(8, 2048, y, 7, 0);
        var frame = Build(scene, [marker]);
        var pad = Assert.Single(frame.Targets, t => t.Id == "landmark:8");
        Assert.Equal(sky ? "Skyway teleport pad" : "Land Bridge teleport pad", pad.Label);
        Assert.Equal(NavigationCategory.Objects, pad.Category);
        Assert.Contains(frame.Targets, t => t.Id == "exit:0" && t.Category == NavigationCategory.Exits);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, pad.ApproachPoints));
    }

    [Fact]
    public void NativeMissingSkywayNameUsesTheVerifiedAreaName()
    {
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }, _ => "Map Not Specified")
            .Build(Field(337, []), Map(337), Viewport, [], State());
        Assert.Equal("Skyway", frame.AreaName);
    }

    [Theory]
    [InlineData(355, 9)]
    [InlineData(359, 8)]
    public void VerifiedWhiteDollDoesNotRetainTheGenericObjectLabel(int scene, int slot)
    {
        var actor = Actor(slot, 4080, 6128, 4, 100);
        var frame = new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(Field(scene, [actor]), OpenMap(), Viewport, [], State());
        Assert.Equal("Small white doll", Assert.Single(frame.Targets, t => t.Id == $"actor:{slot}:4:100").Label);
    }

    [Theory]
    [InlineData("wrong scene")] [InlineData("wrong class")]
    [InlineData("wrong visual")] [InlineData("hidden")]
    [InlineData("removed")] [InlineData("calls disabled")]
    [InlineData("incoherent scene")]
    public void UnverifiedBookIdentityCannotAcquireABookLabel(string mutation)
    {
        var book = Actor(11, 3456, 4095, 4, 115);
        book = mutation switch
        {
            "wrong class" => book with { ClassTag = 5 },
            "wrong visual" => book with { VisualIndex = 114 },
            "hidden" => book with { DrawMode = 0 },
            "removed" => book with { ClassTag = 0x84 },
            "calls disabled" => book with { ScriptCallsEnabled = false },
            _ => book,
        };
        var field = Field(mutation == "wrong scene" ? 360 : 357, [book]);
        if (mutation == "incoherent scene") field = field with { SceneIdCoherent = false };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, OpenMap(), Viewport, [], State());
        Assert.DoesNotContain(frame.Targets, t => t.Label.StartsWith("Book "));
    }

    [Fact]
    public void SealedSkywayPadKeepsItsAppearanceWithoutAnEnabledWarp()
    {
        var state = State() with { Globals = new Dictionary<int, int> { [0x57] = 128 } };
        var frame = Build(337, [Actor(8, 2048, 6640, 7, 0)], state);
        var pad = Assert.Single(frame.Targets, t => t.Id == "landmark:8");
        Assert.Equal(NavigationCategory.Objects, pad.Category);
        Assert.Equal("Skyway teleport pad", pad.Label);
        Assert.Equal("Teleportation is currently unavailable.", pad.Instruction);
    }

    [Fact]
    public void ProtoDomeUsesTheSameTimeGateNameAndCategoryAsTheForestGate()
    {
        var gate = Actor(11, 2048, 2047, 4, 151) with { ActivationEnabled = 1, LoadedFlag = 1 };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(Field(227, [gate]), OpenMap(), Viewport, [], new(64, true));
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:11:4:151");
        Assert.Equal("Time Gate", target.Label);
        Assert.Equal(NavigationCategory.Objects, target.Category);
    }

    private static readonly FieldViewport Viewport = new(0, 0, 16384, 16384);
    private static NavigationFrame Build(int scene, FieldActorSnapshot[] actors, FieldStoryState? state = null) =>
        new FieldNavigationSource(new NoMemory(), _ => { }).Build(Field(scene, actors), Map(scene), Viewport, [], state ?? State());
    private static FieldStoryState State() => new(159, true) { Locals = Locals(), Globals = new Dictionary<int, int> { [0x57] = 0 } };
    private static Dictionary<int, int> Locals(params (int Index, int Value)[] values)
    {
        var result = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0);
        foreach (var pair in values) result[pair.Index] = pair.Value;
        return result;
    }
    private static FieldNavigationSnapshot Field(int scene, FieldActorSnapshot[] actors)
    {
        var x = scene == 355 ? 4352 : scene == 357 ? 1280 : 2048;
        var y = scene == 355 ? 7423 : scene == 357 ? 7679 : scene is 336 or 337 or 353 ? 7167 : 3071;
        var lead = Actor(1, x, y, 0, 0) with { IsPartyMember = true, LoadedFlag = 1 };
        return new(0x1000, 0x4000, 0x2000, 0x20000, 40, scene, true, 3, 0, 0, 1, lead, [lead, .. actors]);
    }
    private static FieldActorSnapshot Actor(int slot, int x, int y, int kind, int visual) =>
        new(slot, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 1, 0, visual, kind,
            0x2020, 0, 128, false, true, true, true);
    private static FieldMapSnapshot Map(int scene)
    {
        using var stream = typeof(ZealNavigationTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.zeal-initial-maps-0361.json")!;
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.GetProperty(scene.ToString()).Deserialize<FieldMapSnapshot>()!;
    }
    private static FieldMapSnapshot OpenedMap(int scene)
    {
        var map = Map(scene);
        var result = map with { CollisionShapes = (byte[])map.CollisionShapes.Clone(),
            TerrainFlags = (byte[])map.TerrainFlags.Clone(), CollisionLayers = (byte[])map.CollisionLayers.Clone() };
        var copies = GameNavigationCatalog.ForScene(scene)!.Actors.SelectMany(a => a.Actions)
            .Where(a => a.Kind == "Terrain" && a.Copy is { Flags: 59 }).Select(a => a.Copy!).ToArray();
        foreach (var copy in copies)
        foreach (var (original, updated) in new[] { (map.CollisionShapes, result.CollisionShapes),
                     (map.TerrainFlags, result.TerrainFlags), (map.CollisionLayers, result.CollisionLayers) })
        for (var y = 0; y <= copy.Bottom - copy.Top; y++)
        for (var x = 0; x <= copy.Right - copy.Left; x++)
            updated[(copy.Y + y) * map.Width + copy.X + x] = original[(copy.Top + y) * map.Width + copy.Left + x];
        return result;
    }
    private static FieldMapSnapshot OpenMap() => new(64, 64, new byte[4096], new byte[4096],
        Enumerable.Repeat((byte)1, 4096).ToArray(), 1, false, 64, 64, Enumerable.Repeat((byte)128, 4096).ToArray());
    private sealed class NoMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
