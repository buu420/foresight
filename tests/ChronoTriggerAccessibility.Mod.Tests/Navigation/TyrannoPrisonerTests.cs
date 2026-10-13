using System.Runtime.CompilerServices;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Tyranno Lair interior (scene 302, Atel_0057). Actor 0 loops from 0x0284 on the
/// leader tile (opcode 22 to locals 8/9): X == 55 and Y in 19..25. Below story 0x96 the
/// crossing starts the Turnkey battle and sets 0x96. At exactly 0x96 with local 0x0A clear
/// it requests Kino's fn3 (actor 9, 0x053A: draw, walk to tile 49,19) and sets local 0x0A
/// (opcode 75; 1628FB writes the same A+118B0+index*8 cell that 12 reads at 1651A0).
/// Kino's fn1 (0x04D6) is Confirm only and ends with 5A 97.
/// Fixture: the tester's live route evidence at story 147 (16:05:02) and the same log's
/// actor facts right after BattleEnded (16:23:44, storyPoint=150).</summary>
public sealed class TyrannoPrisonerTests
{
    private static readonly Fixture Data = Load();
    private static readonly FieldViewport AfterBattleView = new(12288, 2816, 16384, 6400);

    [Fact]
    public void TheActualPreBattleCaptureOffersNoReveal()
    {
        Assert.Equal(147, Data.Story.Point);
        Assert.Empty(TyrannoLairTargets.Build(Data.Field(Data.Actors), Data.Map, Data.Story));
    }

    [Fact]
    public void AfterTheBattleTheRevealIsTheNativeColumnAndReachable()
    {
        var field = Data.Field(Data.AfterBattle);
        var reveal = Assert.Single(TyrannoLairTargets.Build(field, Data.Map, AfterBattle()));
        Assert.Equal(TyrannoLairTargets.RevealId, reveal.Id);
        Assert.NotEqual(NavigationCategory.People, reveal.Category);
        Assert.DoesNotContain("Kino", reveal.Label, StringComparison.OrdinalIgnoreCase);
        Assert.False(reveal.Visible);
        Assert.Null(reveal.ConfirmFacings);
        Assert.NotEmpty(reveal.ApproachPoints);
        Assert.All(reveal.ApproachPoints, p =>
        {
            Assert.Equal(55, p.X / 256);
            Assert.InRange(p.Y / 256, 19, 25);
        });
        var graph = new FieldNavigationGraph(Data.Map, new FieldActorCollisionRules(field));
        var lead = field.LeadPlayer!;
        Assert.NotNull(NavigationPathfinder.Search(graph, new(lead.FineX, lead.FineY, 1), reveal.ApproachPoints).Route);
    }

    public static TheoryData<string> Broken => new()
    {
        "scene", "incoherent", "story-0x95", "story-0x97", "local-0A-set", "local-0A-unknown", "no-control",
        "menu-input", "transition", "actor0-stopped", "actor0-elsewhere", "actor0-pc-unknown", "kino-drawn",
        "kino-class", "kino-visual", "kino-missing", "kino-ran",
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void TheRevealNeedsEveryNativeGate(string broken)
    {
        var actors = Data.AfterBattle.ToList();
        var story = AfterBattle();
        var map = Data.Map;
        void Actor(int index, Func<FieldActorSnapshot, FieldActorSnapshot?> change)
        {
            var at = actors.FindIndex(a => a.Index == index);
            if (change(actors[at]) is { } changed) actors[at] = changed; else actors.RemoveAt(at);
        }
        switch (broken)
        {
            case "story-0x95": story = story with { Point = 0x95 }; break;
            case "story-0x97": story = story with { Point = 0x97 }; break;
            case "local-0A-set": story = story with { Locals = new Dictionary<int, int>(story.Locals) { [0x0A] = 1 } }; break;
            case "local-0A-unknown": story = story with { Locals = story.Locals.Where(l => l.Key != 0x0A).ToDictionary() }; break;
            case "transition": map = map with { TransitionPending = true }; break;
            case "actor0-stopped": Actor(0, a => a with { ScriptProcessingEnabled = false }); break;
            case "actor0-elsewhere": Actor(0, a => a with { ScriptAddress = 0x02D9 }); break;
            case "actor0-pc-unknown": Actor(0, a => a with { ScriptAddress = null }); break;
            case "kino-drawn": Actor(9, a => a with { DrawMode = 1 }); break;
            case "kino-class": Actor(9, a => a with { ClassTag = 5 }); break;
            case "kino-visual": Actor(9, a => a with { VisualIndex = 7 }); break;
            case "kino-missing": Actor(9, _ => null); break;
            case "kino-ran": Actor(9, a => a with { ScriptAddress = 0x0539 }); break;
        }
        var field = Data.Field(actors) with
        {
            SceneId = broken == "scene" ? 430 : 302,
            SceneIdCoherent = broken != "incoherent",
            ControlFlag = broken == "no-control" ? 0 : 1,
            InputMode = broken == "menu-input" ? 1 : 0,
        };
        Assert.Empty(TyrannoLairTargets.Build(field, map, story));
    }

    [Fact]
    public void WhileTheCellIsDarkTheObjectiveUsesTheRevealAndNamesNoOne()
    {
        var field = Data.Field(Data.AfterBattle);
        var frame = Source().Build(field, Data.Map, AfterBattleView, [], AfterBattle());
        // The current frame alone keeps the prisoner unnamed and out of People.
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.People);
        Assert.DoesNotContain(frame.Targets, t => t.Label.Contains("Kino", StringComparison.OrdinalIgnoreCase));
        var reveal = TyrannoLairTargets.Build(field, Data.Map, AfterBattle());
        var lead = field.LeadPlayer!;
        var story = Assert.Single(new FullStoryTargets().Build(302, AfterBattle(), reveal, new(lead.FineX, lead.FineY, 1)));
        Assert.Equal("story:full:free-kino", story.Id);
        Assert.Equal(reveal[0].ApproachPoints.ToHashSet(), story.ApproachPoints.ToHashSet());
        Assert.Empty(story.ApproachConfirms ?? new Dictionary<NavigationPoint, Func<NavigationPoint, IReadOnlyList<NavigationDirection>>>());
        Assert.DoesNotContain("Kino", story.Label + story.Instruction, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void ManualAndAutomaticGuidanceReachTheReveal(NavigationCommand command)
    {
        var field = Data.Field(Data.AfterBattle);
        var lead = field.LeadPlayer!;
        var player = new NavigationPoint(lead.FineX, lead.FineY, 1);
        var story = Assert.Single(new FullStoryTargets().Build(302, AfterBattle(),
            TyrannoLairTargets.Build(field, Data.Map, AfterBattle()), player));
        var frame = new NavigationFrame("302", true, player, [story],
            new FieldNavigationGraph(Data.Map, new FieldActorCollisionRules(field)), NavigationUnits.LocalStep);
        var controller = new NavigationController();
        for (var n = 0; controller.TargetId != story.Id; n++)
        {
            Assert.True(n < 8);
            controller.Handle(NavigationCommand.NextCategory, frame, 0);
        }
        var result = controller.Handle(command, frame, 0);
        Assert.DoesNotContain(result.Speech, s => s.StartsWith("No route", StringComparison.Ordinal));
        Assert.True(result.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        // Manual guidance speaks the leg and never moves; automatic walking holds an input.
        if (command == NavigationCommand.Guide)
        {
            Assert.Equal(NavigationDirection.None, result.Direction);
            Assert.NotNull(result.ManualLeg);
        }
        else Assert.NotEqual(NavigationDirection.None, result.Direction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    public void OnceRevealedKinoIsAConfirmTargetAcrossTheBars(int scanGate)
    {
        // fn3 draws actor 9 and walks it to tile (49,19) (96 31 13); the fine point uses the
        // capture's own tile convention for this actor (tile 16 was 4351). Local 0x0A is set.
        // +20 is the camera cache 17D230 gates on; the hidden capture had 0.
        var actors = Data.AfterBattle.Select(a => a.Index == 9 ? a with
        {
            DrawMode = 1, FineY = 19 * 256 + 255, TileY = 19, ScriptAddress = 0x054A, ActivationBinding = scanGate,
        } : a).ToList();
        var field = Data.Field(actors);
        var story = AfterBattle() with { Locals = new Dictionary<int, int>(AfterBattle().Locals) { [0x0A] = 1 } };
        Assert.Empty(TyrannoLairTargets.Build(field, Data.Map, story));
        var frame = Source().Build(field, Data.Map, AfterBattleView, [], story);
        var kino = Assert.Single(frame.Targets, t => t.Id == "actor:9:4:6");
        Assert.Equal(NavigationCategory.People, kino.Category);
        Assert.Equal("Kino", kino.Label);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, kino.ApproachPoints).Route;
        Assert.NotNull(route);
        var stand = route![^1];
        // Row 20 is the cell wall: the standing point is on the room floor below it.
        Assert.Equal(21, stand.Y / 256);
        Assert.Contains(NavigationDirection.North, FieldInteractionRange.Facings(stand.X, stand.Y, 12672, 19 * 256 + 255));
        // Arrival only offers the facing; the player's own Confirm runs fn1 (0x04D6).
        if (scanGate == 0) Assert.True(kino.ConfirmPending!(stand));
        else Assert.Contains(NavigationDirection.North, kino.ConfirmFacings!(stand));
        var bound = Assert.Single(frame.Targets, t => t.Id == "story:full:free-kino");
        Assert.Equal(kino.ApproachPoints.ToHashSet(), bound.ApproachPoints.ToHashSet());
        Assert.NotEmpty(bound.ApproachConfirms!);
    }

    [Fact]
    public void BeforeTheBattleBreakingInBindsOnlyToActorZerosEncounterStrip()
    {
        // Actor 1's two trap strips (x 15 and x 29) are also live encounters at story 147, but in
        // the western section; the step that sets 0x96 is actor 0's column at x 55.
        var regions = GameNavigationCatalog.ForScene(302)!.Regions.Where(r => r.Kind == "Encounter" && r.Available(Data.Story)).ToArray();
        Assert.Equal([0, 1, 1], regions.Select(r => r.Actor).Order());
        var available = regions.Select(r => new NavigationTarget(r.Id, "Encounter", NavigationCategory.Enemies,
            new(r.Left * 256 + 128, r.Top * 256 + 128, 1), [new(r.Left * 256 + 128, r.Top * 256 + 128, 1)], false, true)).ToArray();
        var lead = Data.Field(Data.Actors).LeadPlayer!;
        var story = Assert.Single(new FullStoryTargets().Build(302, Data.Story, available, new(lead.FineX, lead.FineY, 1)));
        Assert.Equal("story:full:tyranno-lair", story.Id);
        Assert.All(story.ApproachPoints, p => Assert.Equal(55, p.X / 256));
    }

    private static FieldStoryState AfterBattle() => Data.Story with
    {
        Point = 150,
        Globals = new Dictionary<int, int>(Data.Story.Globals) { [0] = 150 },
    };

    private static FieldNavigationSource Source() => new(new NoMemory(), _ => { });

    private sealed record Fixture(JsonElement FieldJson, FieldStoryState Story, FieldMapSnapshot Map,
        FieldActorSnapshot[] Actors, FieldActorSnapshot[] AfterBattle)
    {
        public FieldNavigationSnapshot Field(IReadOnlyList<FieldActorSnapshot> actors)
        {
            var leader = FieldJson.GetProperty("leader").GetInt32();
            return new FieldNavigationSnapshot(0x10000, 0x20000, 0x30000, 0x40000, actors.Count,
                FieldJson.GetProperty("scene").GetInt32(), true, 1, 0, 0, leader, actors.Single(a => a.Index == leader),
                actors, actors.Where(a => a.IsPartyMember).Select(a => a.Index).ToArray())
            {
                LastPartySlotRaw = FieldJson.GetProperty("lastPartySlotRaw").GetInt32(),
                ActorCollisionRadius = FieldJson.GetProperty("collisionRadius").GetInt32(),
                ContactActorRaw = FieldJson.GetProperty("contactActorRaw").GetInt32(),
                ConfirmActorRaw = FieldJson.GetProperty("confirmActorRaw").GetInt32(),
            };
        }
    }

    private static Fixture Load([CallerFilePath] string file = "")
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "tyranno-302-0357.json")));
        var data = doc.RootElement; var s = data.GetProperty("Story");
        var story = new FieldStoryState(s.GetProperty("point").GetInt32(), true)
        {
            Globals = JsonSerializer.Deserialize<Dictionary<int, int>>(s.GetProperty("globals"))!,
            Locals = JsonSerializer.Deserialize<Dictionary<int, int>>(s.GetProperty("locals"))!,
            Extended = JsonSerializer.Deserialize<Dictionary<int, int>>(s.GetProperty("extended"))!,
        };
        return new(data.GetProperty("Field").Clone(), story, JsonSerializer.Deserialize<FieldMapSnapshot>(data.GetProperty("Map"))!,
            JsonSerializer.Deserialize<FieldActorSnapshot[]>(data.GetProperty("Actors"))!,
            JsonSerializer.Deserialize<FieldActorSnapshot[]>(data.GetProperty("AfterBattleActors"))!);
    }

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
