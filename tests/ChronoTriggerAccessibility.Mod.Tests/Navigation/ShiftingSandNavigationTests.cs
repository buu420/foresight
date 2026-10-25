using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class ShiftingSandNavigationTests
{
    [Theory]
    [InlineData(160,64)] [InlineData(161,68)]
    public void RealRoomChestsRemainNativeConfirmTargetsAfterAutomaticArrival(int scene,int id)
    {
        var frame=Frame(scene);var target=frame.Targets.Single(t=>t.Id=="chest:"+id);
        Assert.True(target.KeepInteractionReach);
        Assert.False(target.FollowUntilInteraction);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.Contains("sand",target.Instruction!,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Confirm",target.ArrivalAnnouncement!);
    }

    [Fact]
    public void AForeignOrIncoherentSceneCannotAcquireTheSandInteractionPolicy()
    {
        Assert.All(Frame(160,identityScene:158).Targets,t=>Assert.False(t.KeepInteractionReach));
        Assert.All(Frame(160,coherent:false).Targets,t=>Assert.False(t.KeepInteractionReach));
    }

    [Fact]
    public void NativeTwoRowContactIsAvailableAndStableGoalsRemainOnlyAPreference()
    {
        var target=Frame(160).Targets.Single(t=>t.Id=="chest:65");
        Assert.Contains(target.ApproachPoints,p=>p.X/256==19 && p.Y/256==10);
        Assert.Contains(target.PreferredApproachPoints,p=>p.X/256==19 && p.Y/256==10);
        Assert.True(target.ApproachPoints.Count>target.PreferredApproachPoints.Count);
    }

    [Fact]
    public void SandRoutesRespectTheNativeTreasureGridInTheirBodyProbes()
    {
        var frame=Frame(161);var graph=frame.Graph;
        // The leading corner enters chest72's tile before the foot does.
        var from=new NavigationPoint(2400,6976,1);
        Assert.DoesNotContain(new NavigationPoint(2400,7040,1),graph.Neighbours(from));
        Assert.DoesNotContain(new NavigationPoint(2400,7040,1),graph.ForGoals([new(2400,7040,1)]).Neighbours(from));
    }

    [Fact]
    public void PerpendicularSandUsesOrdinaryDiagonalSteeringTowardTheRoute()
    {
        var frame=SimpleFrame() with{Player=new(176,128,1)};
        var result=new NavigationResult([],NavigationDirection.North,true,true){SteeringPoint=new(128,0,1)};
        var pad=FieldFloorMovement.Adjust(frame,result,Floor(1,2));
        Assert.Equal(0xA08u,pad.Pad); // Run up-left against eastward drift.
    }

    [Fact]
    public void StartingAfterMotionResetRecapturesTheCurrentFloorBeforeApplyingRun()
    {
        var keys=new HashSet<int>();long now=0;var speech=new List<string>();
        FieldFloorSnapshot? floor=null;var observations=0;
        var runtime=new FieldNavigationRuntime(_=>{floor=null;return SimpleFrame();},new(keys.Contains,()=>true),
            ()=>true,()=>now,speech.Add,_=>{},observeFieldInput:_=>{observations++;floor=Floor(1,2);},
            fieldFloor:()=>floor,adjustFieldInput:(f,r)=>FieldFloorMovement.Adjust(f,r,floor));
        runtime.Enable();runtime.OnInput(0x1000,0);now+=16;keys.Add('P');
        var pad=runtime.OnInput(0x1000,0);
        Assert.Equal(0x108u,pad);Assert.True(observations>=3);
        Assert.DoesNotContain(speech,s=>s.Contains("unavailable"));
    }

    [Fact]
    public void HeldDashThatNativelySelectsWalkingIsExplainedWithoutChangingThePlayersInput()
    {
        var keys=new HashSet<int>();long now=0;var speech=new List<string>();
        var floor=Floor(0,1);
        var runtime=new FieldNavigationRuntime(_=>SimpleFrame(),new(keys.Contains,()=>true),()=>true,()=>now,speech.Add,_=>{},
            fieldFloor:()=>floor,adjustFieldInput:(f,r)=>FieldFloorMovement.Adjust(f,r,floor));
        runtime.Enable();runtime.OnInput(0x1000,0);now+=16;keys.Add('P');
        Assert.Equal(8u,runtime.OnInput(0x1000,8));
        Assert.Contains(speech,s=>s.Contains("release",StringComparison.OrdinalIgnoreCase));
    }

    private static FieldFloorSnapshot Floor(int mode,int toggle)=>new(0x1000,0x4000,160,6,16,0,mode,toggle);
    private static NavigationFrame SimpleFrame()=>new("00001000:160",true,new(0,0,1),
        [new("goal","Destination",NavigationCategory.People,new(256,0,1),[new(256,0,1)],true,true)],new Line(),256)
        {MovingFloor=new(NavigationDirection.East,16)};
    private sealed class Line:INavigationGraph
    {public IEnumerable<NavigationPoint> Neighbours(NavigationPoint p){if(p.X<512)yield return p with{X=p.X+64};if(p.X>0)yield return p with{X=p.X-64};}}

    internal static NavigationFrame Frame(int scene,int? identityScene=null,bool coherent=true)
    {
        using var doc=JsonDocument.Parse(typeof(ShiftingSandNavigationTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.sunken-desert-initial-0374.json")!);
        var row=doc.RootElement.GetProperty("scenes").EnumerateArray().Single(e=>e.GetProperty("scene").GetInt32()==scene);
        var w=row.GetProperty("width").GetInt32();var h=row.GetProperty("height").GetInt32();
        byte[] Bytes(string key)=>Convert.FromBase64String(row.GetProperty(key).GetString()!);
        var map=new FieldMapSnapshot(w,h,Bytes("shapes"),Bytes("flags"),Bytes("layers"),1,false,w,h,Enumerable.Repeat((byte)128,w*h).ToArray());
        var treasures=row.GetProperty("chests").EnumerateArray().Select(e=>new FieldTreasure(e.GetProperty("index").GetInt32(),e.GetProperty("x").GetInt32()*256+128,e.GetProperty("y").GetInt32()*256+128)).ToArray();
        var grid=treasures.Select(t=>(t.FineX/256,t.FineY/256)).ToHashSet();
        var x=scene==160?5760:1536;var y=scene==160?2290:1663;
        var actor=new FieldActorSnapshot(6,x/256,x,x&255,y/256,y,y&255,1,1,1,0,0,0x2020,0,128,true,true,true,true);
        var field=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,scene==160?20:21,identityScene??scene,coherent,1,0,6,6,actor,[actor]);
        return new FieldNavigationSource(new NoMemory(),_=>{}).Build(field,map,new(0,0,w*256,h*256),treasures,new(213,true),(a,b)=>grid.Contains((a,b)));
    }
    private sealed class NoMemory:IReadableMemory{public bool TryRead(nuint a,Span<byte>b)=>false;}
}
