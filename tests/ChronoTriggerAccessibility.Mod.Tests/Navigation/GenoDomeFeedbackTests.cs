using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class GenoDomeFeedbackTests
{
    [Fact]
    public void ElectricalChargeIsAnnouncedOnceAndItsVisibleEndIsAnnounced()
    {
        var said=new List<string>();var feedback=new GenoDomeFeedback(said.Add,_=>"R-66");feedback.Enable();
        feedback.Observe(Field(),Story(),View);
        feedback.Observe(Field(charged:true),Story(charge:true),View);
        feedback.Observe(Field(charged:true),Story(charge:true),View);
        Assert.Equal(new[]{"R-66 is electrically charged."},said);
        Assert.Contains("electrically charged",feedback.Status);
        feedback.Observe(Field(),Story(),View);
        Assert.Equal("R-66's electrical charge has faded.",said.Last());
        Assert.Null(feedback.Status);
    }

    [Theory]
    [InlineData(0)] // No visual effect despite a set charge flag.
    [InlineData(1)] // Lingering drawn effect after the flag clears.
    [InlineData(2)] // Missing story capture.
    [InlineData(3)] // Missing effect actor.
    [InlineData(4)] // Effect draw state unavailable.
    public void UnknownOrContradictoryStateDoesNotInventDischarge(int missing)
    {
        var said=new List<string>();var f=new GenoDomeFeedback(said.Add);f.Enable();
        f.Observe(Field(charged:true),Story(charge:true),View);said.Clear();
        var field=Field(charged:missing==1);
        if(missing==3)field=field with{Actors=field.Actors.Where(a=>a.Index!=9).ToArray()};
        if(missing==4)field=field with{Actors=field.Actors.Select(a=>a.Index==9?a with{DrawModeKnown=false}:a).ToArray()};
        f.Observe(field,missing==2?null:Story(charge:missing==0),View);
        Assert.Empty(said);Assert.Null(f.Status);
        f.Observe(Field(charged:true),Story(charge:true),View);Assert.Empty(said);
        f.Observe(Field(),Story(),View);Assert.Single(said);
    }

    [Fact]
    public void AVisibleDroidReportsWaitingThenFollowingWithoutRepeatingEachStep()
    {
        var said=new List<string>();var f=new GenoDomeFeedback(said.Add);f.Enable();
        f.Observe(Field(droid:true),Story(),View);
        Assert.Equal("The mobile Sentinel is waiting.",Assert.Single(said));
        f.Observe(Field(droid:true,following:true),Story(follow:true),View);
        f.Observe(Field(droid:true,following:true),Story(follow:true),View);
        Assert.Equal("The mobile Sentinel is following you.",said.Last());Assert.Equal(2,said.Count);
        Assert.Contains("following",f.Status);
    }

    [Fact]
    public void DisappearingOrUnknownDroidIsNotReportedAsWaiting()
    {
        var said=new List<string>();var f=new GenoDomeFeedback(said.Add);f.Enable();
        f.Observe(Field(droid:true,following:true),Story(follow:true),View);said.Clear();
        f.Observe(Field(),Story(),View);
        f.Observe(Field(droid:true,following:true),Story(follow:true) with{Locals=new Dictionary<int,int>()},View);
        Assert.Empty(said);Assert.Null(f.Status);
        f.Observe(Field(droid:true,following:true),Story(follow:true),View);Assert.Empty(said);
    }

    [Fact]
    public void ATrackedSentinelReturningToItsPostReportsTheEndOfFollowing()
    {
        var said=new List<string>();var f=new GenoDomeFeedback(said.Add);f.Enable();
        f.Observe(Field(droid:true,following:true),Story(follow:true),View);said.Clear();
        // Native battles reset local12 and return the tracked robot to its
        // already-visited post. It must not silently remain "following".
        f.Observe(Field(droid:true),Story(),new(5000,5000,8000,8000));
        Assert.Equal("The mobile Sentinel has returned to its post.",Assert.Single(said));
    }

    [Fact]
    public void NativeDroidMeetingDoesNotSoundLikeReturningToTheWaitingRoom()
    {
        var said=new List<string>();var f=new GenoDomeFeedback(said.Add);f.Enable();
        f.Observe(Field(droid:true,following:true),Story(follow:true),View);said.Clear();
        var field=Field(droid:true) with{Actors=new[]{Leader(),Droid(8,7,36),Droid(29,7,34),Effect(false)}};
        f.Observe(field,Story(complete:true),View);
        Assert.Equal("The mobile Sentinel has stopped beside the guard Sentinel.",Assert.Single(said));
        f.Observe(field,Story(complete:true),View);Assert.Single(said);
    }

    [Theory]
    [InlineData(0)] // Foreign original/vortex scene.
    [InlineData(1)] // Incoherent scene.
    [InlineData(2)] // Native control has not returned.
    [InlineData(3)] // Missing/foreign leader.
    [InlineData(4)] // Offscreen effects and droid.
    public void UnobservableStateStaysSilent(int variant)
    {
        var said=new List<string>();var f=new GenoDomeFeedback(said.Add);f.Enable();var field=Field(true,true,true);
        field=variant switch{0=>field with{SceneId=659},1=>field with{SceneIdCoherent=false},2=>field with{ControlFlag=0},
            3=>field with{LeadPlayer=null},_=>field};
        f.Observe(field,Story(charge:true,follow:true),variant==4?new(0,0,128,128):View);
        Assert.Empty(said);Assert.Null(f.Status);
    }

    [Fact]
    public void DisableSuspendAndOwnerChangeCannotSpeakStaleCues()
    {
        var said=new List<string>();var f=new GenoDomeFeedback(said.Add);
        f.Observe(Field(charged:true),Story(charge:true),View);Assert.Empty(said);
        f.Enable();f.Observe(Field(charged:true),Story(charge:true),View);Assert.Single(said);
        f.Suspend();Assert.Null(f.Status);
        f.Observe(Field(charged:true),Story(charge:true),View);Assert.Single(said);
        f.Disable();f.Observe(Field(),Story(),View);Assert.Single(said);Assert.Null(f.Status);
        f.Enable();f.Observe(Field(),Story(),View);Assert.Single(said);
        f.Observe(Field(charged:true) with{ActorBase=0x5000},Story(charge:true),View);Assert.Equal(2,said.Count);
    }

    [Fact]
    public void ReentrantDisableWhileResolvingANameCannotPublish()
    {
        var said=new List<string>();GenoDomeFeedback? f=null;
        f=new GenoDomeFeedback(said.Add,_=>{f!.Disable();return "Robo";});f.Enable();
        f.Observe(Field(charged:true),Story(charge:true),View);Assert.Empty(said);Assert.Null(f.Status);
    }

    [Fact]
    public void TheFieldSourcePublishesChargeFeedbackFromItsExistingCapture()
    {
        var said=new List<string>();var feedback=new GenoDomeFeedback(said.Add);feedback.Enable();
        var source=new FieldNavigationSource(new Empty(),_=>{},fieldObserver:feedback.Observe);
        var map=new FieldMapSnapshot(64,64,new byte[4096],new byte[4096],Enumerable.Repeat((byte)1,4096).ToArray(),1,false,64,64,
            Enumerable.Repeat((byte)128,4096).ToArray());
        source.Build(Field(charged:true),map,View,[],Story(charge:true));
        Assert.Equal("Robo is electrically charged.",Assert.Single(said));
        source.Build(Field(),map,View,[],Story());
        Assert.Equal("Robo's electrical charge has faded.",said.Last());
    }

    private static readonly FieldViewport View=new(0,0,16384,16384);
    internal static FieldActorSnapshot Leader()=>GenoDomeNavigationTests.Marker(4,25,25) with{ClassTag=0,VisualIndex=3,IsPartyMember=true,LoadedFlag=1};
    private static FieldActorSnapshot Effect(bool charged)=>GenoDomeNavigationTests.Marker(9,25,25) with{ClassTag=4,VisualIndex=86,LoadedFlag=0,DrawMode=charged?1:0};
    private static FieldActorSnapshot Droid(int slot,int x,int y)=>GenoDomeNavigationTests.Marker(slot,x,y) with{ClassTag=4,VisualIndex=170,LoadedFlag=1};
    internal static FieldNavigationSnapshot Field(bool charged=false,bool droid=false,bool following=false)
    {
        var actors=new List<FieldActorSnapshot>{Leader(),Effect(charged)};
        if(droid)actors.Add(following?Droid(8,25,26):Droid(8,21,8) with{FineY=0x880,FractionY=128});
        return new(0x1000,0x4000,0x2000,0x20000,51,256,true,1,0,0,4,actors[0],actors);
    }
    internal static FieldStoryState Story(bool charge=false,bool follow=false,bool complete=false)=>new(214,true)
    {Globals=new Dictionary<int,int>{{0x13C,1|(charge?0x40:0)|(complete?0x80:0)}},Locals=new Dictionary<int,int>{{12,follow?1:0}}};
    private sealed class Empty:IReadableMemory{public bool TryRead(nuint address,Span<byte> bytes)=>false;}
}
