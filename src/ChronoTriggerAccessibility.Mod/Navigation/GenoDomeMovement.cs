using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Use a selectable short walking step where a straight running step
/// is obstructed. Otherwise preserve the controller's native input: a rejected
/// straight probe can still be a valid native wall slide, turn or touch.</summary>
public static class GenoDomeMovement
{
    public static bool IsScene(string scene)=>scene.EndsWith(":256",StringComparison.Ordinal);

    public static FieldNavigationPad Adjust(NavigationFrame frame,NavigationResult result,FieldFloorSnapshot? native)
    {
        var fallback=FieldNavigationRuntime.DirectionBits(result.Direction);
        if(result.SteeringPoint is null || result.NativeInteractionInput || result.InteractionFacing!=NavigationDirection.None) return new(fallback);
        if(native is not {RunMode:>=0 and <=2,RunToggle:>=0 and <=2} floor ||
            frame.Scene!=$"{floor.Engine:X8}:{floor.Scene}" || frame.CanTraverseFloorStep is not { } clear)
            return new(fallback);
        var mode=floor.RunMode.Value;var toggle=floor.RunToggle.Value;
        if(toggle==0)toggle=mode switch{0=>1,1=>2,_=>0};
        // Conveyor capability remains in FieldFloorMovement. This optional
        // correction neither changes a direction nor predicts native slides.
        if(floor.ForceX!=0 || floor.ForceY!=0 || mode==1 && toggle==1 || mode is 0 or 2 && toggle==2)return new(fallback);
        var dx=(fallback&0x100)!=0?1:(fallback&0x200)!=0?-1:0;
        var dy=(fallback&0x400)!=0?1:(fallback&0x800)!=0?-1:0;
        (int X,int Y) Step(int speed)=>dx!=0 && dy!=0
            ? ((int)(dx*speed/1.39f),(int)(dy*speed/1.39f)) : (dx*speed,dy*speed);
        var run=Step(32);var walk=Step(16);
        if(clear(frame.Player,run.X,run.Y) || !clear(frame.Player,walk.X,walk.Y))return new(fallback);
        // Original 175C90: held Dash walks in modes0/2; released Dash walks
        // in mode1 unless toggle1 forces running. Never synthesize a release edge.
        return new(fallback|(mode==1?0u:8u)){OverrideDash=true};
    }

}
