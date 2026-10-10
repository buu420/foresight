using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Whole-game checks over the installed scripts. field-content-coverage-0335.json holds
/// every actor whose Confirm or contact handler acts (1973 in 449 scenes), at its installed
/// initialisation position, as exported by
/// artifacts/research/content-coverage-0335/claude/export_content_fixture.py. The real
/// FieldNavigationSource must offer each one, or the fixture must say why the field layer does
/// not: battles belong to the encounter layer, story contact triggers and the two audited
/// pickups to the story providers that name them while their objective is current.
/// The route cases use installed terrain (field-content-maps-0335.json), not an open room, and
/// field-treasures-0335.json holds every treasure record with its installed map cell.</summary>
public sealed class FieldContentCoverageTests
{
    [Fact]
    public void InnkeeperKeepsItsPeopleDestinationDuringTheHostilePhase()
    {
        var row = Content.Actors.Single(r => r.Scene == 35 && r.Actor == 9);
        var actor = Snapshot(new(row.Actor, row.Class, row.Visual, row.X, row.Y));
        var state = new FieldStoryState(0x60, false)
        { Globals = new Dictionary<int, int> { [0x1A1] = 0, [0x1A2] = 0 } };
        var frame = Build(35, [actor], state, Open(row.X, row.Y));
        Assert.Contains(frame.Targets, t => t.Id == $"actor-encounter:{actor.Index}");
        var keeper = Assert.Single(frame.Targets, t => t.Id == Id(actor));
        Assert.Equal(NavigationCategory.People, keeper.Category);
        Assert.Equal("Innkeeper", keeper.Label);
    }

    [Fact]
    public void RescuedFritzCounterDoesNotRouteToTheFormerShopkeeper()
    {
        // Atel0017: Local06 moves actor8 away; counter11 now calls actor9 at1AA.
        var oldKeeper = Snapshot(new(8, 4, 22, 0x1AF0, 0x2580));
        var fritz = Snapshot(new(9, 4, 41, 0x1690, 0x2860));
        var counter = Snapshot(new(11, 4, 100, 0x1680, 0x28FF));
        var state = new FieldStoryState(0x40, false)
        { Globals = new Dictionary<int, int> { [0x142] = 0x10 }, Locals = new Dictionary<int, int> { [6] = 1 } };
        var map = Open(0x1C00, 0x2B00);
        var alone = Build(17, [oldKeeper, fritz], state, map);
        var together = Build(17, [oldKeeper, fritz, counter], state, map);
        Assert.Equal(alone.Targets.Single(t => t.Id == Id(oldKeeper)).ApproachPoints,
            together.Targets.Single(t => t.Id == Id(oldKeeper)).ApproachPoints);
        Assert.Contains(together.Targets.Single(t => t.Id == Id(fritz)).ApproachPoints,
            p => FieldInteractionRange.ReachesWithin(p.X, p.Y, counter.FineX, counter.FineY, 32));
    }

    [Theory]
    [InlineData(0x80, 0)]
    [InlineData(0, 0x10)]
    public void MedinaShopPhaseDoesNotBorrowTheFormerFightersCounter(int flagsA1, int flagsA2)
    {
        var state = new FieldStoryState(0x60, false)
        { Globals = new Dictionary<int, int> { [0x1A1] = flagsA1, [0x1A2] = flagsA2 } };
        Assert.Empty(FieldContentFacts.StandInsFor(39, 9, state));
        Assert.Empty(FieldContentFacts.StandInsFor(39, 9));
        Assert.Contains(FieldContentFacts.StandInsFor(39, 9, state with
        { Globals = new Dictionary<int, int> { [0x1A1] = 0, [0x1A2] = 0 } }), s => s.Actor == 8);
    }

    private sealed record Placed(int Actor, int Class, int Visual, int X, int Y);
    private sealed record Row(int Scene, int Actor, int Class, int Visual, int X, int Y);
    private sealed record Exclusion(int Scene, int Actor, string Reason);
    private sealed record StandInCase(int Scene, string Kind, Placed Owner, Placed StandIn);
    private sealed record GroupCase(int Scene, Placed[] Members);
    private sealed record Fixture(Row[] Actors, Exclusion[] Excluded, StandInCase[] StandIns, GroupCase[] Groups);

    private static readonly Fixture Content = Load<Fixture>("field-content-coverage-0335.json");
    private static readonly Dictionary<string, FieldMapSnapshot> Maps = Load<Dictionary<string, FieldMapSnapshot>>("field-content-maps-0335.json");
    private static readonly string[] GenericLabels = ["Person", "Creature", "Object", "Interactable scenery", "Item pickup"];
    private sealed record TreasureRecord(int Index, int X, int Y, int Shape, int Tile);
    private sealed record TreasureScene(int Scene, int Width, int Height, int First, int Last, TreasureRecord[] Records);
    private static readonly TreasureScene[] Treasures = Load<TreasureScene[]>("field-treasures-0335.json");

    [Fact]
    public void EveryScriptInteractiveActorIsOfferedOrAccountedFor()
    {
        Assert.Equal(1973, Content.Actors.Length + Content.Excluded.Length);
        Assert.Equal(449, Content.Actors.Select(r => r.Scene).Concat(Content.Excluded.Select(e => e.Scene)).Distinct().Count());
        Assert.Equal(new Dictionary<string, int> { ["encounter-only"] = 441, ["story-trigger"] = 4, ["audited-gate"] = 2, ["parked"] = 7, ["party-only"] = 1 },
            Content.Excluded.GroupBy(e => e.Reason).ToDictionary(g => g.Key, g => g.Count()));
        var missing = new List<string>();
        foreach (var row in Content.Actors)
        {
            var actor = Snapshot(new(row.Actor, row.Class, row.Visual, row.X, row.Y));
            var offered = false;
            foreach (var state in States(row.Scene, actor))
            {
                var frame = Build(row.Scene, [actor], state, Open(row.X, row.Y));
                // A battle the encounter layer offers under Enemies is offered too.
                if (frame.Targets.Any(t => t.Id == Id(actor) || t.Id == $"actor-encounter:{actor.Index}")) { offered = true; break; }
            }
            if (!offered) missing.Add($"{row.Scene}:{row.Actor}");
        }
        Assert.True(missing.Count == 0, "Not offered: " + string.Join(", ", missing));
    }

    [Fact]
    public void ShopAndInnKeepersAndCountersAreNamedForWhatTheyAre()
    {
        var generic = new List<string>();
        foreach (var row in Content.Actors.Where(r => FieldContentFacts.Service(r.Scene, r.Actor) is not null))
        {
            var actor = Snapshot(new(row.Actor, row.Class, row.Visual, row.X, row.Y));
            // Judge the keeper where trading is what Confirm does; states in which the
            // encounter layer has a battle available are that layer's to present.
            var info = GameNavigationCatalog.ActorInfo(row.Scene, actor);
            var target = States(row.Scene, actor).Select(state => WithoutEncounters(state, info))
                .Where(state => info?.Actions.Any(a => a.Kind == "Encounter" && a.Available(state)) != true)
                .Select(state => Build(row.Scene, [actor], state, Open(row.X, row.Y)).Targets
                .FirstOrDefault(t => t.Id == Id(actor))).FirstOrDefault(t => t is not null);
            if (target is null || GenericLabels.Contains(target.Label)) generic.Add($"{row.Scene}:{row.Actor}={target?.Label ?? "not offered"}");
        }
        Assert.True(generic.Count == 0, "Generic service labels: " + string.Join(", ", generic));
    }

    [Theory]
    [InlineData(183, "Sealed box")]
    [InlineData(151, "Time Gate")]
    public void SealedBoxesAndTimeGatesCarryTheirAppearance(int visual, string label)
    {
        var rows = Content.Actors.Where(r => r.Class == 4 && r.Visual == visual).ToArray();
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            var actor = Snapshot(new(row.Actor, row.Class, row.Visual, row.X, row.Y));
            var target = States(row.Scene, actor).Select(state => Build(row.Scene, [actor], state, Open(row.X, row.Y)).Targets
                .FirstOrDefault(t => t.Id == Id(actor))).First(t => t is not null)!;
            // Curated names ("Sealed box 1", Proto Dome's "Gate") keep precedence.
            Assert.True(target.Label.StartsWith(label, StringComparison.Ordinal) || target.Label == "Gate",
                $"{row.Scene}:{row.Actor} is \"{target.Label}\"");
        }
    }

    [Fact]
    public void EveryStandInExtendsItsKeeperAndIsNotOfferedTwice()
    {
        Assert.Equal(20, Content.StandIns.Length);
        foreach (var pair in Content.StandIns)
        {
            var owner = Snapshot(pair.Owner);
            var standIn = Snapshot(pair.StandIn);
            var state = States(pair.Scene, owner).First();
            if (pair.Kind == "Forward")
                state = pair.Scene == 17 ? state with
                {
                    Point = 0x40, Locals = new Dictionary<int, int> { [6] = pair.Owner.Actor == 8 ? 0 : 1 },
                    Globals = new Dictionary<int, int> { [0x142] = 0x10 },
                } : state with { Globals = new Dictionary<int, int> { [0x1A1] = 0, [0x1A2] = 0 } };
            var frame = Build(pair.Scene, [owner, standIn], state,
                Open(Math.Max(pair.Owner.X, pair.StandIn.X), Math.Max(pair.Owner.Y, pair.StandIn.Y)));
            // An owner the field layer does not offer in this state has its own coverage row.
            if (frame.Targets.SingleOrDefault(t => t.Id == Id(owner)) is not { } keeper) continue;
            // Somewhere to stand that reaches the counter is among the keeper's goals.
            Assert.Contains(keeper.ApproachPoints, p => FieldInteractionRange.ReachesWithin(p.X, p.Y, standIn.FineX, standIn.FineY, 32));
            var counter = frame.Targets.FirstOrDefault(t => t.Id == Id(standIn));
            if (pair.Kind == "Forward" || counter?.Category == NavigationCategory.People) continue;
            Assert.True(counter is null, $"{pair.Scene}: stand-in {pair.StandIn.Actor} is offered as \"{counter?.Label}\" beside its keeper");
        }
    }

    [Fact]
    public void RepeatedMarkersOfOneFeatureAreOneRow()
    {
        Assert.Equal(29, Content.Groups.Length);
        var merged = 0;
        foreach (var group in Content.Groups)
        {
            var members = group.Members.Select(Snapshot).ToArray();
            var state = States(group.Scene, members[0]).First();
            var map = Open(group.Members.Max(m => m.X), group.Members.Max(m => m.Y));
            // Markers the field layer does not offer on their own (battles, which the
            // encounter layer owns) have nothing to merge.
            if (!members.All(m => Build(group.Scene, [m], state, map).Targets.Any(t => t.Id == Id(m)))) continue;
            var rows = Build(group.Scene, members, state, map).Targets.Where(t => members.Any(m => t.Id == Id(m))).ToArray();
            var row = Assert.Single(rows);
            merged++;
            foreach (var member in members)
                Assert.Contains(row.ApproachPoints, p => Math.Abs(p.X - member.FineX) <= 512 && Math.Abs(p.Y - member.FineY) <= 512);
        }
        Assert.NotEqual(0, merged);
    }

    [Fact]
    public void EveryMarkerOfAMergedFeatureCanBeTheOneConfirmReaches()
    {
        // One row, goals from every marker. Next to one marker the scan (17D230) may pick its
        // higher-slot sibling; both run the same script, so each marker's native win must be
        // an accepted finish, not only the first marker's.
        var checkedGroups = 0;
        foreach (var group in Content.Groups)
        {
            // On camera, 17A4D0 -> 17A860 sets a class-7 marker's +0x20 byte to 0x80.
            var members = group.Members.Select(Snapshot).Select(m => m with { ActivationBinding = 0x80 }).ToArray();
            var state = States(group.Scene, members[0]).First();
            var map = Open(group.Members.Max(m => m.X), group.Members.Max(m => m.Y));
            if (!members.All(m => Build(group.Scene, [m], state, map).Targets.Any(t => t.Id == Id(m)))) continue;
            var frame = Build(group.Scene, members, state, map);
            var row = Assert.Single(frame.Targets, t => members.Any(m => t.Id == Id(m)));
            var player = FullGameNavigationTests.Actor(0, 384, 384, true) with { ClassTag = 0 };
            FieldActorSnapshot[] actors = [player, .. members];
            IReadOnlyList<NavigationDirection> Ready(NavigationPoint p) => row.ConfirmAt(p)?.Invoke(p) ?? [];
            // Every marker's own standing room finishes as ready.
            foreach (var member in members)
                Assert.True(row.ApproachPoints.Any(p => Math.Abs(p.X - member.FineX) <= 512 &&
                    Math.Abs(p.Y - member.FineY) <= 512 && Ready(p).Count != 0),
                    $"scene {group.Scene}: no Confirm-ready goal near marker {member.Index}");
            // And at every goal, any facing the scan gives to any of these markers is accepted; the
            // first marker's rule alone rejects a sibling's win. (Scene 241 places 12 and 13 on one
            // point, so 13, the higher slot, takes every Confirm there and 12 never does.)
            var facings = new[] { NavigationDirection.North, NavigationDirection.South, NavigationDirection.West, NavigationDirection.East };
            foreach (var goal in row.ApproachPoints)
            foreach (var facing in facings)
                if (members.Any(m => m.Index == FieldInteractionRange.ConfirmWinner(actors, 1, goal.X, goal.Y, facing)))
                    Assert.Contains(facing, Ready(goal));
            checkedGroups++;
        }
        Assert.NotEqual(0, checkedGroups);
    }

    [Theory]
    // Truce Inn (Atel counter marker 13: 12 10 00 00 08, 75 10, 02 10 11, 77 10, 00).
    [InlineData(12, 8, 22, 13200, 4960, 13, 13184, 5119, 52, 28, "Innkeeper")]
    // Porre Market: the counter marker runs a byte-identical copy of the clerk's C8 8C.
    [InlineData(54, 8, 22, 9856, 10592, 9, 9856, 11007, 38, 44, "Shopkeeper")]
    public void KeeperBehindACounterIsReachedFromTheCustomerSide(int scene, int keeper, int visual, int x, int y,
        int counter, int counterX, int counterY, int startX, int startY, string label)
    {
        var owner = Snapshot(new(keeper, 4, visual, x, y));
        var standIn = Snapshot(new(counter, 7, 0, counterX, counterY));
        var frame = Build(scene, [owner, standIn], States(scene, owner).First(), Maps[scene.ToString()], startX, startY);
        var target = Assert.Single(frame.Targets, t => t.Id == Id(owner));
        Assert.Equal(label, target.Label);
        Assert.DoesNotContain(frame.Targets, t => t.Id == Id(standIn));
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.True(FieldInteractionRange.ReachesWithin(route[^1].X, route[^1].Y, counterX, counterY, 32));
    }

    [Fact]
    public void ChorasBartenderIsReachedThroughAnyOfTheThreeCounterMarkers()
    {
        // Choras Tavern markers 9, 10 and 11 at (39..41, 12) each forward to actor 8.
        var owner = Snapshot(new(8, 4, 22, 41 * 256 + 128, 10 * 256 + 255));
        var counters = new[] { 39, 40, 41 }.Select((x, i) => Snapshot(new(9 + i, 7, 0, x * 256 + 128, 12 * 256 + 255))).ToArray();
        var frame = Build(188, [owner, .. counters], States(188, owner).First(), Maps["188"], 35, 20);
        var target = Assert.Single(frame.Targets, t => t.Id == Id(owner));
        Assert.All(counters, c => Assert.DoesNotContain(frame.Targets, t => t.Id == Id(c)));
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.Contains(counters, c => FieldInteractionRange.ReachesWithin(route[^1].X, route[^1].Y, c.FineX, c.FineY, 32));
    }

    [Fact]
    public void FerryOfficeCatStaysListedWhileItCarriesTheTicketSellersReach()
    {
        var seller = Snapshot(new(8, 4, 22, 0x0480, 0x3960));
        var cat = Snapshot(new(9, 4, 59, 4 * 256 + 128, 57 * 256 + 255));
        var frame = Build(18, [seller, cat], States(18, seller).First(), Maps["18"], 6, 60);
        var target = Assert.Single(frame.Targets, t => t.Id == Id(seller));
        Assert.Contains(frame.Targets, t => t.Id == Id(cat) && t.Category == NavigationCategory.People);
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
    }

    [Theory]
    // Guardia Forest (1000 AD) signposts are two markers each with the same handler.
    [InlineData(33, 22, 46, 34, 22, 45)]
    [InlineData(35, 16, 4, 36, 16, 3)]
    public void GuardiaForestTwoTileSignpostIsOneRoutedRow(int first, int x1, int y1, int second, int x2, int y2)
    {
        var a = Snapshot(new(first, 7, 0, x1 * 256 + 128, y1 * 256 + 255));
        var b = Snapshot(new(second, 7, 0, x2 * 256 + 128, y2 * 256 + 255));
        var frame = Build(19, [a, b], States(19, a).First(), Maps["19"], 19, 45);
        var row = Assert.Single(frame.Targets, t => t.Id == Id(a) || t.Id == Id(b));
        Assert.Equal(Id(a), row.Id);
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, row.ApproachPoints).Route);
    }

    [Fact]
    public void GuardiaForestSealedBoxIsNamedAndRouted()
    {
        // Atel0116 actor 62: 16 00 A5 04 gates the item; before that the box is sealed.
        var box = Snapshot(new(62, 4, 183, 47 * 256 + 128, 5 * 256 + 255));
        var frame = Build(119, [box], new FieldStoryState(0x40, false) { Globals = Enumerable.Range(1, 511).ToDictionary(i => i, _ => 0) },
            Maps["119"], 19, 45);
        var target = Assert.Single(frame.Targets, t => t.Id == Id(box));
        Assert.Equal("Sealed box", target.Label);
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
    }

    /// <summary>All 343 TakaraDataTbl records: 12 are alias or filler entries (x = y = 0), so
    /// 331 are treasure, resolved through 150 scenes. Native memory is laid out the way
    /// 0x179690 leaves it: grid at Engine+E44, range at FieldState+2190, layer-1 tiles at
    /// ActorBase+10FD0 and open bits at ActorBase+110B4.</summary>
    [Fact]
    public void EveryInstalledTreasureRecordIsReadAndOfferedUntilOpened()
    {
        Assert.Equal(150, Treasures.Length);
        Assert.Equal(331, Treasures.SelectMany(s => s.Records).Select(r => r.Index).Distinct().Count());
        var offered = new HashSet<int>();
        foreach (var scene in Treasures)
        {
            var inside = scene.Records.Where(r => r.X < scene.Width && r.Y < scene.Height).ToArray();
            var memory = new TreasureMemory(scene, inside);
            var shapes = new byte[scene.Width * scene.Height];
            foreach (var r in inside) shapes[r.Y * scene.Width + r.X] = (byte)r.Shape;
            var map = new FieldMapSnapshot(scene.Width, scene.Height, shapes, new byte[shapes.Length],
                Enumerable.Repeat((byte)1, shapes.Length).ToArray(), 1, false, scene.Width, scene.Height,
                Enumerable.Repeat((byte)128, shapes.Length).ToArray());
            var field = FullGameNavigationTests.Field(scene.Scene);
            Assert.True(FieldEnvironmentCapture.TryTreasures(memory, field, map, out var found, includeGuidePickups: true));
            Assert.Equal(inside.Select(r => r.Index).Order(), found.Select(t => t.Index).Order());
            foreach (var r in inside)
            {
                var treasure = Assert.Single(found, t => t.Index == r.Index);
                Assert.Equal((r.X * 256 + 128, r.Y * 256 + 128), (treasure.FineX, treasure.FineY));
                Assert.Equal((r.Shape & 1) != 0 && r.Tile is 0xFE or 0xEE or 0xE0 or 0xF0, treasure.IsChest);
            }
            var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map,
                new(0, 0, scene.Width * 256, scene.Height * 256), found, new FieldStoryState(10, false));
            foreach (var treasure in found)
                Assert.Contains(frame.Targets, t => t.Id == $"chest:{treasure.Index}" &&
                    t.Label == (treasure.IsChest ? "Treasure chest" : "Item pickup"));
            offered.UnionWith(found.Select(t => t.Index));
            memory.OpenAll();
            Assert.True(FieldEnvironmentCapture.TryTreasures(memory, field, map, out found, includeGuidePickups: true));
            Assert.Empty(found);
        }
        // Record 0 (47,47) lies outside scene 0's map, but scene 531 aliases the same table.
        Assert.Equal(331, offered.Count);
    }

    private sealed class TreasureMemory : IReadableMemory
    {
        private readonly byte[] bytes = new byte[0x40000];
        private readonly TreasureScene scene;
        public TreasureMemory(TreasureScene scene, TreasureRecord[] records)
        {
            this.scene = scene;
            var cells = scene.Width * scene.Height;
            Word(0x1E44, 0x20000); Word(0x1E48, 0x20000 + cells); Word(0x1E50, scene.Width); Word(0x1E54, scene.Height);
            Array.Fill(bytes, (byte)128, 0x20000, cells);
            Word(0x4190, scene.First); Word(0x4194, scene.Last);
            Word(0x14FD0, 0x30000); Word(0x14FDC, scene.Width);
            foreach (var r in records)
            {
                bytes[0x20000 + r.Y * scene.Width + r.X] = (byte)(r.Index - scene.First);
                bytes[0x30000 + r.Y * scene.Width + r.X] = (byte)r.Tile;
            }
        }
        public void OpenAll()
        {
            for (var index = scene.First; index < scene.Last; index++)
                bytes[0x150B4 + ((index >> 3) & 63) * 4] |= (byte)(1 << (index & 7));
        }
        private void Word(int address, int value) => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(address, 4), value);
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if ((ulong)address + (uint)destination.Length > (ulong)bytes.Length) return false;
            bytes.AsSpan((int)address, destination.Length).CopyTo(destination);
            return true;
        }
    }

    private static string Id(FieldActorSnapshot actor) =>
        actor.ClassTag == 7 ? $"landmark:{actor.Index}" : $"actor:{actor.Index}:{actor.ClassTag}:{actor.VisualIndex}";

    private static FieldActorSnapshot Snapshot(Placed p) =>
        FullGameNavigationTests.Actor(p.Actor, p.X, p.Y) with
        {
            ClassTag = p.Class, VisualIndex = p.Visual, DrawMode = p.Class == 7 ? 0 : 1,
            LoadedFlag = p.Class == 7 ? 0 : 1, ActivationEnabled = 0, ActivationBinding = 0,
        };

    /// <summary>The same state with one guard of each available battle made false, where the
    /// guard reads a story variable that can be set independently.</summary>
    private static FieldStoryState WithoutEncounters(FieldStoryState state, GameNavigationCatalog.Actor? info)
    {
        foreach (var guard in info?.Actions.Where(a => a.Kind == "Encounter" && a.Available(state))
                     .Select(a => a.Guards.FirstOrDefault(g => g.Source is "Global" or "Local" or "Extra" && !(g.Source == "Global" && g.Index == 0)))
                     .OfType<GameNavigationCatalog.Requirement>() ?? [])
        {
            if (Enumerable.Range(0, 256).FirstOrDefault(v => !guard.MatchesValue(v), -1) is not (>= 0 and var value)) continue;
            var cells = new Dictionary<int, int>(guard.Source switch
            {
                "Global" => state.Globals, "Local" => state.Locals, _ => state.Extended,
            }) { [guard.Index] = value };
            state = guard.Source switch
            {
                "Global" => state with { Globals = cells }, "Local" => state with { Locals = cells }, _ => state with { Extended = cells },
            };
        }
        return state;
    }

    /// <summary>States satisfying each catalogued action of the actor in turn, then none.</summary>
    private static IEnumerable<FieldStoryState> States(int scene, FieldActorSnapshot actor)
    {
        var info = GameNavigationCatalog.ActorInfo(scene, actor);
        foreach (var action in info?.Actions.OrderBy(a => a.Touch) ?? Enumerable.Empty<GameNavigationCatalog.Action>())
            if (Solve(action.Guards) is { } state) yield return state;
        yield return Solve([])!;
    }

    private static FieldStoryState? Solve(GameNavigationCatalog.Requirement[] guards)
    {
        var point = 3;
        var globals = Enumerable.Range(1, 511).ToDictionary(i => i, _ => 0);
        var locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0);
        var extended = Enumerable.Range(0, 80).ToDictionary(i => i, _ => 0);
        var inventory = new Dictionary<int, int>();
        var gold = 0;
        var active = new HashSet<int>(); var inactive = new HashSet<int>();
        var recruited = new HashSet<int>(); var absent = new HashSet<int>();
        foreach (var group in guards.Where(g => g.Source is not ("X" or "Y")).GroupBy(g => (g.Source, g.Index)))
        {
            var (source, index) = group.Key;
            IEnumerable<int> values = source switch
            {
                "Global" when index == 0 => Enumerable.Range(3, 253).Concat(Enumerable.Range(0, 3)),
                "Item" => Enumerable.Range(0, 100),
                "Gold" => group.SelectMany(g => new[] { 0, g.Value - 1, g.Value, g.Value + 1, 9_999_999 }).Where(v => v >= 0),
                "ActiveParty" or "Recruited" => [0, 1],
                _ => Enumerable.Range(0, 256),
            };
            int? chosen = null;
            foreach (var v in values) if (group.All(g => g.MatchesValue(v))) { chosen = v; break; }
            if (chosen is not { } value) return null;
            switch (source)
            {
                case "Global" when index == 0: point = value; break;
                case "Global": globals[index] = value; break;
                case "Local": locals[index] = value; break;
                case "Extra": extended[index] = value; break;
                case "Item": inventory[index] = value; break;
                case "Gold": gold = value; break;
                case "ActiveParty": (value == 1 ? active : inactive).Add(index); break;
                case "Recruited": (value == 1 ? recruited : absent).Add(index); break;
                default: return null;
            }
        }
        if (active.Count > 3 || active.Overlaps(inactive) || active.Overlaps(absent) || recruited.Overlaps(absent)) return null;
        var first = active.ToList();
        foreach (var c in Enumerable.Range(0, 7))
            if (first.Count < 3 && !first.Contains(c) && !inactive.Contains(c) && !absent.Contains(c)) first.Add(c);
        var party = first.Concat(Enumerable.Range(0, 7).Where(c => !first.Contains(c) && !absent.Contains(c))).ToList();
        if (recruited.Any(c => !party.Contains(c))) return null;
        while (party.Count < 9) party.Add(0x80);
        return new FieldStoryState(point, true)
        { Globals = globals, Locals = locals, Extended = extended, Inventory = inventory, Gold = gold, Party = party };
    }

    /// <summary>An open room large enough for the placed actors.</summary>
    private static FieldMapSnapshot Open(int x, int y)
    {
        var width = Math.Max(16, x / 256 + 2);
        var height = Math.Max(16, y / 256 + 2);
        var cells = width * height;
        return new(width, height, new byte[cells], new byte[cells], Enumerable.Repeat((byte)1, cells).ToArray(), 1, false,
            width, height, Enumerable.Repeat((byte)128, cells).ToArray());
    }

    private static NavigationFrame Build(int scene, FieldActorSnapshot[] actors, FieldStoryState state, FieldMapSnapshot map,
        int startX = 1, int startY = 1)
    {
        var player = FullGameNavigationTests.Actor(0, startX * 256 + 128, startY * 256 + 128, true) with { ClassTag = 0 };
        var field = FullGameNavigationTests.Field(scene) with { LeadPlayer = player, Actors = [player, .. actors], ActorCount = actors.Length + 1 };
        return new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, map, new(0, 0, map.Width * 256, map.Height * 256), [], state);
    }

    private static T Load<T>(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            $"ChronoTriggerAccessibility.Mod.Tests.Navigation.{name}")!;
        return JsonSerializer.Deserialize<T>(stream)!;
    }

    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
