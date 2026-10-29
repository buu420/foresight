using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FrozenCliffsRuntimeTests
{
    [Fact]
    public void ManualGuidanceNeverSuppliesGameInput()
    {
        var h = new Harness(); h.Enable(); h.Press('O');
        Assert.Equal(0u,h.Press('I')); Assert.Equal(0u,h.Tick());
    }

    [Fact]
    public void ManualSlideGuidanceExplainsTheCapturedWalkingSettingWithoutSupplyingInput()
    {
        var h=new Harness();h.Floor=h.Floor!.Value with{RunToggle=2};h.Enable();h.Press('O');
        Assert.Equal(0u,h.Press('I'));
        Assert.Contains(h.Speech,s=>s.Contains("tap and release Dash",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AutomaticSlidingRequiresSelectionAndNeverPressesConfirm()
    {
        var h = new Harness(); h.Enable(); Assert.Equal(0u,h.Tick()); h.Press('O');
        var pad=h.Press('P'); Assert.NotEqual(0u,pad&0xF00); Assert.Equal(0u,pad&~0xF08u);
        Assert.Equal(0u,h.Press('P')); Assert.Equal(0u,h.Tick());
    }

    [Theory]
    [InlineData("foreground")] [InlineData("gap")] [InlineData("capture")] [InlineData("control")]
    [InlineData("scene")] [InlineData("manual")] [InlineData("modifier")] [InlineData("run")]
    [InlineData("floor")] [InlineData("actor")] [InlineData("actor-base")] [InlineData("disable")]
    public void EveryLossOfSlideAuthorityStopsWithoutAutomaticResumption(string cause)
    {
        var h=new Harness();h.Enable();h.Press('O');Assert.NotEqual(0u,h.Press('P')&0xF00);
        switch(cause)
        {
            case "foreground":h.Foreground=false;break; case "gap":h.Now+=1000;break;
            case "capture":h.FailCapture=true;break;case "control":h.Frame=h.Frame with{CanNavigate=false};break;
            case "scene":h.Frame=h.Frame with{Scene="00001000:605"};break;
            case "run":h.Floor=h.Floor!.Value with{RunToggle=2};break; case "floor":h.Floor=null;break;
            case "actor":h.Floor=h.Floor!.Value with{Actor=4};break;
            case "actor-base":h.Floor=h.Floor!.Value with{ActorBase=0x5000};break;
            case "disable":h.Runtime.Disable();break;
        }
        var input=cause=="manual"?0x400u:cause=="modifier"?0x2000u:0u;
        Assert.Equal(input,h.Tick(input));
        h.Foreground=true;h.FailCapture=false;h.Frame=h.Original;h.Floor=new(0x1000,0x4000,604,3,0,32,0,1);
        Assert.Equal(0u,h.Tick());Assert.Equal(0u,h.Tick());
    }

    [Fact]
    public void AClosedNativeRunToggleGivesInstructionsInsteadOfTryingToSlideBlindly()
    {
        var h=new Harness();h.Floor=h.Floor!.Value with{RunToggle=2};h.Enable();h.Press('O');
        Assert.Equal(0u,h.Press('P'));Assert.Contains(h.Speech,s=>s.Contains("running is required",StringComparison.Ordinal));
    }

    [Fact]
    public void TheFinalContactUsesUpAndReleasesWhenTheNativeWarpTakesControl()
    {
        var h=new Harness();var goal=h.Frame.Targets.Single(t=>t.Id==Harness.CaveId).ApproachPoints[0];
        h.Frame=h.Frame with{Player=goal,PlayerFacing=NavigationDirection.East};h.Floor=h.Floor!.Value with{ForceY=0};
        h.Enable();h.Press('O');h.Press('P');
        Assert.Equal(0x800u,h.Tick());
        h.Frame=h.Frame with{CanNavigate=false};Assert.Equal(0u,h.Tick());
    }

    private sealed class Harness
    {
        public const string CaveId="script-region:9:605:7:14:7:14";
        public HashSet<int> Keys{get;}=[];public List<string> Speech{get;}=[];
        public bool Foreground=true,FailCapture;public long Now;
        public NavigationFrame Original{get;}=FrozenCliffsNavigationTests.Sample(604).Frame;
        public NavigationFrame Frame;
        public FieldFloorSnapshot? Floor=new(0x1000,0x4000,604,3,0,32,0,1);
        public FieldNavigationRuntime Runtime{get;}
        public Harness()
        {
            Frame=Original with{Targets=Original.Targets.Where(t=>t.Id==CaveId).ToArray()};
            Runtime=new(_=>FailCapture?null:Frame,new(Keys.Contains,()=>Foreground),()=>Foreground,()=>Now,
                Speech.Add,_=>{},adjustFieldInput:(f,result)=>FieldFloorMovement.Adjust(f,result,Floor),fieldFloor:()=>Floor);
        }
        public void Enable(){Runtime.Enable();Tick();}
        public uint Tick(uint input=0){Now+=16;return Runtime.OnInput(0x1000,input);}
        public uint Press(int key){Keys.Clear();Tick();Keys.Add(key);var pad=Tick();Keys.Clear();return pad;}
    }
}
