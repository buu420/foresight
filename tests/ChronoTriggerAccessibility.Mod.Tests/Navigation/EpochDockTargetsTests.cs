using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Atel_0283 actor36 fn2, including native A4's one-byte branch operand.
/// The ordinary catalog decoder stops before the boarding choice. These fixtures
/// use installed MapTable_0085 collision planes and the native marker's xy 27,18.</summary>
public sealed class EpochDockTargetsTests
{
    private static readonly FieldViewport WholeRoom = new(0, 0, 12288, 12288);

    [Fact]
    public void ParkedEpochHasAReachableDockOnTheInstalledFloor()
    {
        var field = Field(); var map = NativeMap();
        var dock = Assert.Single(EpochDockTargets.Build(field, map, WholeRoom, Story()));
        Assert.Equal(NavigationCategory.Objects, dock.Category);
        Assert.Equal("Epoch dock", dock.Label);
        Assert.Equal(36, dock.ActorSlot);
        Assert.True(dock.GuideAvailable);
        Assert.True(dock.Visible);
        Assert.False(dock.Discovered);
        Assert.Null(dock.ConfirmFacings);
        Assert.NotNull(dock.ContactPosition);
        Assert.NotEmpty(dock.ApproachPoints);
        Assert.All(dock.ApproachPoints, p =>
        {
            Assert.Equal(1, p.Layer);
            Assert.True(new FieldNavigationGraph(map).TryPosition(p.X, p.Y, p.Layer, out var actual));
            Assert.Equal(p, actual);
        });
        var route = NavigationPathfinder.Search(new FieldNavigationGraph(map),
            new(field.LeadPlayer!.FineX, field.LeadPlayer.FineY, 1), dock.ApproachPoints).Route;
        Assert.NotNull(route);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(77)]
    [InlineData(171)]
    [InlineData(173)]
    public void NoEpochHintBeforeTheNativeStoryGate(int point) =>
        Assert.Empty(EpochDockTargets.Build(Field(), NativeMap(), WholeRoom, Story(point)));

    [Theory]
    [InlineData(174, 0)]
    [InlineData(174, 0x20)]
    [InlineData(174, 0x40)]
    [InlineData(220, 0)]
    public void OwningTheEpochDoesNotPutItAtThisDockWhenItIsParkedElsewhere(int point, int flags)
    {
        var story = Story(point) with { Globals = new Dictionary<int, int> { [0xAB] = flags, [0xD8] = 3 } };
        Assert.Empty(EpochDockTargets.Build(Field(), NativeMap(), WholeRoom, story));
    }

    [Theory]
    [InlineData(174)]
    [InlineData(202)]
    [InlineData(203)]
    [InlineData(220)]
    public void ValidLaterVisitsRetainTheDock(int point) =>
        Assert.Single(EpochDockTargets.Build(Field(), NativeMap(), WholeRoom, Story(point)));

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(3, true)]
    [InlineData(5, false)]
    [InlineData(7, true)]
    public void LaterReturnRespectsTheNativeGasparConversationGate(int flags, bool available)
    {
        var story = Story(203) with { Globals = new Dictionary<int, int> { [0xAB] = 0x80, [0xD8] = flags } };
        Assert.Equal(available, EpochDockTargets.Build(Field(), NativeMap(), WholeRoom, story).Count == 1);
    }

    public static TheoryData<string> Invalid => new()
    {
        "scene", "scene-incoherent", "no-control", "menu", "transition", "story-unknown",
        "parked-flag-unknown", "late-gate-unknown", "missing-marker", "wrong-class", "wrong-position",
        "party-marker", "marker-incoherent", "calls-disabled", "processing-disabled", "priority-busy",
        "priority-unknown", "leader-hidden", "map-blocked",
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void UnknownOrUnavailableNativeStateCannotCreateADock(string problem)
    {
        var field = Field(); var map = NativeMap(); FieldStoryState? story = Story();
        var marker = field.Actors.Single(a => a.Index == 36);
        switch (problem)
        {
            case "scene": field = field with { SceneId = 473 }; break;
            case "scene-incoherent": field = field with { SceneIdCoherent = false }; break;
            case "no-control": field = field with { ControlFlag = 0 }; break;
            case "menu": field = field with { InputMode = 1 }; break;
            case "transition": map = map with { TransitionPending = true }; break;
            case "story-unknown": story = null; break;
            case "parked-flag-unknown": story = story with { Globals = new Dictionary<int, int>() }; break;
            case "late-gate-unknown": story = Story(203) with { Globals = new Dictionary<int, int> { [0xAB] = 0x80 } }; break;
            case "missing-marker": field = field with { Actors = field.Actors.Where(a => a.Index != 36).ToArray() }; break;
            case "wrong-class": marker = marker with { ClassTag = 4 }; break;
            case "wrong-position": marker = marker with { FineX = 6272, TileX = 24 }; break;
            case "party-marker": marker = marker with { IsPartyMember = true }; break;
            case "marker-incoherent": marker = marker with { CoordinatesCoherent = false }; break;
            case "calls-disabled": marker = marker with { ScriptCallsEnabled = false }; break;
            case "processing-disabled": marker = marker with { ScriptProcessingEnabled = false }; break;
            case "priority-busy": marker = marker with { ScriptPriority = 2 }; break;
            case "priority-unknown": marker = marker with { ScriptPriority = null }; break;
            case "leader-hidden": field = field with { LeadPlayer = field.LeadPlayer! with { DrawMode = 0 } }; break;
            case "map-blocked": map = map with { CollisionLayers = new byte[map.Width * map.Height] }; break;
        }
        if (problem != "missing-marker")
            field = field with { Actors = field.Actors.Select(a => a.Index == 36 ? marker : a).ToArray() };
        Assert.Empty(EpochDockTargets.Build(field, map, WholeRoom, story));
    }

    [Fact]
    public void AnAvailableOffCameraDockIsAGuideWithoutPretendingItWasSeen()
    {
        var dock = Assert.Single(EpochDockTargets.Build(Field(), NativeMap(), new(1536, 256, 5760, 3456), Story()));
        Assert.True(dock.GuideAvailable);
        Assert.False(dock.Visible);
        Assert.False(dock.Discovered);
    }

    [Fact]
    public void TheSceneryMarkerHasNoSpriteIdentityRequirement()
    {
        // Class7 has no sprite. The native touch handler uses its slot, position,
        // script gates and priority; the unused visual word cannot hide the dock.
        var field = Field();
        field = field with { Actors = field.Actors.Select(a => a.Index == 36 ? a with { VisualIndex = 170 } : a).ToArray() };
        Assert.Single(EpochDockTargets.Build(field, NativeMap(), WholeRoom, Story()));
    }

    [Fact]
    public void FieldSourcePublishesTheParkedDockAndNeverTheMisdecodedEncounter()
    {
        var frame = Source().Build(Field(), NativeMap(), WholeRoom, [], Story());
        var dock = Assert.Single(frame.Targets, t => t.Id == "epoch-dock:36");
        Assert.Equal(NavigationCategory.Objects, dock.Category);
        Assert.Equal("Epoch dock", dock.Label);
        Assert.DoesNotContain(frame.Targets, t => t.Id is "actor-encounter:36" or "landmark:36");
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, dock.ApproachPoints).Route);
    }

    [Fact]
    public void FieldSourceCannotExposeAMarkerOrEncounterWhenTheEpochIsElsewhere()
    {
        var state = Story() with { Globals = new Dictionary<int, int> { [0xAB] = 0, [0xCD] = 6, [0xD8] = 3 } };
        var frame = Source().Build(Field(), NativeMap(), WholeRoom, [], state);
        Assert.DoesNotContain(frame.Targets, t => t.Id is "epoch-dock:36" or "actor-encounter:36" or "landmark:36");
    }

    [Fact]
    public void ANewCaptureRemovesThePreviouslySeenDockAfterTheEpochLeaves()
    {
        var source = Source();
        var first = source.Build(Field(), NativeMap(), WholeRoom, [], Story());
        Assert.Contains(first.Targets, t => t.Id == "epoch-dock:36" && t.Visible && t.Discovered);
        var state = Story() with { Globals = new Dictionary<int, int> { [0xAB] = 0, [0xCD] = 5, [0xD8] = 3 } };
        var next = source.Build(Field(), NativeMap(), WholeRoom, [], state);
        Assert.DoesNotContain(next.Targets, t => t.Id is "epoch-dock:36" or "actor-encounter:36" or "landmark:36");
    }

    [Fact]
    public void FieldSourceOffersAnOffCameraParkedDockWithoutInventingDiscovery()
    {
        var frame = Source().Build(Field(), NativeMap(), new(1536, 256, 5760, 3456), [], Story());
        var dock = Assert.Single(frame.Targets, t => t.Id == "epoch-dock:36");
        Assert.True(dock.GuideAvailable);
        Assert.False(dock.Visible);
        Assert.False(dock.Discovered);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void BothGuidanceModesApproachTheDockAndYieldToTheGamesBoardingChoice(NavigationCommand command)
    {
        var field = Field(); var map = NativeMap();
        var target = Assert.Single(EpochDockTargets.Build(field, map, WholeRoom, Story()));
        var frame = new NavigationFrame("464", true, new(7392, 5343, 1), [target],
            new FieldNavigationGraph(map, new FieldActorCollisionRules(field)), NavigationUnits.LocalStep);
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.NextCategory, frame, 1);
        var result = controller.Handle(command, frame, 2);
        Assert.True(result.Guiding, string.Join(" ", result.Speech));
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        if (command == NavigationCommand.Guide)
        {
            Assert.Equal(NavigationDirection.None, result.Direction);
            Assert.NotNull(result.ManualLeg);
        }
        else Assert.NotEqual(NavigationDirection.None, result.Direction);
        // Atel0283 E3 00 and C3 A5 00 06 open the game's own choice. Losing
        // normal field control cancels directional walking; the choice stays the player's.
        result = controller.Update(frame with { CanNavigate = false }, 16);
        Assert.False(result.Guiding);
        Assert.False(result.AutoWalking);
        Assert.Equal(NavigationDirection.None, result.Direction);
    }

    private static FieldStoryState Story(int point = 174) => new(point, true)
    {
        Globals = new Dictionary<int, int> { [0xAB] = 0x80, [0xCD] = 6, [0xD8] = 3 },
    };

    private static FieldNavigationSnapshot Field()
    {
        var leader = Actor(1, 7392, 5343) with { ClassTag = 0, IsPartyMember = true, LoadedFlag = 1 };
        var dock = Actor(36, 10112, 6399) with { ScriptAddress = 0x1990, CurrentScriptOpcode = 0xB2 };
        return new(0x10000, 0x20000, 0x30000, 0x40000, 37, 464, true, 1, 0, 0, 1, leader, [leader, dock], [1]);
    }

    private static FieldActorSnapshot Actor(int id, int x, int y) => new(id, x / 256, x, x & 255,
        y / 256, y, y & 255, 0, 1, 0, 0, 7, 0, 0, 0, false, true, true, true);

    private static FieldNavigationSource Source() => new(new NoMemory(), _ => { });

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }

    private static FieldMapSnapshot NativeMap()
    {
        using var stream = typeof(EpochDockTargetsTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.end-of-time-map-0345.json")!;
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        byte[] Plane(string name) => Convert.FromBase64String(root.GetProperty(name).GetString()!);
        var width = root.GetProperty("Width").GetInt32(); var height = root.GetProperty("Height").GetInt32();
        return new(width, height, Plane("CollisionShapes"), Plane("TerrainFlags"), Plane("CollisionLayers"),
            1, false, width, height, Plane("ExitCells"));
    }
}
