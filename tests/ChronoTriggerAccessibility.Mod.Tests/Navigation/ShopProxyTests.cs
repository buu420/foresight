using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Truce Market, scene 118. Confirming the counter is how the player reaches the
/// shopkeeper: actor 9's whole activate handler is <c>02 10 11</c>, a call to actor 8
/// function 1, and actor 8's handler is the shop itself (<c>C8 81</c> between its two
/// lines). The clerk's own sprite is behind a solid bench and out of the handler's range
/// from every tile, so the counter is where a Shopkeeper destination has to lead.
/// Decode: artifacts/research/shop-enemies-0325/navigation/proxies.py.</summary>
public sealed class ShopProxyTests
{
    private const int Scene = 118;

    [Fact]
    public void ChoosingTheShopkeeperRoutesToTheCounterThatRunsTheirScript()
    {
        var frame = Build();
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:8:4:49");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.True(FieldInteractionRange.ReachesWithin(route[^1].X, route[^1].Y, 14336, 2032, 32),
            $"route ended at {route[^1]}, out of confirm range of the counter");
    }

    [Fact]
    public void TheCounterIsNotAlsoOfferedAsItsOwnAnonymousTarget()
    {
        Assert.DoesNotContain(Build().Targets, t => t.Id == "actor:9:4:100");
    }

    [Fact]
    public void TheShopkeeperKeepsTheirOwnLabelAndCategory()
    {
        var target = Assert.Single(Build().Targets, t => t.Id == "actor:8:4:49");
        Assert.Equal(NavigationCategory.People, target.Category);
        Assert.Equal("Shopkeeper", target.Label);
    }

    [Fact]
    public void AMissingProxyLeavesTheShopkeeperWithNoRouteRatherThanAnInventedOne()
    {
        var frame = Build(omitCounter: true);
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:8:4:49");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
    }

    [Fact]
    public void AnUndrawnProxyIsNotUsed()
    {
        var route = RouteToShopkeeper(FullGameNavigationTests.Actor(9, 14336, 2032)
            with { VisualIndex = 100, DrawMode = 0 });
        Assert.Null(route);
    }

    [Fact]
    public void AnActorWithTheWrongVisualInTheProxySlotIsNotUsed()
    {
        var route = RouteToShopkeeper(FullGameNavigationTests.Actor(9, 14336, 2032)
            with { VisualIndex = 7 });
        Assert.Null(route);
    }

    [Fact]
    public void AProxyWhoseNativeScriptCallsAreDisabledIsNotUsed()
    {
        Assert.Null(RouteToShopkeeper(FullGameNavigationTests.Actor(9, 14336, 2032)
            with { VisualIndex = 100, ScriptCallsEnabled = false }));
    }

    [Fact]
    public void ADisabledShopkeeperCannotBorrowTheCounter()
    {
        var frame = Build(keeperCalls: false);
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:8:4:49");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
    }

    [Fact]
    public void AnUnrelatedActorInTheProxySlotIsStillListed()
    {
        var frame = Build(FullGameNavigationTests.Actor(9, 14336, 2032) with { VisualIndex = 7 });
        Assert.Contains(frame.Targets, t => t.Id == "actor:9:4:7");
    }

    [Fact]
    public void TheSameActorIndexInAnotherSceneIsNotAProxy()
    {
        // Scene 119 has no such binding; actor 9 there must not lend its position.
        var player = FullGameNavigationTests.Actor(1, 15488, 2879) with { IsPartyMember = true, ClassTag = 0 };
        var keeper = FullGameNavigationTests.Actor(8, 14336, 1888) with { VisualIndex = 49 };
        var counter = FullGameNavigationTests.Actor(9, 14336, 2032) with { VisualIndex = 100 };
        var field = FullGameNavigationTests.Field(119) with
            { LeadPlayer = player, Actors = [player, keeper, counter] };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, CounterApproachTests.Market(), new(12288, 768, 16512, 3968), [],
                new FieldStoryState(18, false));
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:8:4:49");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
    }

    private static IReadOnlyList<NavigationPoint>? RouteToShopkeeper(FieldActorSnapshot? counter)
    {
        var frame = Build(counter);
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:8:4:49");
        return NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
    }

    private static NavigationFrame Build(FieldActorSnapshot? counter = null, bool omitCounter = false, bool keeperCalls = true)
    {
        var player = FullGameNavigationTests.Actor(1, 15488, 2879) with { IsPartyMember = true, ClassTag = 0 };
        var keeper = FullGameNavigationTests.Actor(8, 14336, 1888) with { VisualIndex = 49, ScriptCallsEnabled = keeperCalls };
        if (!omitCounter)
            counter ??= FullGameNavigationTests.Actor(9, 14336, 2032) with { VisualIndex = 100 };
        FieldActorSnapshot[] actors = counter is null ? [player, keeper] : [player, keeper, counter];
        var field = FullGameNavigationTests.Field(Scene) with { LeadPlayer = player, Actors = actors };
        return new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, CounterApproachTests.Market(), new(12288, 768, 16512, 3968), [],
                new FieldStoryState(18, false));
    }

    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
