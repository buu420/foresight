using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class BikeRaceCaptureTests
{
    private const uint Image = 0x400000, Scene = 0x900000, Data = 0xA00000;
    private static NavigationMemory Frame() => new NavigationMemory()
        .Add(Scene, new byte[0x5248]).Add(Data + 0x2E040, new byte[0x80])
        .Word(Scene, Image + 0x3B49C8).Word(Scene + 4, Data).Word(Image + 0x41B4BC, Data)
        .Word(Scene + 0xC04, 1).Short(Data + 0x2E380, 0).Byte(Data + 0x2E392, 0)
        .Short(Data + 0x2E04E, 0x6000).Short(Data + 0x2E073, 0x2000)
        .Word(Data + 0x2E051, 0x20000).Word(Data + 0x2E076, 0x21000)
        .Short(Data + 0x2E0BC, 100).Word(Scene + 0x4F04, 720).Word(Scene + 0x4EB8, 15)
        .Word(Scene + 0x4EFC, 3).Word(Scene + 0x4F00, 0);

    [Fact] public void ReadsRenderedHudAndVisibleTrackPosition()
    {
        var frame = new BikeRaceCapture(Frame()).Capture(Image, Scene);
        Assert.NotNull(frame); Assert.Equal(BikeRacePhase.Racing, frame.Phase);
        Assert.Equal(720, frame.Distance); Assert.Equal(15, frame.Score);
        Assert.Equal(3, frame.Boosts); Assert.True(frame.BoostReady);
        Assert.Equal(-1, frame.Lead); Assert.Equal(BikeRaceLane.Above, frame.JohnnyLane);
    }
    [Theory]
    [InlineData(0x1000, BikeRaceLane.Below)]
    [InlineData(0x2000, BikeRaceLane.Aligned)]
    [InlineData(0x6000, BikeRaceLane.Above)]
    public void ToneDirectionMatchesNativeOverview(int lane, BikeRaceLane expected)
    {
        var memory = Frame().Short(Data + 0x2E04E, (ushort)lane);
        Assert.Equal(expected, new BikeRaceCapture(memory).Capture(Image, Scene)!.JohnnyLane);
    }
    [Theory] [InlineData(1, BikeRaceResult.Won)] [InlineData(2, BikeRaceResult.Lost)]
    public void ResultLatchEndsRaceEvenWhenFinishAnimationFlagIsNotSet(int result, BikeRaceResult expected)
    {
        var memory = Frame().Short(Data + 0x2E380, (ushort)result);
        var frame = new BikeRaceCapture(memory).Capture(Image, Scene)!;
        Assert.Equal(BikeRacePhase.Finished, frame.Phase); Assert.Equal(expected, frame.Result);
    }
    [Fact] public void NoBoostModeDoesNotExposeStaleBoostIndicators()
    {
        var frame = new BikeRaceCapture(Frame().Byte(Scene + 0xC10, 1).Word(Scene + 0x4EFC, 999))
            .Capture(Image, Scene)!;
        Assert.False(frame.BoostsEnabled); Assert.False(frame.BoostReady);
    }
    [Fact] public void ReadsPauseAndDoesNotStartBeforeTheNativeRaceLoopRuns()
    {
        var frame = new BikeRaceCapture(Frame().Byte(Data + 0x2E392, 1).Short(Data + 0x2E0BC, 0))
            .Capture(Image, Scene)!;
        Assert.True(frame.Paused); Assert.Equal(BikeRacePhase.Preparing, frame.Phase);
    }
    [Fact] public void RejectsOtherSceneUninitializedSceneAndForeignEngine()
    {
        Assert.Null(new BikeRaceCapture(Frame().Word(Scene, 0)).Capture(Image, Scene));
        Assert.Null(new BikeRaceCapture(Frame().Word(Scene + 0xC04, 0)).Capture(Image, Scene));
        Assert.Null(new BikeRaceCapture(Frame().Word(Scene + 4, Data + 4)).Capture(Image, Scene));
    }
    [Fact] public void RejectsTornEnginePointerAndInvalidHudInsteadOfAnnouncingInventedValues()
    {
        var memory = Frame(); var reads = 0;
        memory.BeforeRead = p => { if (p == Image + 0x41B4BC && ++reads == 2) memory.Word(p, Data + 4); };
        Assert.Null(new BikeRaceCapture(memory).Capture(Image, Scene));
        Assert.Null(new BikeRaceCapture(Frame().Word(Scene + 0x4F04, uint.MaxValue)).Capture(Image, Scene));
        Assert.Null(new BikeRaceCapture(new NavigationMemory()).Capture(Image, Scene));
    }
}
