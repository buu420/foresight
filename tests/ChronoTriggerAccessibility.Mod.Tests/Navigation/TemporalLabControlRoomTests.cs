using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class TemporalLabControlRoomTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InitialControlRoomReachesItsNativeDoorBeforeTheConsole(bool automatic)
    {
        var s = Sample(); var frame = TemporalLabPassageTests.Build(s.Field, s.Map, s.Story);
        var door = Assert.Single(frame.Targets, t => t.Id == "landmark:9");
        Assert.Contains("door", door.Label, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(door.ContactPosition);
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, door.ApproachPoints).Route);
        var target = Assert.Single(frame.Targets, t => t.Id == "story:full:lavos");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(route.Route); Assert.Equal("landmark:9", route.IntermediateId);
        Assert.NotNull(route.IntermediateContact);
        Assert.True(TemporalLabPassageTests.Start(frame, target.Id, automatic).Result.Guiding);
        var copy = GameNavigationCatalog.ActorInfo(620, s.Field.Actors.Single(a => a.Index == 9))!.Actions.First(a => a.Copy is not null).Copy!;
        var opened = TemporalLabPassageTests.Open(s.Map, copy);
        var next = TemporalLabPassageTests.Build(s.Field, opened,
            s.Story with { Locals = new Dictionary<int, int>(s.Story.Locals) { [100] = 1 } });
        var console = Assert.Single(next.Targets, t => t.Id == "landmark:8");
        Assert.Contains("console", console.Label, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(console.ConfirmFacings);
        Assert.NotNull(NavigationPathfinder.Search(next.Graph, next.Player, console.ApproachPoints).Route);
    }

    internal static (FieldNavigationSnapshot Field, FieldMapSnapshot Map, FieldStoryState Story) Sample()
    {
        using var doc = JsonDocument.Parse(typeof(TemporalLabControlRoomTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.temporal-lab-initial-0385.json")!);
        var map = doc.RootElement.GetProperty("rooms")[0].GetProperty("map").Deserialize<FieldMapSnapshot>()!;
        var template = TemporalLabPassageTests.Sample("capture-2-1872");
        // Initial native map, derived actor records from Atel0620 init0170/01C1.
        // The player pose is the native618 exit2 arrival; no620 live capture exists.
        var field = TemporalLabPassageTests.At(template.Field with { SceneId = 620, ActorCollisionRadius = 224 }, 7296, 15103);
        var marker = template.Field.Actors.Single(a => a.Index == 13);
        FieldActorSnapshot At(int slot, int x, int y, int pc, bool confirm) => marker with
        { Index = slot, TileX = x / 256, FineX = x, FractionX = x % 256, TileY = y / 256, FineY = y,
            FractionY = y % 256, ActivationBinding = 128, ActivationEnabled = confirm ? 1 : 0,
            ScriptAddress = pc, CurrentScriptOpcode = 0, ScriptCallsEnabled = true };
        field = field with { Actors = new[] { field.LeadPlayer!, At(8, 4104, 6144, 0x175, true), At(9, 4224, 13888, 0x1C6, false) } };
        return (field, map, template.Story with { Locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0) });
    }
}
