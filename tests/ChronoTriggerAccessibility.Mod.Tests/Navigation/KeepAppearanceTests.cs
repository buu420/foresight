using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Installed Atel0290: four identical NPC121 lights have different
/// roles for each D0 pit number. Appearance and player choice cannot reveal D0.</summary>
public sealed class KeepAppearanceTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void HiddenPitNumberDoesNotIdentifyLightRolesOrChooseOneForThePlayer(int pit)
    {
        var frame = Build(471, State(136, pit), Lights(471));
        var objects = frame.Targets.Where(t => t.Category == NavigationCategory.Objects && t.Label.StartsWith("Glowing point")).ToArray();
        Assert.Equal(new[] { "Glowing point A", "Glowing point B", "Glowing point C", "Glowing point D" }, objects.Select(t => t.Label).Order().ToArray());
        Assert.DoesNotContain(frame.Targets, t => t.Category is NavigationCategory.Exits or NavigationCategory.Enemies);
        Assert.DoesNotContain(frame.Targets, t => t.Label.Contains("Save point", StringComparison.OrdinalIgnoreCase));
        var choices = frame.Targets.Where(t => t.Category == NavigationCategory.StoryEvents && !t.IsStoryNote).ToArray();
        Assert.Equal(4, choices.Length);
        foreach (var light in objects)
        {
            Assert.NotEmpty(light.ApproachPoints);
            Assert.True(frame.Graph.IsTerminal(light.ApproachPoints[0]));
            var route = NavigationPathfinder.Search(frame.Graph, frame.Player, light.ApproachPoints).Route;
            Assert.NotNull(route);
            Assert.DoesNotContain(route.Skip(1), p => frame.Graph.IsTerminal(p) &&
                !frame.Graph.IsSameTerminal(p, light.ApproachPoints[0]));
            Assert.All(light.ApproachPoints, p => Assert.Equal((light.Position.X >> 8, light.Position.Y >> 8), (p.X >> 8, p.Y >> 8)));
            Assert.Contains(choices, t => t.ApproachPoints.SequenceEqual(light.ApproachPoints));
            foreach (var command in new[] { NavigationCommand.Guide, NavigationCommand.ToggleWalk })
            {
                var controller = new NavigationController();
                var selected = frame with { Targets = [light] };
                for (var i = 0; i < (int)light.Category; i++) controller.Handle(NavigationCommand.NextCategory, selected, 0);
                var result = controller.Handle(command, selected, 1);
                Assert.True(result.Guiding, string.Join(" | ", result.Speech));
                Assert.False(controller.AllowsConfirmWhileFollowing(selected));
            }
        }
    }

    [Theory]
    [InlineData(1, 12)] [InlineData(2, 9)] [InlineData(3, 10)]
    [InlineData(4, 11)] [InlineData(5, 12)] [InlineData(6, 9)]
    public void SavingIsIdentifiedOnlyWithTheNativeCanSaveBitOnTheSameLight(int pit, int actor)
    {
        var lights = Lights(471);
        var save = lights.Single(a => a.Index == actor);
        var on = Player(save.TileX, save.TileY);
        var frame = Build(471, State(136, pit, 0x80), lights, on);
        var known = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects && t.Label.EndsWith(", save point"));
        Assert.Equal($"Glowing point {(char)('A' + actor - 9)}, save point", known.Label);
        Assert.Equal("Press Confirm to save.", known.ArrivalInstruction);
        Assert.Equal(4, frame.Targets.Count(t => t.Category == NavigationCategory.Objects));
        Assert.DoesNotContain(Build(471, State(136, pit), lights, on).Targets, t => t.Label.EndsWith(", save point"));
        Assert.DoesNotContain(Build(471, State(136, pit, 0x80), lights).Targets, t => t.Label.EndsWith(", save point"));
    }

    [Theory]
    [InlineData("hidden")] [InlineData("removed")] [InlineData("wrong-sprite")]
    public void UsedOrUndrawnLightsDoNotReappearThroughScriptRegions(string variant)
    {
        var lights = Lights(471).Select(a => a.Index == 9 ? variant switch {
            "hidden" => a with { DrawMode = 0 }, "removed" => a with { ClassTag = 0x84 },
            _ => a with { VisualIndex = 112 },
        } : a).ToArray();
        var frame = Build(471, State(136, 1), lights);
        Assert.DoesNotContain(frame.Targets, t => t.Label == "Glowing point A" || t.Id.StartsWith("script-region:9:"));
    }

    [Fact]
    public void TheTwoIdenticalOzzieRoomLightsHaveStableAppearanceLabels()
    {
        var frame = Build(174, State(137), Lights(174));
        Assert.Equal(new[] { "Glowing point A", "Glowing point B" }, frame.Targets.Where(t => t.Category == NavigationCategory.Objects).Select(t => t.Label).Order().ToArray());
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.Equal(2, frame.Targets.Count(t => t.Category == NavigationCategory.StoryEvents && !t.IsStoryNote));
        var router = new SceneConnectionRouter();
        Assert.NotNull(router.Distance(174, State(137), 172));
        var warp = GameNavigationCatalog.ForScene(174)!.Regions.First(r => r.Kind == "Warp");
        var privateTarget = new NavigationTarget(warp.Id, "Private warp role", NavigationCategory.Exits,
            new(12928, 13440, 1), [], true, true);
        Assert.Empty(router.Next(174, State(137), [172], [privateTarget]));
    }

    [Fact]
    public void UnopenedPitsAreNeitherTargetsNorSecretRouteTerminals()
    {
        var pits = new[] { (10, 50, 10), (11, 51, 16), (12, 57, 15), (13, 57, 12), (14, 60, 11), (15, 58, 8) };
        var actors = pits.Select(p => FullGameNavigationTests.Actor(p.Item1, p.Item2 * 256 + 128, p.Item3 * 256 + 255) with { ClassTag = 7, LoadedFlag = 0 }).ToArray();
        var state = State(136);
        var frame = Build(168, state, actors);
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Exits);
        foreach (var (_, x, y) in pits) Assert.False(frame.Graph.IsTerminal(new(x * 256 + 128, y * 256 + 128, 1)));
        var region = GameNavigationCatalog.ForScene(168)!.Regions.First(r => r.Kind == "Warp");
        var fake = new NavigationTarget(region.Id, "Hidden pit", NavigationCategory.Exits, new(12928, 2688, 1), [], true, true);
        Assert.Empty(new SceneConnectionRouter().Next(168, state, [471], [fake]));
    }

    [Theory]
    [InlineData(169, 9, 42)] [InlineData(171, 10, 46)] [InlineData(125, 9, 37)]
    public void VisibleDisguisesStayInPeopleUntilTheEnemySpriteIsDrawn(int scene, int actor, int visual)
    {
        var figure = FullGameNavigationTests.Actor(actor, 8 * 256 + 128, 10 * 256 + 255) with { VisualIndex = visual, ClassTag = 4 };
        var state = State(136);
        state = state with { Globals = new Dictionary<int, int>(state.Globals) { [0xA3] = 2 } };
        var frame = Build(scene, state, [figure]);
        Assert.Contains(frame.Targets, t => t.Category == NavigationCategory.People && t.ActorSlot == actor);
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Enemies);
        if (scene == 125)
        {
            var person = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.People);
            Assert.Null(person.ConfirmFacings);
            Assert.NotNull(person.ContactPosition);
        }
    }

    [Theory]
    [InlineData(471, 136, 4)] [InlineData(174, 137, 2)]
    public void OffCameraLightsRemainSelectableStoryChoices(int scene, int point, int count)
    {
        var frame = Build(scene, State(point), Lights(scene), viewport: new(7 * 256, 10 * 256, 24 * 256, 23 * 256));
        var choices = frame.Targets.Where(t => t.Category == NavigationCategory.StoryEvents && !t.IsStoryNote).ToArray();
        Assert.Equal(count, choices.Length);
        Assert.All(choices, t => Assert.False(t.Visible));
        foreach (var choice in choices)
        {
            var controller = new NavigationController();
            var selected = frame with { Targets = [choice] };
            for (var i = 0; i < (int)choice.Category; i++) controller.Handle(NavigationCommand.NextCategory, selected, 0);
            Assert.True(controller.Handle(NavigationCommand.Guide, selected, 1).Guiding);
        }
    }

    [Fact]
    public void StoryGuidanceRequiresTheNativeOzzieCorridorInteraction()
    {
        var state = State(136);
        var globals = new Dictionary<int, int>(state.Globals) { [0xA3] = 0x7E };
        var current = FullStoryObjectives.Build(170, state with { Globals = globals });
        var step = Assert.Single(current, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("full:keep-ozzie-corridor", step.Id);
        Assert.Contains(step.Goals, g => g.Scene == 170 && g.Actors.Contains(10));
        globals[0xAC] = 2;
        Assert.DoesNotContain(FullStoryObjectives.Build(170, state with { Globals = globals }), t => t.Id == step.Id);
    }

    [Theory]
    [InlineData(166, 9, 11, 9)] [InlineData(170, 8, 39, 61)] [InlineData(447, 8, 62, 9)]
    public void SingleLightRegionsRequireTheirDrawnSprite(int scene, int actor, int x, int y)
    {
        var light = FullGameNavigationTests.Actor(actor, x * 256 + 128, y * 256 + 255) with { ClassTag = 4, VisualIndex = 121, LoadedFlag = 0 };
        var controller = FullGameNavigationTests.Actor(10, 11 * 256 + 128, 9 * 256 + 255) with { ClassTag = 7, LoadedFlag = 0 };
        FieldActorSnapshot[] Actors(FieldActorSnapshot l) => scene == 166 ? [l, controller] : [l];
        var state = State(136);
        state = state with { Globals = new Dictionary<int, int>(state.Globals) { [0xA3] = 0x30 } };
        var shown = Build(scene, state, Actors(light));
        Assert.Single(shown.Targets, t => t.Label == "Glowing point" || t.Label.StartsWith("Return warp,"));
        Assert.DoesNotContain(shown.Targets, t => t.Category == NavigationCategory.Enemies);
        foreach (var changed in new[] { light with { DrawMode = 0 }, light with { VisualIndex = 120 } })
            Assert.DoesNotContain(Build(scene, state, Actors(changed)).Targets, t => t.Label == "Glowing point" || t.Label.StartsWith("Return warp,"));
    }

    private static FieldActorSnapshot[] Lights(int scene) => (scene == 471
        ? new[] { (9, 15, 7), (10, 30, 16), (11, 15, 29), (12, 1, 16) }
        : new[] { (8, 50, 52), (9, 56, 52) }).Select(p => FullGameNavigationTests.Actor(p.Item1,
            p.Item2 * 256 + 128, p.Item3 * 256 + 255) with { ClassTag = 4, VisualIndex = 121, LoadedFlag = 0 }).ToArray();
    private static FieldActorSnapshot Player(int x = 15, int y = 16) => FullGameNavigationTests.Actor(1, x * 256 + 128, y * 256 + 128, true) with { ClassTag = 0 };
    private static FieldStoryState State(int point, int pit = 1, int canSave = 0) => new(point, true) {
        Globals = Enumerable.Range(0, 512).ToDictionary(i => i, i => i == 0xD0 ? pit : i == 0x1CF ? canSave : 0),
        Locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0),
    };
    private static NavigationFrame Build(int scene, FieldStoryState state, FieldActorSnapshot[] actors, FieldActorSnapshot? player = null, FieldViewport? viewport = null)
    {
        player ??= Player();
        var field = FullGameNavigationTests.Field(scene) with { LeadPlayer = player, Actors = [player, .. actors] };
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, new(64, 64,
            new byte[4096], new byte[4096], Enumerable.Repeat((byte)1, 4096).ToArray(), 1, false,
            64, 64, Enumerable.Repeat((byte)128, 4096).ToArray()), viewport ?? new(0, 0, 16384, 16384), [], state);
    }
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
