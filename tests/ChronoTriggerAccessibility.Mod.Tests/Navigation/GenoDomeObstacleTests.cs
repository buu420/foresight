using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class GenoDomeObstacleTests
{
    [Theory]
    [InlineData(23,41,1,"Southern")]
    [InlineData(5,12,4,"Western")]
    [InlineData(21,15,2,"Northern")]
    public void AVisibleReceiverDoorOpeningIsAnnouncedOnce(int x,int y,int flag,string direction)
    {
        var sample=GenoDomeRouteTests.Live(2179);var said=new List<string>();
        var feedback=new GenoDomeFeedback(said.Add);feedback.Enable();
        feedback.Observe(sample.Field,sample.Map,sample.Story,View);said.Clear();
        var map=Copy(sample.Map,60,60,3,3,x,y);var story=Flag(sample.Story,0x13E,flag);
        feedback.Observe(sample.Field,map,story,View);
        Assert.Equal(direction+" door opened.",Assert.Single(said));
        feedback.Observe(sample.Field,map,story,View);Assert.Single(said);
    }

    [Fact]
    public void TheVisibleLaserBarrierReportsBothStatesWithoutInferringItsLampSense()
    {
        var sample=GenoDomeRouteTests.Live(2179);var said=new List<string>();
        var feedback=new GenoDomeFeedback(said.Add);feedback.Enable();
        feedback.Observe(sample.Field,sample.Map,sample.Story,View);said.Clear();
        var open=Copy(sample.Map,58,55,1,3,38,17);var story=Flag(sample.Story,0x13D,8);
        feedback.Observe(sample.Field,open,story,View);
        Assert.Equal("Laser barrier is inactive.",Assert.Single(said));
        feedback.Observe(sample.Field,sample.Map,sample.Story,View);
        Assert.Equal("Laser barrier is active.",said.Last());Assert.Equal(2,said.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AHiddenOrIncompleteDoorChangeDoesNotSpeak(bool offscreen)
    {
        var sample=GenoDomeRouteTests.Live(2179);var said=new List<string>();
        var feedback=new GenoDomeFeedback(said.Add);feedback.Enable();
        feedback.Observe(sample.Field,sample.Map,sample.Story,View);said.Clear();
        var map=offscreen?Copy(sample.Map,60,60,3,3,23,41):sample.Map;
        feedback.Observe(sample.Field,map,Flag(sample.Story,0x13E,1),offscreen?new(0,0,32,32):View);
        Assert.Empty(said);
    }

    [Fact]
    public void AUsedReceiverDoesNotRequestAnotherChargeDelivery()
    {
        var sample=GenoDomeRouteTests.Live(2179);
        var frame=Build(sample.Field,Copy(sample.Map,60,60,3,3,23,41),Flag(sample.Story,0x13E,1));
        var target=Assert.Single(frame.Targets,t=>t.Id=="landmark:26");
        Assert.DoesNotContain("Walk inside with an electrical charge",target.Instruction!);
        Assert.Contains("Southern door is open",target.Instruction!);
    }

    [Fact]
    public void MissingNativePatternsDoNotInventDoorOrBarrierState()
    {
        var sample=GenoDomeRouteTests.Live(2179);var said=new List<string>();
        var feedback=new GenoDomeFeedback(said.Add);feedback.Enable();
        feedback.Observe(sample.Field,sample.Map,sample.Story,View);said.Clear();
        var map=sample.Map with{CollisionShapes=new byte[4096],TerrainFlags=new byte[4096],CollisionLayers=new byte[4096]};
        var story=Flag(sample.Story,0x13E,1);
        feedback.Observe(sample.Field,map,story,View);Assert.Empty(said);
        var control=Assert.Single(Build(sample.Field,map,story).Targets,t=>t.Id=="landmark:13");
        Assert.DoesNotContain("is active",control.Instruction??"");
    }

    [Fact]
    public void SelectingTheVisibleLaserControlDescribesTheActualBarrier()
    {
        var frame=GenoDomeRouteTests.Live(2179).Frame;
        Assert.Contains("Laser barrier is active",Assert.Single(frame.Targets,t=>t.Id=="landmark:13").Instruction!);
    }

    [Theory]
    [InlineData(2179,"chest:140","Northern door")]
    [InlineData(2242,"chest:141","Northern door")]
    [InlineData(2575,"chest:140","Northern door")]
    [InlineData(2605,"exit:1","Southern door")]
    [InlineData(2503,"landmark:48","Laser barrier")]
    public void TheReportedUnavailableRoutesExplainAVerifiedObstacle(int line,string id,string obstacle)
    {
        foreach(var automatic in new[]{false,true})
        {
            var frame=GenoDomeRouteTests.Live(line).Frame;
            var result=VortexRoomGeometryTests.Start(frame,id,automatic);
            Assert.False(result.Guiding);Assert.Contains(result.Speech,s=>s.Contains(obstacle,StringComparison.Ordinal));
        }
    }

    internal static FieldMapSnapshot Copy(FieldMapSnapshot source,int sx,int sy,int width,int height,int dx,int dy)
    {
        var result=source with{CollisionShapes=(byte[])source.CollisionShapes.Clone(),TerrainFlags=(byte[])source.TerrainFlags.Clone(),
            CollisionLayers=(byte[])source.CollisionLayers.Clone()};
        foreach(var (from,to) in new[]{(source.CollisionShapes,result.CollisionShapes),(source.TerrainFlags,result.TerrainFlags),
                    (source.CollisionLayers,result.CollisionLayers)})
            for(var y=0;y<height;y++)for(var x=0;x<width;x++)to[(dy+y)*source.Width+dx+x]=from[(sy+y)*source.Width+sx+x];
        return result;
    }
    private static FieldStoryState Flag(FieldStoryState story,int index,int flag)
    {
        var globals=story.Globals.ToDictionary();globals[index]=globals.GetValueOrDefault(index)|flag;
        return story with{Globals=globals};
    }
    private static readonly FieldViewport View=new(0,0,16384,16384);
    private static ChronoTriggerAccessibility.Core.Navigation.NavigationFrame Build(FieldNavigationSnapshot f,FieldMapSnapshot m,FieldStoryState s)=>
        new FieldNavigationSource(new Empty(),_=>{}).Build(f,m,View,[],s);
    private sealed class Empty:IReadableMemory{public bool TryRead(nuint address,Span<byte> bytes)=>false;}
}
