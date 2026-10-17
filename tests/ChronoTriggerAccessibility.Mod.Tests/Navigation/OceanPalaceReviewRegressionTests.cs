using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Regressions from the 0.3.67 Navigation review
/// (artifacts/research/ocean-palace-0367/claude-entities/release-review.md).</summary>
public sealed class OceanPalaceReviewRegressionTests
{
    // Atel_0499 parks the Algetty cutscene cast on tile (9,17), below the doorway on floorless
    // rows, while global 0x1A0 bit 0x20 says the scene is pending; actor 0 starts it on entry.

    [Fact]
    public void PendingAlgettySceneListsNoneOfItsParkedCast()
    {
        var frame = OceanPalaceFixtures.Frame(Algetty(pending: true));
        Assert.DoesNotContain(frame.Targets, t => new[] { 9, 11, 12, 13 }.Any(slot =>
            t.Id.StartsWith($"actor:{slot}:", StringComparison.Ordinal)));
        Assert.Contains(frame.Targets, t => t.Id == "actor:8:4:67");
        Assert.Contains(frame.Targets, t => t.Id == "actor:10:4:0" && t.Label == "Melchior");
    }

    [Fact]
    public void PendingAlgettySceneStoryIsANoteRatherThanAWalk()
    {
        var story = Assert.Single(OceanPalaceFixtures.Frame(Algetty(pending: true)).Targets,
            t => t.Id == "story:full:algetty-elder");
        Assert.True(story.IsStoryNote);
        Assert.Empty(story.ApproachPoints);
        Assert.Contains("runs by itself", story.Instruction, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, 2432, 4607)]   // not pending
    [InlineData(true, 2944, 1919)]    // pending, but at its ordinary tile (11,7)
    public void OnlyTheFlaggedStagingTileRemovesTheMessenger(bool pending, int x, int y)
    {
        var frame = OceanPalaceFixtures.Frame(Algetty(pending, messenger: (x, y)));
        Assert.Contains(frame.Targets, t => t.Id == "actor:9:4:65");
    }

    // A shown name identifies a character the player can see; alone it reveals no one.

    [Theory]
    [InlineData(414, 14, 6, 0xA1, 0xC6)]   // Golem room Dalton, drawn at (8,13) from entry
    [InlineData(415, 12, 4, 0x09, 0xC9)]
    [InlineData(415, 13, 4, 0x4A, 0xC9)]
    [InlineData(415, 14, 4, 0x3C, 0xC9)]
    [InlineData(404, 9, 5, 0x98, 0xBD)]
    public void ANamedCharacterWithoutAnInteractionIsNotRevealedOffCamera(int scene, int slot, int type, int visual,
        int point)
    {
        var frame = Synthetic(scene, 16, 48, Story(point), (2048, 11000), new(0, 9216, 4224, 12416),
            Actor(slot, 2048, 3328, type, visual));
        Assert.DoesNotContain(frame.Targets, t => t.Id == $"actor:{slot}:{type}:{visual}");
    }

    [Theory]
    [InlineData(415, 13, 4, 0x4A, 0xC9, "Queen")]
    [InlineData(404, 9, 5, 0x98, 0xBD, "Mune")]
    public void ANamedCharacterOnCameraIsListedByName(int scene, int slot, int type, int visual, int point, string name)
    {
        var frame = Synthetic(scene, 16, 48, Story(point), (2048, 5300), new(0, 2304, 4224, 5504),
            Actor(slot, 2048, 3328, type, visual));
        var person = Assert.Single(frame.Targets, t => t.Id == $"actor:{slot}:{type}:{visual}");
        Assert.Equal(name, person.Label);
        Assert.True(person.Visible);
    }

    [Fact]
    public void EarlierNamedSlotsKeepTheirGuidance()
    {
        // Kino's scene-430 slot has no action of its own (StoryCharacterLabels, 0.3.56).
        var frame = Synthetic(430, 64, 64, Story(0x98), (1000, 1000), new(0, 0, 2048, 2048),
            Actor(17, 40 * 256 + 128, 40 * 256 + 128, 4, 6));
        var kino = Assert.Single(frame.Targets, t => t.Id == "actor:17:4:6");
        Assert.Equal("Kino", kino.Label);
        Assert.False(kino.Visible);
        Assert.True(kino.GuideAvailable);
    }

    [Fact]
    public void GolemRoomDaltonOnCameraLeadsToHisControllersNativeTrigger()
    {
        // Atel_0222 actor 12 loops while local08 is 0 and starts Dalton's scene when the
        // leader's row is below 0x13 (catalog region 12, rows 0-18). Dalton has no Confirm.
        var frame = GolemRoom(onCamera: true, controllerActive: true);
        var dalton = Assert.Single(frame.Targets, t => t.Id == "actor:14:6:161");
        Assert.Equal("Dalton", dalton.Label);
        Assert.True(dalton.GuideAvailable);
        Assert.Null(dalton.ConfirmFacings);
        Assert.NotEmpty(dalton.ApproachPoints);
        Assert.All(dalton.ApproachPoints, p => Assert.True(p.Y / 256 <= 18, $"{p} is outside the trigger rows"));
        AssertGuides(frame, dalton);
    }

    [Fact]
    public void GolemRoomDaltonOffCameraIsNotRevealedEvenWithHisController()
    {
        var frame = GolemRoom(onCamera: false, controllerActive: true);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "actor:14:6:161");
    }

    [Fact]
    public void GolemRoomDaltonWithoutAnActiveControllerKeepsOnlyHisVisibleName()
    {
        var frame = GolemRoom(onCamera: true, controllerActive: false);
        var dalton = Assert.Single(frame.Targets, t => t.Id == "actor:14:6:161");
        Assert.Equal("Dalton", dalton.Label);
        Assert.False(dalton.GuideAvailable);
    }

    // Sprites drawn beneath the opaque background (live priority 0).

    [Fact]
    public void UpperHallMageBelowTheBackgroundIsNotListed()
    {
        // Atel_0393 actor 23: load_enemy 3B, 07E8 8E 04, raised only by actor 7 (07F8 8E 33).
        var frame = Synthetic(405, 48, 48, Story(195), (6000, 7000), new(3840, 5760, 8064, 8960),
            Actor(23, 3840, 8944, 5, 0x3B) with { RenderPriorityPacked = 0, LoadedFlag = 0 });
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("actor:23:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0x2020, 0x3B, true)]   // raised above the background
    [InlineData(0, 0x52, true)]        // another sprite
    [InlineData(0, 0x3B, false)]       // scene identity not coherent
    public void OnlyTheAuditedUpperHallMageIsConcealed(int priority, int visual, bool coherent)
    {
        var frame = Synthetic(405, 48, 48, Story(195), (6000, 7000), new(3840, 5760, 8064, 8960),
            Actor(23, 3840, 8944, 5, visual) with { RenderPriorityPacked = priority, LoadedFlag = 0 }, coherent);
        Assert.Contains(frame.Targets, t => t.Id == $"actor:23:5:{visual}");
    }

    [Fact]
    public void AlgettyPairBelowTheBackgroundIsNotListedAfterTheScene()
    {
        // Capture 031602-5863 (0xBA): Atel_0499 0494/050D 8E 04 at the doorway, after KMES1_117.
        var frame = OceanPalaceFixtures.Frame("031602-5863");
        Assert.DoesNotContain(frame.Targets, t => t.Id is "actor:11:4:9" or "actor:12:4:10");
        Assert.Contains(frame.Targets, t => t.Id == "actor:8:4:67");
        Assert.Contains(frame.Targets, t => t.Id == "actor:10:4:0");
    }

    [Theory]
    [InlineData(11, 0x2020, 0x09)]
    [InlineData(12, 0, 0x0B)]
    public void TheAlgettyPairNeedsItsExactSpriteAndBackgroundPriority(int slot, int priority, int visual)
    {
        var capture = OceanPalaceFixtures.Read("031602-5863");
        capture = capture with { Field = capture.Field with { Actors = capture.Field.Actors.Select(a =>
            a.Index == slot ? a with { RenderPriorityPacked = priority, VisualIndex = visual } : a).ToArray() } };
        Assert.Contains(OceanPalaceFixtures.Frame(capture).Targets, t => t.Id == $"actor:{slot}:4:{visual}");
    }

    // Atel_0399 bridge switch: panel actor 8 (load_npc A7, 8E 26) is drawn; knob actor 9
    // (load_npc 64, 8E 04) beneath it runs function 2 on contact.

    private const string Panel = "actor:8:4:167";

    [Fact]
    public void InactiveBridgeSwitchStaysListedWithANeutralRestriction()
    {
        var frame = Bridge(0x17);
        var panel = Assert.Single(frame.Targets, t => t.Label == "Floor switch");
        Assert.Equal(Panel, panel.Id);
        Assert.Equal((4336, 6640), (panel.Position.X, panel.Position.Y));
        Assert.False(panel.GuideAvailable);
        Assert.Empty(panel.ApproachPoints);
        Assert.Equal("This switch is currently inactive.", panel.GuidanceRestriction);
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("actor:9:", StringComparison.Ordinal));
    }

    [Fact]
    public void ActiveBridgeSwitchFinishesOnTheKnobsContact()
    {
        var frame = Bridge(0x77);
        var panel = Assert.Single(frame.Targets, t => t.Label == "Floor switch");
        Assert.Equal(Panel, panel.Id);
        Assert.Equal((4336, 6640), (panel.Position.X, panel.Position.Y));
        Assert.True(panel.GuideAvailable);
        Assert.Null(panel.GuidanceRestriction);
        Assert.Null(panel.ConfirmFacings);
        Assert.NotEmpty(panel.ApproachPoints);
        Assert.All(panel.ApproachPoints, p => Assert.Equal(6480 >> 8, p.Y >> 8));
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:palace-bridge");
        Assert.Equal(panel.ApproachPoints, story.ApproachPoints);
        AssertGuides(frame, panel);
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("actor:9:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0x17)]
    [InlineData(0x77)]
    public void AKnobHiddenByAnEarlyTouchLeavesThePanelListed(int palace)
    {
        // An early touch hides the knob (Atel0399 fn2) without changing any flag.
        var frame = Bridge(palace, knob: a => a with { DrawMode = 0 });
        var panel = Assert.Single(frame.Targets, t => t.Id == Panel);
        Assert.False(panel.GuideAvailable);
        Assert.Equal("This switch is currently inactive.", panel.GuidanceRestriction);
    }

    [Fact]
    public void ToggledBridgeKeepsASingleSwitchRow()
    {
        // 0x162 bit 0x80 and 0x15A bit 3: Atel_0399 places the panel at 1B00 and the knob at 1A90.
        var frame = Bridge(0xF7, extended: true);
        var panel = Assert.Single(frame.Targets, t => t.Label == "Floor switch");
        Assert.Equal(Panel, panel.Id);
        Assert.True(panel.GuideAvailable);
        Assert.All(panel.ApproachPoints, p => Assert.Equal(6800 >> 8, p.Y >> 8));
    }

    [Theory]
    [InlineData(0, 1, 0xA7)]        // panel below the background
    [InlineData(0x2020, 0, 0xA7)]   // panel not drawn
    [InlineData(0x2020, 1, 0xA6)]   // another sprite
    public void WithoutTheVisibleNativePanelNoFloorSwitchIsListed(int priority, int drawMode, int visual)
    {
        var frame = Bridge(0x77, panel: a => a with { RenderPriorityPacked = priority, DrawMode = drawMode, VisualIndex = visual });
        Assert.DoesNotContain(frame.Targets, t => t.Label == "Floor switch");
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("actor:9:", StringComparison.Ordinal));
    }

    [Fact]
    public void ElevatorLegIsNamedForTheElevator()
    {
        var story = Assert.Single(OceanPalaceFixtures.Frame("062759-1078").Targets,
            t => t.Id == "story:full:palace-lower-west");
        Assert.Equal("Take the elevator down", story.Label);
    }

    private static OceanPalaceFixtures.Capture Algetty(bool pending, (int X, int Y)? messenger = null)
    {
        var capture = OceanPalaceFixtures.Read("031602-5863");
        var (mx, my) = messenger ?? (2432, 4607);
        var leader = capture.Field.LeadPlayer!.Index;
        var actors = capture.Field.Actors.Select(a => a.Index switch
        {
            _ when a.Index == leader => Move(a, 2176, 2303),
            9 => Move(a with { ClassTag = 4, VisualIndex = 0x41, DrawMode = 1, LoadedFlag = 1, ActivationBinding = 0,
                RenderPriorityPacked = 0x3030, ScriptCallsEnabled = true }, mx, my),
            11 => Move(a with { DrawMode = 1, ActivationBinding = 0, RenderPriorityPacked = 0x2020 }, 2432, 4607),
            12 => Move(a with { DrawMode = 1, ActivationBinding = 0, RenderPriorityPacked = 0x2020 }, 2432, 4607),
            13 => Move(a with { ClassTag = 5, VisualIndex = 0xA1, DrawMode = 1, LoadedFlag = 1, ActivationBinding = 0,
                RenderPriorityPacked = 0x2020, ScriptCallsEnabled = true }, 2304, 4352),
            _ => a,
        }).ToArray();
        var flags = capture.Story.Globals.GetValueOrDefault(0x1A0) & ~0x20 | (pending ? 0x20 : 0);
        var story = capture.Story with { Point = 0xB7,
            Globals = new Dictionary<int, int>(capture.Story.Globals) { [0] = 0xB7, [0x1A0] = flags } };
        return capture with { Story = story,
            Field = capture.Field with { Actors = actors, LeadPlayer = actors.Single(a => a.Index == leader) } };
    }

    private static NavigationFrame GolemRoom(bool onCamera, bool controllerActive)
    {
        var controller = Actor(12, 6144, 2560, 7, 0) with
        {
            ScriptProcessingEnabled = controllerActive, ScriptCallsEnabled = controllerActive,
        };
        return onCamera
            ? Synthetic(414, 16, 32, Story(0xC6), (2048, 5300), new(0, 2304, 4224, 5504),
                Actor(14, 2048, 3328, 6, 0xA1), controller)
            : Synthetic(414, 16, 32, Story(0xC6), (2048, 7000), new(0, 5120, 4224, 8320),
                Actor(14, 2048, 3328, 6, 0xA1), controller);
    }

    private static NavigationFrame Bridge(int palace, bool extended = false,
        Func<FieldActorSnapshot, FieldActorSnapshot>? panel = null, Func<FieldActorSnapshot, FieldActorSnapshot>? knob = null)
    {
        var story = new FieldStoryState(198, true)
        {
            Globals = new Dictionary<int, int> { [0] = 198, [0x162] = palace, [0x15A] = extended ? 0x08 : 0 },
            Locals = new Dictionary<int, int>(),
        };
        var plate = Actor(8, 4336, extended ? 6912 : 6640, 4, 0xA7) with { LoadedFlag = 0 };
        var button = Actor(9, 4336, extended ? 6800 : 6480, 4, 0x64) with { RenderPriorityPacked = 0, LoadedFlag = 0 };
        return Synthetic(412, 32, 32, story, (4336, 3583), new(0, 0, 32 * 256, 32 * 256),
            panel?.Invoke(plate) ?? plate, knob?.Invoke(button) ?? button);
    }

    private static FieldStoryState Story(int point) => new(point, true)
    {
        Globals = new Dictionary<int, int> { [0] = point },
        Locals = new Dictionary<int, int> { [8] = 0 },
    };

    private static NavigationFrame Synthetic(int scene, int width, int height, FieldStoryState story,
        (int X, int Y) player, FieldViewport viewport, params FieldActorSnapshot[] actors) =>
        Synthetic(scene, width, height, story, player, viewport, actors, true);

    private static NavigationFrame Synthetic(int scene, int width, int height, FieldStoryState story,
        (int X, int Y) player, FieldViewport viewport, FieldActorSnapshot actor, bool coherent) =>
        Synthetic(scene, width, height, story, player, viewport, [actor], coherent);

    private static NavigationFrame Synthetic(int scene, int width, int height, FieldStoryState story,
        (int X, int Y) player, FieldViewport viewport, FieldActorSnapshot[] actors, bool coherent)
    {
        var lead = Actor(1, player.X, player.Y, 0, 0) with { IsPartyMember = true };
        var all = new[] { lead }.Concat(actors).ToArray();
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, all.Length, scene, coherent,
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

    private static FieldActorSnapshot Move(FieldActorSnapshot a, int x, int y) =>
        a with { FineX = x, FineY = y, TileX = x >> 8, TileY = y >> 8, FractionX = x & 255, FractionY = y & 255,
            CoordinatesCoherent = true, FacingValid = true, DrawModeKnown = true };

    private static void AssertGuides(NavigationFrame frame, NavigationTarget target)
    {
        foreach (var command in new[] { NavigationCommand.Guide, NavigationCommand.ToggleWalk })
        {
            var controller = new NavigationController();
            var selected = frame with { Targets = [target] };
            for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, selected, 0);
            var result = controller.Handle(command, selected, 1);
            Assert.True(result.Guiding, string.Join(" | ", result.Speech));
            Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        }
    }

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
