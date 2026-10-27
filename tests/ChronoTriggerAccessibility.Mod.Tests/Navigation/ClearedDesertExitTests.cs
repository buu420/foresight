using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class ClearedDesertExitTests
{
    // Native initial room161 planes, exit263 and all treasure collision records.
    // Player positions and committed forces are from the supplied0.3.77 log.
    // All chests opened, no actors/encounters and a full viewport are explicit
    // offline assumptions. This does not claim a captured live room snapshot.
    [Theory]
    [InlineData(1914,2935,0,-16,0x600u)]
    [InlineData(1420,2948,16,0,0xA00u)]
    [InlineData(1421,2903,16,0,0xA00u)]
    public void ExitWalkingCountersTheLoggedSidewaysPush(int x,int y,int fx,int fy,uint expected)
    {
        var frame=Frame(x,y);
        Assert.DoesNotContain(frame.Targets,t=>t.Id.StartsWith("chest:"));
        var (controller,result)=Start(frame,true);
        Assert.True(result.AutoWalking);
        Assert.Equal("exit:0",controller.TargetId);
        Assert.NotNull(result.SteeringPoint);
        var pad=FieldFloorMovement.Adjust(frame,result,new(0x1000,0x4000,161,6,fx,fy,0,1));
        Assert.Null(pad.StopReason);
        Assert.Equal(expected,pad.Pad);
    }

    [Theory]
    [InlineData(1914,2935,NavigationDirection.SouthWest)]
    [InlineData(1420,2948,NavigationDirection.NorthWest)]
    [InlineData(1392,2856,NavigationDirection.North)]
    public void ManualExitGuidanceDescribesTheSidewaysCorrection(int x,int y,NavigationDirection expected)
    {
        var (_,result)=Start(Frame(x,y),false);
        Assert.True(result.Guiding);
        Assert.False(result.AutoWalking);
        Assert.Equal(NavigationDirection.None,result.Direction);
        Assert.Equal(expected,result.ManualLeg!.Direction);
        if(expected is NavigationDirection.NorthWest or NavigationDirection.SouthWest)
            Assert.Contains(result.Speech,s=>s.Contains("drift",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExitArrivalDoesNotAcquireTheChestConfirmOrHoldingPolicy()
    {
        var frame=Frame(1420,2948);
        var exit=frame.Targets.Single(t=>t.Id=="exit:0");
        Assert.False(exit.KeepInteractionReach);
        Assert.Null(exit.ConfirmFacings);
        var (controller,_)=Start(frame,true);
        var arrived=controller.Update(frame with{Player=new(1264,1520,1)},16);
        Assert.False(arrived.AutoWalking);
        Assert.False(controller.IsHolding);
        Assert.Contains(arrived.Speech,s=>s.StartsWith("Arrived at",StringComparison.Ordinal));
        Assert.DoesNotContain(arrived.Speech,s=>s.Contains("Confirm",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnUnrelatedSceneDoesNotAcquireTheDesertSteeringPolicy()
    {
        var (controller,result)=Start(Frame(1914,2935,232),true);
        Assert.True(result.AutoWalking);
        Assert.Equal("exit:0",controller.TargetId);
        Assert.Null(result.SteeringPoint);
    }

    private static (NavigationController,NavigationResult) Start(NavigationFrame frame,bool automatic)
    {
        var controller=new NavigationController();
        // Keep the captured exit as the only selection; category browsing is
        // covered separately and is not the failure under investigation.
        frame=frame with{Targets=frame.Targets.Where(t=>t.Id=="exit:0").ToArray()};
        controller.Handle(NavigationCommand.NextCategory,frame,0);
        Assert.Equal("exit:0",controller.TargetId);
        var result=controller.Handle(automatic?NavigationCommand.ToggleWalk:NavigationCommand.Guide,frame,0);
        return (controller,result);
    }

    internal static NavigationFrame Frame(int x,int y,int scene=161)
    {
        using var doc=JsonDocument.Parse(typeof(ClearedDesertExitTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.sunken-desert-initial-0374.json")!);
        var row=doc.RootElement.GetProperty("scenes").EnumerateArray().Single(e=>e.GetProperty("scene").GetInt32()==161);
        var w=row.GetProperty("width").GetInt32();var h=row.GetProperty("height").GetInt32();
        byte[] Bytes(string key)=>Convert.FromBase64String(row.GetProperty(key).GetString()!);
        var exits=Enumerable.Repeat((byte)128,w*h).ToArray();
        exits[4*w+4]=exits[5*w+4]=0;
        var map=new FieldMapSnapshot(w,h,Bytes("shapes"),Bytes("flags"),Bytes("layers"),1,false,w,h,exits);
        var grid=row.GetProperty("chests").EnumerateArray().Select(e=>(e.GetProperty("x").GetInt32(),e.GetProperty("y").GetInt32())).ToHashSet();
        var actor=new FieldActorSnapshot(6,x/256,x,x&255,y/256,y,y&255,1,1,1,0,0,0x2020,0,128,true,true,true,true);
        var field=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,21,scene,true,1,0,6,6,actor,[actor]);
        return new FieldNavigationSource(new NoMemory(),_=>{}).Build(field,map,new(0,0,w*256,h*256),[],
            new(214,true){Globals=new Dictionary<int,int>{{0x1A3,1}}},(a,b)=>grid.Contains((a,b)));
    }
    private sealed class NoMemory:IReadableMemory{public bool TryRead(nuint a,Span<byte>b)=>false;}
}
