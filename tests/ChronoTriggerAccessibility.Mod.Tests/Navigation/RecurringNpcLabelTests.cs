using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class RecurringNpcLabelTests
{
    [Theory]
    [InlineData(332,9,4,9,"Schala",0xD4)]
    [InlineData(328,9,4,10,"Janus",0xD4)]
    [InlineData(188,13,4,5,"Toma",0xD4)]
    [InlineData(279,8,4,6,"Kino",0xD4)]
    [InlineData(4,8,4,39,"Taban",0xD4)]
    [InlineData(9,9,4,213,"Lara",0xD4)]
    [InlineData(1,15,4,37,"Mother",0xD4)]
    [InlineData(40,8,4,0,"Melchior",0xD4)]
    [InlineData(242,16,5,0,"Nu, Belthasar's recording",0xD4)]
    [InlineData(464,28,4,72,"Gaspar",0xD4)]
    [InlineData(465,10,6,224,"Spekkio",0xD4)]
    [InlineData(152,10,4,4,"Tata",0xD4)]
    [InlineData(180,9,5,165,"Slash",0xD4)]
    [InlineData(180,10,6,160,"Flea",0xD4)]
    [InlineData(177,8,5,168,"Ozzie",0xD4)]
    [InlineData(300,8,5,154,"Azala",0xD4)]
    [InlineData(409,33,5,151,"Masa",0xC4)]
    [InlineData(404,9,6,152,"Mune",0xC2)]
    public void EveryRequestedCharacterHasANativePeopleRepresentationWhenIntroduced(int scene,int slot,int type,int visual,string name,int point)
    {
        var state=State(point) with{Globals=new Dictionary<int,int>(State(point).Globals)
        {[0xF4]=0x10,[0x1A0]=2,[0x5A]=4,[0x7C]=1,[0xD8]=2,[0x138]=1}};
        Assert.Contains(Frame(scene,slot,type,visual,state).Targets,t=>t.Label==name&&t.Category==NavigationCategory.People);
    }

    [Theory]
    [InlineData(329,12,4,9,"Schala",0xF4,0x10)]
    [InlineData(332,9,4,9,"Schala",0xF4,0x10)]
    [InlineData(328,9,4,10,"Janus",0xF4,0x10)]
    [InlineData(116,19,4,5,"Toma",0x144,0x10)]
    [InlineData(138,21,4,5,"Toma",0xF2,1)]
    [InlineData(155,18,4,5,"Toma",0xF2,2)]
    [InlineData(188,13,4,5,"Toma",0x1A0,2)]
    [InlineData(63,9,4,5,"Toma",0x1AC,0x40)]
    [InlineData(4,8,4,39,"Taban",-1,0)]
    [InlineData(9,9,4,213,"Lara",0x5A,4)]
    [InlineData(4,11,4,213,"Lara",0x5A,0x42)]
    [InlineData(40,8,4,0,"Melchior",-1,0)]
    [InlineData(41,8,4,0,"Melchior",-1,0)]
    [InlineData(440,23,4,0,"Melchior",-1,0)]
    [InlineData(242,16,5,0,"Nu, Belthasar's recording",0x7C,1)]
    [InlineData(465,10,6,224,"Spekkio",-1,0)]
    [InlineData(152,10,4,4,"Tata",-1,0)]
    [InlineData(169,11,5,157,"Slash",0xA3,0x40)]
    [InlineData(180,9,6,165,"Slash",-1,0)]
    [InlineData(184,9,5,186,"Slash",-1,0)]
    [InlineData(180,10,5,160,"Flea",-1,0)]
    [InlineData(183,9,6,187,"Flea",-1,0)]
    [InlineData(289,8,5,154,"Azala",-1,0)]
    [InlineData(300,8,6,154,"Azala",-1,0)]
    [InlineData(301,9,5,154,"Azala",-1,0)]
    public void ProvenNativeIdentityAndIntroductionProduceTheName(int scene,int slot,int type,int visual,string name,int flag,int value)
    {
        var state=State();if(flag>=0)state=state with{Globals=new Dictionary<int,int>(state.Globals){[flag]=value}};
        var result=RecurringNpcLabels.Resolve(scene,Actor(slot,type,visual),state);
        Assert.True(result.Handled);Assert.Equal(name,result.Name);
        Assert.NotEqual(name,RecurringNpcLabels.Resolve(scene,Actor(slot,type,visual),State(3)).Name);
        Assert.False(RecurringNpcLabels.Resolve(scene,Actor(slot,type==4?5:4,visual),state).Handled);
    }

    [Theory]
    [InlineData(332,9,4,9)]
    [InlineData(328,9,4,10)]
    [InlineData(116,19,4,5)]
    [InlineData(465,10,5,225)]
    [InlineData(5,14,4,0)]
    public void MissingIntroductionSuppressesTheOldPrematureName(int scene,int slot,int type,int visual)
    {
        var result=RecurringNpcLabels.Resolve(scene,Actor(slot,type,visual),State(3));
        Assert.True(result.Handled);Assert.Null(result.Name);
    }

    [Theory]
    [InlineData(145,9,4,4)]
    [InlineData(241,8,4,12)]
    [InlineData(62,13,4,5)]
    [InlineData(33,8,5,166)]
    [InlineData(154,11,4,37)]
    [InlineData(391,8,5,160)]
    [InlineData(125,9,4,37)]
    [InlineData(449,8,4,0)]
    [InlineData(188,13,4,44)]
    public void ReusedSpritesAndDisguisesDoNotAcquireAnotherCharactersIdentity(int scene,int slot,int type,int visual) =>
        Assert.False(RecurringNpcLabels.Resolve(scene,Actor(slot,type,visual),State()).Handled);

    [Fact]
    public void EnhasasBoyCanBeRecognizedAsJanusOnlyAfterTheNativeIntroduction()
    {
        Assert.Null(RecurringNpcLabels.Resolve(355,Actor(30,4,10),State(0xA0)).Name);
        Assert.Equal("Janus",RecurringNpcLabels.Resolve(355,Actor(30,4,10),State(0xAB)).Name);
    }

    [Theory]
    [InlineData(0,0,false)]
    [InlineData(2,0,false)]
    [InlineData(0,1,false)]
    [InlineData(2,1,true)]
    [InlineData(2,2,true)]
    public void GasparNeedsBothTheIdentityClueAndHisOwnConfirmation(int reveal,int cape,bool named)
    {
        var state=State() with{Globals=new Dictionary<int,int>(State().Globals){[0xD8]=reveal,[0x138]=cape}};
        var identity=RecurringNpcLabels.Resolve(464,Actor(28,4,72),state);
        Assert.True(identity.Handled);
        Assert.Equal(named?"Gaspar":"Old man under the lamp",identity.Name);
    }

    [Fact]
    public void TheConstructsRecordingIdentitySurvivesTheConsumedEgg()
    {
        var state=State() with{Globals=new Dictionary<int,int>(State().Globals){[0x170]=4}};
        Assert.Equal("Nu, Belthasar's recording",RecurringNpcLabels.Resolve(242,Actor(16,5,0),state).Name);
    }

    [Fact]
    public void ExistingNativeNameProviderCannotBypassAnUnprovedIntroduction()
    {
        var frame=Frame(5,14,4,0,State(3));
        Assert.DoesNotContain(frame.Targets,t=>t.Label.Contains("Melchior",StringComparison.Ordinal));
    }

    [Fact]
    public void NamedChallengersArePeopleAndKeepTheirSeparateBattleTargets()
    {
        var state=State() with{Globals=new Dictionary<int,int>(State().Globals){[0xA3]=0x40}};
        var frame=Frame(169,11,5,157,state);
        Assert.Contains(frame.Targets,t=>t.Label=="Slash"&&t.Category==NavigationCategory.People);
        Assert.Contains(frame.Targets,t=>t.Category==NavigationCategory.Enemies);
    }

    [Fact]
    public void HiddenOrIncoherentActorsCannotPublishTheName()
    {
        Assert.DoesNotContain(Frame(40,8,4,0,State(),drawn:false).Targets,t=>t.Label=="Melchior");
        Assert.DoesNotContain(Frame(40,8,4,0,State(),coherent:false).Targets,t=>t.Label=="Melchior");
    }

    [Fact]
    public void LarasUnpositionedPresentDaySlotDoesNotBecomeARecordedIdentity()
    {
        var state=State() with{Globals=new Dictionary<int,int>{[0x7C]=0x20}};
        Assert.Null(RecurringNpcLabels.Resolve(4,Actor(11,4,213),state).Name);
    }

    internal static FieldStoryState State(int point=0xD4)=>new(point,false)
    {Globals=Enumerable.Range(0,512).ToDictionary(i=>i,_=>0),Inventory=new Dictionary<int,int>(),Party=[0,1,2,3,4,5,6,255,255]};
    internal static FieldActorSnapshot Actor(int slot,int type,int visual)=>new(slot,8,2176,128,11,2944,128,1,1,1,visual,type,0x2020,1,128,false,true,true,true);
    internal static NavigationFrame Frame(int scene,int slot,int type,int visual,FieldStoryState state,bool drawn=true,bool coherent=true)
    {
        var lead=Actor(0,0,0) with{IsPartyMember=true,FineX=2048,FineY=3840,TileX=8,TileY=15,FractionX=0,FractionY=0};
        var actor=Actor(slot,type,visual) with{DrawMode=drawn?1:0};
        var field=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,64,scene,coherent,1,0,0,0,lead,[lead,actor]);
        var map=new FieldMapSnapshot(32,32,new byte[1024],new byte[1024],Enumerable.Repeat((byte)1,1024).ToArray(),1,false,
            32,32,Enumerable.Repeat((byte)128,1024).ToArray());
        return new FieldNavigationSource(new NoMemory(),_=>{}).Build(field,map,new(0,0,8192,8192),[],state);
    }
    private sealed class NoMemory:IReadableMemory{public bool TryRead(nuint address,Span<byte> destination)=>false;}
}
