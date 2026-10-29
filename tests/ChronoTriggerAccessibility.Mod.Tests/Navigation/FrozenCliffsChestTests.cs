using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FrozenCliffsChestTests
{
    public static IEnumerable<object[]> Chests => new (int Scene, int Index, int X, int Y, string Letter)[]
    {
        (602,319,2944,15871,"A"), (602,320,2944,15871,"B"), (603,321,1664,7935,"A"),
        (605,322,2176,1023,"A"), (605,323,2176,1023,"B"), (605,324,3968,3327,"C"),
        (606,325,384,2815,"A"), (606,326,384,2815,"B"), (606,327,384,2815,"C"),
        (648,334,16000,6143,"A"), (648,335,16000,6143,"B"), (648,336,16000,6143,"C"),
    }.SelectMany(c => new[] { new object[] { c.Scene,c.Index,c.X,c.Y,c.Letter,false },
        new object[] { c.Scene,c.Index,c.X,c.Y,c.Letter,true } });

    [Theory]
    [MemberData(nameof(Chests))]
    public void EveryCliffChestHasADistinctLabelAndNativeInteractionRoute(int scene, int index, int x, int y, string letter, bool automatic)
    {
        var sample = FrozenCliffsNavigationTests.Sample(scene, pose: new(x,y,1));
        var chest = Assert.Single(sample.Frame.Targets, t => t.Id == "chest:" + index);
        Assert.Equal("Treasure chest " + letter, chest.Label);
        Assert.Null(chest.GuidanceRestriction);
        var route = NavigationPathfinder.Search(sample.Frame.Graph, sample.Frame.Player, chest.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.NotEmpty(chest.ConfirmFacings!(route.Route[^1]));
        Assert.True(FrozenCliffsNavigationTests.Start(sample.Frame, chest.Id, automatic).Guiding);
    }

    [Fact]
    public void OpeningAChestDoesNotRenumberTheOtherChests()
    {
        var sample = FrozenCliffsNavigationTests.Sample(605, pose:new(2176,1023,1));
        var remaining = sample.Treasures.Where(t => t.Index != 322).ToArray();
        var frame = FrozenCliffsNavigationTests.Build(sample.Field, sample.Map, sample.Story, remaining);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "chest:322");
        Assert.Equal("Treasure chest B", frame.Targets.Single(t => t.Id == "chest:323").Label);
    }

    [Fact]
    public void AnUpperLedgeDoesNotReceiveAnInventedLocalRoute()
    {
        var sample = FrozenCliffsNavigationTests.Sample(605,pose:new(6272,6399,1));
        // The native third cave lands below a one-way strip. The native guide
        // grid can identify its upper chests, but it cannot make an uphill path.
        var frame = new FieldNavigationSource(new Empty(),_=>{}).Build(sample.Field,sample.Map,
            new(5120,5120,9344,8320),sample.Treasures,sample.Story);
        foreach(var chest in frame.Targets.Where(t=>t.Id is "chest:322" or "chest:323"))
        {
            Assert.Null(NavigationPathfinder.Search(frame.Graph,frame.Player,chest.ApproachPoints).Route);
            var result=FrozenCliffsNavigationTests.Start(frame,chest.Id,true);
            Assert.False(result.Guiding);
            Assert.Contains(result.Speech,s=>s.Contains("No route",StringComparison.Ordinal));
        }
    }
    private sealed class Empty:ChronoTriggerAccessibility.Native.Memory.IReadableMemory
    {public bool TryRead(nuint address,Span<byte> destination)=>false;}
}
