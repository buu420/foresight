using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class PresentVortexNavigationTests
{
    internal const string FirstSwitch="script-switch:8:Extra:35:1:True:22:14:22:14";
    internal const string LadderSwitch="script-switch:8:Extra:35:2:True:7:8:7:8";
    internal const string Story="story:full:lavos";

    [Theory]
    [InlineData("608-switch-ledge",FirstSwitch,22,14,false)] [InlineData("608-switch-ledge",FirstSwitch,22,14,true)]
    [InlineData("613-switch",LadderSwitch,7,8,false)] [InlineData("613-switch",LadderSwitch,7,8,true)]
    public void VisibleNativeSwitchesAreObjectsWithRealConfirmationReach(string key,string id,int x,int y,bool automatic)
    {
        var s=Sample(key);var frame=Build(s);
        var target=Assert.Single(frame.Targets,t=>t.Id==id);
        Assert.Equal(NavigationCategory.Objects,target.Category);
        Assert.Contains("switch",target.Label,StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.All(target.ApproachPoints,p=>{Assert.Equal(x,p.X/256);Assert.Equal(y,p.Y/256);});
        Assert.NotNull(target.ConfirmFacings);
        var (controller,result)=Start(frame,id,automatic);Assert.True(result.Guiding,string.Join(" | ",result.Speech));
        var route=NavigationPathfinder.Search(frame.Graph,frame.Player,target.ApproachPoints).Route!;
        Assert.NotNull(route);
        // Native24 writes Local3, while the script tests Local0. The native
        // prompt accepts any facing; the mod must not invent a turn requirement.
        foreach(var facing in new[]{NavigationDirection.North,NavigationDirection.East,NavigationDirection.South,NavigationDirection.West})
            Assert.Contains(facing,target.AnyConfirmFacings(route[^1]));
        result=controller.Update(frame with{Player=route[^1],PlayerFacing=NavigationDirection.West},100);
        Assert.False(result.AutoWalking);
        Assert.Contains(result.Speech,line=>line.Contains("Confirm",StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Speech,line=>line.Contains("choose Yes",StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("608-switch-ledge")] [InlineData("613-switch")]
    public void ChoosingToPressASwitchDoesNotCreateAnAutomaticExit(string key)
    {
        var frame=Build(Sample(key));
        Assert.DoesNotContain(frame.Targets,t=>t.Id is "script-region:8:609:22:14:22:14" or
            "script-region:8:613:7:8:7:8" or "script-terrain:8:0:22:14:22:14" or "script-terrain:8:0:7:8:7:8");
    }

    public static IEnumerable<object[]> FirstRouteCases=>new[]{
        ("capture-1842","script-region:8:610:11:6:12:7"),
        ("608-left","script-region:8:610:11:6:12:7"),("608-near-left","script-region:8:610:11:6:12:7"),
        ("610-entry","exit:1"),("614-entry","exit:1"),("615-entry","script-region:8:608:13:2:14:2"),
        ("608-switch-ledge",FirstSwitch)}.SelectMany(p=>new[]{new object[]{p.Item1,p.Item2,false},new object[]{p.Item1,p.Item2,true}});

    [Theory]
    [MemberData(nameof(FirstRouteCases))]
    public void StoryUsesTheActualPassageToTheSwitchLedge(string key,string id,bool automatic)
    {
        var frame=Build(Sample(key));var next=frame.Targets.Single(t=>t.Id==id);
        var story=frame.Targets.Single(t=>t.Id==Story);
        Assert.False(story.IsStoryNote);
        Assert.NotEmpty(story.ApproachPoints);
        Assert.All(story.ApproachPoints,p=>Assert.Contains(p,next.ApproachPoints));
        Assert.True(Start(frame,Story,automatic).Result.Guiding);
        Assert.DoesNotContain("entrance switch",story.Label,StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("609-after-switch","script-region:8:611:11:6:12:7",false)]
    [InlineData("609-after-switch","script-region:8:611:11:6:12:7",true)]
    [InlineData("611-ladder","exit:3",false)] [InlineData("611-ladder","exit:3",true)]
    [InlineData("613-switch",LadderSwitch,false)] [InlineData("613-switch",LadderSwitch,true)]
    public void AfterTheFirstSwitchStoryReachesTheLaterNativeControl(string key,string id,bool automatic)
    {
        var frame=Build(Sample(key));var next=frame.Targets.Single(t=>t.Id==id);
        var story=frame.Targets.Single(t=>t.Id==Story);
        Assert.NotEmpty(story.ApproachPoints);
        Assert.All(story.ApproachPoints,p=>Assert.Contains(p,next.ApproachPoints));
        Assert.True(Start(frame,Story,automatic).Result.Guiding);
        Assert.DoesNotContain(frame.Targets,t=>t.Id==FirstSwitch);
    }

    [Theory]
    [InlineData("missing")] [InlineData("disabled")] [InlineData("class")] [InlineData("removed")] [InlineData("pc")]
    public void SwitchObjectsRequireTheirRealNativeWatcher(string variant)
    {
        var s=Sample("608-switch-ledge");var actors=s.Field.Actors.Where(a=>variant!="missing" || a.Index!=8)
            .Select(a=>a.Index!=8?a:variant=="disabled"?a with{ScriptProcessingEnabled=false}:
                variant=="class"?a with{ClassTag=4}:variant=="removed"?a with{ClassTag=0x87}:a with{ScriptAddress=0x1FE}).ToArray();
        Assert.DoesNotContain(Build(s with{Field=s.Field with{Actors=actors}}).Targets,t=>t.Id==FirstSwitch);
    }

    [Theory]
    [InlineData("608-switch-ledge",FirstSwitch,1)] [InlineData("613-switch",LadderSwitch,3)]
    public void PressedOrUnknownSwitchStateDoesNotOfferAnUnpressedControl(string key,string id,int flags)
    {
        var s=Sample(key);var extra=new Dictionary<int,int>(s.State.Extended){[35]=flags};
        Assert.DoesNotContain(Build(s with{State=s.State with{Extended=extra}}).Targets,t=>t.Id==id);
        extra.Remove(35);
        Assert.DoesNotContain(Build(s with{State=s.State with{Extended=extra}}).Targets,t=>t.Id==id);
    }

    [Theory]
    [InlineData("608-left","script-region:8:610:11:6:12:7",3072)]
    [InlineData("615-entry","script-region:8:608:13:2:14:2",3584)]
    public void NarrowPassagesIncludeABodyClearStandingColumn(string key,string id,int column)
    {
        var frame=Build(Sample(key));var target=frame.Targets.Single(t=>t.Id==id);
        Assert.Contains(target.ApproachPoints,p=>p.X>=column-16 && p.X<=column+15);
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph,frame.Player,target.ApproachPoints).Route);
    }

    public static IEnumerable<object[]> WalkingCases=>new[]{
        ("capture-1842","script-region:8:610:11:6:12:7",11,6,12,7),
        ("capture-2900","exit:1",23,63,26,63),
        ("608-left","script-region:8:610:11:6:12:7",11,6,12,7),
        ("610-entry","exit:1",61,23,61,23),("614-entry","exit:1",23,63,26,63),
        ("615-entry","script-region:8:608:13:2:14:2",13,2,14,2),
        ("608-switch-ledge",FirstSwitch,22,14,22,14),("613-switch",LadderSwitch,7,8,7,8)}
        // The captured614 corner needs the native walking control at Run speed;
        // PresentVortexWalkingFeedbackTests exercises the actual input runtime
        // and the player's response to its cue for all native Run settings.
        .SelectMany(c=>new[]{false,true}.SelectMany(a=>(c.Item1=="capture-2900"?new[]{16}:new[]{16,32})
            .Select(speed=>new object[]{c.Item1,c.Item2,c.Item3,c.Item4,c.Item5,c.Item6,a,speed})));

    [Theory]
    [MemberData(nameof(WalkingCases))]
    public void GuidanceReachesTheNativeTilePredicatesUsingTranscribedMovement(string key,string id,int left,int top,int right,int bottom,bool automatic,int speed)
    {
        var s=Sample(key);var frame=Build(s);var (controller,result)=Start(frame,id,automatic);
        Assert.True(result.Guiding);var reached=false;
        for(var tick=1;tick<12000 && result.Guiding;tick++){
            var direction=automatic?result.Direction:result.ManualLeg?.Direction??NavigationDirection.None;
            var p=NativeWalkingReplay.Move(s.Map,frame.Player,direction,speed);
            frame=frame with{Player=p,PlayerFacing=direction};
            reached=p.X/256>=left && p.X/256<=right && p.Y/256>=top && p.Y/256<=bottom;
            if(reached)break;
            result=controller.Update(frame,tick*16,!automatic && direction!=NavigationDirection.None);
        }
        Assert.True(reached,$"{key} stopped at{frame.Player}");
        var stopped=controller.Update(frame with{CanNavigate=false},200000);
        Assert.False(stopped.AutoWalking);Assert.Equal(NavigationDirection.None,stopped.Direction);
    }

    [Theory]
    [InlineData("612-save-room",328)] [InlineData("612-save-room",329)]
    [InlineData("614-entry",331)] [InlineData("615-entry",332)] [InlineData("649-grotto",337)]
    [InlineData("614-ladder-open",330)] [InlineData("614-ladder-open",331)]
    public void AuditedUnopenedChestsKeepTheirRoutesAndCollectedChestsDisappear(string key,int index)
    {
        var s=Sample(key);var frame=Build(s);var id=$"chest:{index}";
        Assert.True(Start(frame,id,false).Result.Guiding);
        Assert.True(Start(frame,id,true).Result.Guiding);
        Assert.DoesNotContain(Build(s with{Treasures=s.Treasures.Where(t=>t.Index!=index).ToArray()}).Targets,t=>t.Id==id);
    }

    [Fact]
    public void TheUnopenedLadderDoesNotGrantAccessToItsFarChest()
    {
        var frame=Build(Sample("614-entry"));
        Assert.DoesNotContain(frame.Targets,t=>t.Id=="chest:330" && t.GuideAvailable);
    }

    internal sealed record Input(FieldNavigationSnapshot Field,FieldMapSnapshot Map,FieldStoryState State,
        IReadOnlyList<FieldTreasure> Treasures,IReadOnlyList<(int X,int Y)> ChestCells);
    internal static Input Sample(string key)
    {
        using var doc=JsonDocument.Parse(typeof(PresentVortexNavigationTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.present-vortex-0387.json")!);
        var r=doc.RootElement.GetProperty("cases").EnumerateArray().Single(e=>e.GetProperty("key").GetString()==key);
        var actors=r.GetProperty("actors").Deserialize<FieldActorSnapshot[]>()!;
        var map=r.GetProperty("map").Deserialize<FieldMapSnapshot>()!;var f=r.GetProperty("field");var st=r.GetProperty("story");
        var lead=actors.Single(a=>a.Index==f.GetProperty("leader").GetInt32());
        var field=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,actors.Max(a=>a.Index)+1,
            f.GetProperty("scene").GetInt32(),f.GetProperty("sceneCoherent").GetBoolean(),f.GetProperty("control").GetInt32(),
            f.GetProperty("inputMode").GetInt32(),0,lead.Index,lead,actors,actors.Where(a=>a.IsPartyMember).Select(a=>a.Index).ToArray()){
                ActorCollisionRadius=f.GetProperty("collisionRadius").GetInt32(),LastPartySlotRaw=f.GetProperty("lastPartySlotRaw").GetInt32(),
                ContactActorRaw=f.GetProperty("contactActorRaw").GetInt32(),ConfirmActorRaw=f.GetProperty("confirmActorRaw").GetInt32()};
        var state=new FieldStoryState(st.GetProperty("point").GetInt32(),true){
            Globals=st.GetProperty("globals").Deserialize<Dictionary<int,int>>()!,Locals=st.GetProperty("locals").Deserialize<Dictionary<int,int>>()!,
            Extended=st.GetProperty("extended").Deserialize<Dictionary<int,int>>()!,Party=[1,2,0,128,130,5,4,3,6]};
        var treasures=r.GetProperty("treasures").EnumerateArray().Select(t=>new FieldTreasure(t.GetProperty("index").GetInt32(),
            t.GetProperty("x").GetInt32(),t.GetProperty("y").GetInt32()){IsChest=t.GetProperty("chest").GetBoolean()}).ToArray();
        var cells=r.TryGetProperty("nativeTreasureGrid",out var grid)?grid.EnumerateArray().Select(t=>(t.GetProperty("x").GetInt32(),t.GetProperty("y").GetInt32())).ToArray():
            treasures.Where(t=>t.IsChest).Select(t=>(t.FineX/256,t.FineY/256)).ToArray();
        return new(field,map,state,treasures,cells);
    }
    internal static NavigationFrame Build(Input s)=>new FieldNavigationSource(new Empty(),_=>{}).Build(s.Field,s.Map,
        new(0,0,s.Map.Width*256,s.Map.Height*256),s.Treasures,s.State,(x,y)=>s.ChestCells.Contains((x,y)));
    internal static (NavigationController Controller,NavigationResult Result) Start(NavigationFrame frame,string id,bool automatic)
    {
        var t=frame.Targets.Single(t=>t.Id==id);var controller=new NavigationController();
        for(var i=0;i<(int)t.Category;i++)controller.Handle(NavigationCommand.NextCategory,frame,0);
        for(var i=0;i<frame.Targets.Count && controller.TargetId!=id;i++)controller.Handle(NavigationCommand.NextTarget,frame,0);
        Assert.Equal(id,controller.TargetId);
        return(controller,controller.Handle(automatic?NavigationCommand.ToggleWalk:NavigationCommand.Guide,frame,1));
    }
    private sealed class Empty:IReadableMemory{public bool TryRead(nuint address,Span<byte>buffer)=>false;}
}
