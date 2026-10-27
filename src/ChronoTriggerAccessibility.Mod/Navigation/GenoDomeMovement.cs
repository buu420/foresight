using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Choose an ordinary direction and selectable gait on Geno Dome's
/// captured floor. The native movement routine still applies collision/forces.
/// A 32-unit step can be refused at a corner that admits a 16-unit step.</summary>
public static class GenoDomeMovement
{
    public static bool IsScene(string scene)=>scene.EndsWith(":256",StringComparison.Ordinal);

    public static FieldNavigationPad Adjust(NavigationFrame frame,NavigationResult result,FieldFloorSnapshot? native)
    {
        var fallback=FieldNavigationRuntime.DirectionBits(result.Direction);
        if(result.SteeringPoint is not { } goal || result.InteractionFacing!=NavigationDirection.None) return new(fallback);
        if(native is not {RunMode:>=0 and <=2,RunToggle:>=0 and <=2} floor ||
            frame.Scene!=$"{floor.Engine:X8}:{floor.Scene}" || frame.CanTraverseFloorStep is not { } clear)
            return new(0,"Geno Dome's current movement setting is unavailable. Try the route again.");
        var mode=floor.RunMode.Value;var toggle=floor.RunToggle.Value;
        if(toggle==0)toggle=mode switch{0=>1,1=>2,_=>0};
        uint? best=null;var score=double.PositiveInfinity;
        foreach(var dash in new[]{false,true})
        {
            // Original 175C90: held Dash requests walking in modes0/2,
            // running in mode1. Toggle1 forces running in mode1 even released.
            var running=mode==1?dash || toggle==1:!dash && toggle is 0 or 1;
            var speed=running?32:16;
            foreach(var direction in Directions)
            {
                var bits=FieldNavigationRuntime.DirectionBits(direction);
                var dx=(bits&0x100)!=0?speed:(bits&0x200)!=0?-speed:0;
                var dy=(bits&0x400)!=0?speed:(bits&0x800)!=0?-speed:0;
                var x=dx!=0 && dy!=0?(int)(floor.ForceX+dx/1.39f):floor.ForceX+dx;
                var y=dx!=0 && dy!=0?(int)(floor.ForceY+dy/1.39f):floor.ForceY+dy;
                if(result.Direction==NavigationDirection.North && y>=0 || result.Direction==NavigationDirection.South && y<=0 ||
                    result.Direction==NavigationDirection.West && x>=0 || result.Direction==NavigationDirection.East && x<=0) continue;
                if(!clear(frame.Player,x,y))continue;
                var wx=result.Direction is NavigationDirection.North or NavigationDirection.South?16:1;
                var wy=result.Direction is NavigationDirection.West or NavigationDirection.East?16:1;
                var distance=wx*Math.Pow((double)goal.X-frame.Player.X-x,2)+wy*Math.Pow((double)goal.Y-frame.Player.Y-y,2);
                if(distance>=score)continue;
                score=distance;best=bits|(dash?8u:0u);
            }
        }
        return best is { } pad?new(pad):new(0,
            "The route needs short walking steps here. Select walking with your Dash control, then start auto-walk again.");
    }

    private static readonly NavigationDirection[] Directions=[NavigationDirection.North,NavigationDirection.South,
        NavigationDirection.West,NavigationDirection.East,NavigationDirection.NorthWest,NavigationDirection.NorthEast,
        NavigationDirection.SouthWest,NavigationDirection.SouthEast];
}
