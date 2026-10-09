using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class IokaPeopleTests
{
    [Theory]
    [InlineData(3, 1, 1, "Amy")]
    [InlineData(4, 2, 2, "Jen")]
    [InlineData(6, 3, 5, "Ria")]
    public void PlayableCharactersActingAsNpcsUseTheirCurrentNames(int index, int type, int visual, string name)
    {
        var actor = Actor(index, 2176, 2559) with { ClassTag = type, VisualIndex = visual, IsPartyMember = type < 3 };
        var source = new FieldNavigationSource(new NoMemory(), _ => { }, characterName: c => c == visual ? name : null);
        var target = Assert.Single(Build(280, [actor], source).Targets, t => t.Id == $"actor:{index}:{type}:{visual}");
        Assert.Equal(name, target.Label);
    }

    [Fact]
    public void AnOrdinaryNpcVisualIndexIsNeverUsedAsACharacterIdentity()
    {
        var actor = Actor(15, 2176, 2559) with { VisualIndex = 6 };
        var source = new FieldNavigationSource(new NoMemory(), _ => { }, characterName: _ => "Magus");
        var target = Assert.Single(Build(280, [actor], source).Targets, t => t.Id == "actor:15:4:6");
        Assert.Equal("Blond person in blue", target.Label);
    }

    [Theory]
    [InlineData(3, 1, 1, 2176)]
    [InlineData(4, 2, 2, 2688)]
    public void FeastCompanionsWithNativeTalkHandlersAreSelectable(int index, int type, int visual, int x)
    {
        // 2026-10-09 tester capture, scene 280: these are active party slots,
        // with distinct native function-1 dialogue handlers and binding 0x80.
        var actor = Actor(index, x, 2559) with { ClassTag = type, VisualIndex = visual, IsPartyMember = true };
        var frame = Build(280, [actor]);
        var target = Assert.Single(frame.Targets, t => t.Id == $"actor:{index}:{type}:{visual}");
        Assert.Equal(NavigationCategory.People, target.Category);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.NotEmpty(target.ConfirmFacings!(route[^1]));
    }

    [Fact]
    public void OrdinaryFollowersAndTheLeaderAreNotPeopleTargets()
    {
        var follower = Actor(3, 2176, 2559) with { ClassTag = 1, VisualIndex = 1, IsPartyMember = true };
        Assert.DoesNotContain(Build(279, [follower]).Targets, t => t.Category == NavigationCategory.People);
        Assert.DoesNotContain(Build(280, []).Targets, t => t.Category == NavigationCategory.People);
    }

    [Theory]
    [InlineData(true, 0x12)]
    [InlineData(false, 0xAB)]
    [InlineData(true, 0xB0)]
    public void ASecondCompanionCannotStealConfirmAtThePlannedArrival(bool calls, int opcode)
    {
        var marle = Actor(3, 2176, 2559) with { ClassTag = 1, VisualIndex = 1, IsPartyMember = true };
        var lucca = Actor(4, 2176, 2431) with
        {
            ClassTag = 2, VisualIndex = 2, IsPartyMember = true,
            ScriptCallsEnabled = calls, CurrentScriptOpcode = opcode,
        };
        var frame = Build(280, [marle, lucca]);
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:3:1:1");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.NotEmpty(target.ConfirmFacings!(route[^1]));
    }

    [Theory]
    [InlineData(false, 128, true, 1)]
    [InlineData(true, 128, false, 1)]
    [InlineData(true, 128, true, 0)]
    public void FeastCompanionsRequireCoherentSceneProcessingAndVisibleSprite(
        bool coherent, int binding, bool processing, int draw)
    {
        var actor = Actor(3, 2176, 2559) with
        {
            ClassTag = 1, VisualIndex = 1, IsPartyMember = true,
            ActivationBinding = binding, ScriptProcessingEnabled = processing, DrawMode = draw,
        };
        Assert.DoesNotContain(Build(280, [actor], coherent: coherent).Targets, t => t.Id == "actor:3:1:1");
    }

    [Fact]
    public void LuccaStaysListedWhileHerIdleAnimationDisablesCallsAndConfirmWaits()
    {
        // Atel_0371:0579 disables calls while 057A (AB 24) waits, then 057C
        // enables them again. The visible companion remains a destination.
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var lucca = Actor(4, 2688, 2559) with { ClassTag = 2, VisualIndex = 2, IsPartyMember = true };
        var original = Assert.Single(Build(280, [lucca], source).Targets, t => t.Id == "actor:4:2:2");
        var frame = Build(280, [lucca with { ScriptCallsEnabled = false, CurrentScriptOpcode = 0xAB }], source);
        var target = Assert.Single(frame.Targets, t => t.Id == original.Id);
        Assert.Equal(original.Label, target.Label);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.All(target.ApproachPoints, p => Assert.Empty(target.ConfirmFacings!(p)));
        Assert.All(target.ApproachPoints, p => Assert.True(target.ConfirmPending!(p)));
    }

    [Theory]
    [InlineData(0xB0)]
    [InlineData(null)]
    public void APartyFollowerOrUnknownCurrentScriptIsNotAnNpcDestination(int? opcode)
    {
        var actor = Actor(3, 2176, 2559) with
        {
            ClassTag = 1, VisualIndex = 1, IsPartyMember = true, CurrentScriptOpcode = opcode,
        };
        Assert.DoesNotContain(Build(280, [actor]).Targets, t => t.Id == "actor:3:1:1");
    }

    [Fact]
    public void CameraCullingDoesNotRemoveADiscoveredCompanionButConfirmStillWaitsForTheScanGate()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var actor = Actor(3, 2176, 2559) with { ClassTag = 1, VisualIndex = 1, IsPartyMember = true };
        Build(280, [actor], source);
        var frame = Build(280, [actor with { ActivationBinding = 0 }], source);
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:3:1:1");
        Assert.NotEmpty(target.ApproachPoints);
        Assert.All(target.ApproachPoints, p => Assert.Empty(target.ConfirmFacings!(p)));
    }

    [Fact]
    public void FeastVillagersUseVerifiedAppearancesAndDistinctLabels()
    {
        var frame = Build(280,
        [
            Actor(9, 4736, 4863) with { VisualIndex = 31 },
            Actor(10, 2432, 4863) with { VisualIndex = 31 },
            Actor(11, 2944, 5631) with { VisualIndex = 30 },
            Actor(12, 4224, 5631) with { VisualIndex = 30 },
        ]);
        var labels = frame.Targets.Where(t => t.Category == NavigationCategory.People).ToDictionary(t => t.Id, t => t.Label);
        Assert.Equal("Long-haired person in green A", labels["actor:9:4:31"]);
        Assert.Equal("Long-haired person in green B", labels["actor:10:4:31"]);
        Assert.Equal("Short-haired person in green A", labels["actor:11:4:30"]);
        Assert.Equal("Short-haired person in green B", labels["actor:12:4:30"]);
    }

    [Fact]
    public void RepeatedAppearanceAliasesSurviveMovementDisappearanceAndInputOrder()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var a = Actor(9, 2432, 4863) with { VisualIndex = 31 };
        var b = Actor(10, 4736, 4863) with { VisualIndex = 31 };
        var first = Build(280, [b, a], source);
        var original = Assert.Single(first.Targets, t => t.Id == "actor:10:4:31").Label;
        Assert.Equal("Long-haired person in green B", original);
        var next = Build(280, [b with { FineX = 4608 }], source);
        Assert.Equal(original, Assert.Single(next.Targets, t => t.Id == "actor:10:4:31").Label);
    }

    internal static NavigationFrame Build(int scene, FieldActorSnapshot[] actors,
        FieldNavigationSource? source = null, bool coherent = true)
    {
        var player = Actor(2, 4736, 2559) with { ClassTag = 0, VisualIndex = 0, IsPartyMember = true };
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000,
            23, scene, coherent, 1, 0, 1, 2, player, new[] { player }.Concat(actors).ToArray());
        var map = new FieldMapSnapshot(32, 32, new byte[1024], new byte[1024],
            Enumerable.Repeat((byte)1, 1024).ToArray(), 1, false, 32, 32,
            Enumerable.Repeat((byte)128, 1024).ToArray());
        if (scene == 280)
        {
            using var stream = typeof(IokaPeopleTests).Assembly.GetManifestResourceStream(
                "ChronoTriggerAccessibility.Mod.Tests.Navigation.ioka-280-map-0350.json")!;
            using var json = JsonDocument.Parse(stream);
            var m = json.RootElement.GetProperty("Map");
            byte[] Plane(string name) => Convert.FromBase64String(m.GetProperty(name).GetString()!);
            map = new(m.GetProperty("Width").GetInt32(), m.GetProperty("Height").GetInt32(),
                Plane("CollisionShapes"), Plane("TerrainFlags"), Plane("CollisionLayers"), 1, false,
                m.GetProperty("ExitWidth").GetInt32(), m.GetProperty("ExitHeight").GetInt32(), Plane("ExitCells"));
        }
        return (source ?? new(new NoMemory(), _ => { })).Build(field, map,
            new(0, 0, 8192, 8192), [], new FieldStoryState(111, false));
    }

    internal static FieldActorSnapshot Actor(int index, int x, int y) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 1, 1, 1, 0, 4, 0x2020,
            0, 128, false, true, true, true) { CurrentScriptOpcode = 0x12 };
    internal sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
