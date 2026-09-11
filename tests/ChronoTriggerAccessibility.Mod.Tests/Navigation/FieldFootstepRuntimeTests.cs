using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FieldFootstepRuntimeTests
{
    [Fact]
    public void AutoWalkingAndManualWalkingUseTheSameActualPosition()
    {
        var fixture = new Fixture();
        for (var i = 0; i <= 48; i++) fixture.Tick(i * 32, 0x100);
        Assert.Equal(4, fixture.Plays);
        for (var i = 0; i < 24; i++) fixture.Tick(1536, 0x100);
        Assert.Equal(4, fixture.Plays);
    }

    [Fact]
    public void F8TogglesOnceAndHeldKeyDoesNotToggleAfterFocusReturns()
    {
        var fixture = new Fixture();
        fixture.Tick(0, 0);
        fixture.Keys.Add(0x77);
        fixture.Tick(0, 0); fixture.Tick(0, 0);
        Assert.Equal(new[] { "Footsteps off." }, fixture.Speech);
        fixture.Foreground = false; fixture.Tick(0, 0);
        fixture.Foreground = true; fixture.Tick(0, 0);
        Assert.Single(fixture.Speech);
        fixture.Keys.Clear(); fixture.Tick(0, 0);
        fixture.Keys.Add(0x77); fixture.Tick(0, 0);
        Assert.Equal("Footsteps on.", fixture.Speech[^1]);
    }

    [Fact]
    public void MissingControlAndSuspensionDiscardPartialMovement()
    {
        var fixture = new Fixture();
        for (var i = 0; i <= 11; i++) fixture.Tick(i * 32, 0x100);
        fixture.Runtime.Suspend();
        fixture.Tick(384, 0x100);
        Assert.Equal(0, fixture.Plays);
        fixture.Available = false;
        fixture.Tick(1000, 0x100);
        fixture.Available = true;
        fixture.Tick(1000, 0x100);
        Assert.Equal(0, fixture.Plays);
    }

    [Fact]
    public void FootstepFailureDoesNotEscapeIntoTheGameInputHook()
    {
        var runtime = new FieldFootstepRuntime(_ => throw new IOException("test"), () => true,
            _ => false, () => 0, () => { }, () => { }, _ => { }, _ => { });
        runtime.Enable();
        runtime.OnInput(1, 0x100);
        runtime.OnInput(1, 0x100);
    }

    [Fact]
    public void EmbeddedBankContainsFivePlayablePcmWaves()
    {
        for (var i = 1; i <= 5; i++)
        {
            using var stream = typeof(FootstepSound).Assembly.GetManifestResourceStream(
                $"ChronoTriggerAccessibility.Mod.Audio.footstep-{i}.wav");
            Assert.NotNull(stream);
            using var reader = new BinaryReader(stream);
            Assert.Equal("RIFF", new string(reader.ReadChars(4)));
            reader.ReadInt32();
            Assert.Equal("WAVEfmt ", new string(reader.ReadChars(8)));
            Assert.Equal(16, reader.ReadInt32());
            Assert.Equal(1, reader.ReadInt16());
            Assert.Equal(1, reader.ReadInt16());
            Assert.Equal(48000, reader.ReadInt32());
            Assert.InRange(stream.Length, 10000L, 24044L);
        }
    }

    [Fact]
    public void FastRunningLeavesTimeForEachRecordingToFinish()
    {
        var fixture = new Fixture();
        for (var i = 0; i < 192; i++) fixture.Tick(i * 32, 0x100, 16);
        Assert.True(fixture.PlayTimes.Count >= 8);
        using var stream = typeof(FootstepSound).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Audio.footstep-1.wav")!;
        var duration = (stream.Length - 44) / 96.0;
        Assert.All(fixture.PlayTimes.Zip(fixture.PlayTimes.Skip(1)), pair =>
            Assert.True(pair.Second - pair.First > duration));
    }

    private sealed class Fixture
    {
        public readonly HashSet<int> Keys = [];
        public readonly List<string> Speech = [];
        public readonly FieldFootstepRuntime Runtime;
        public bool Foreground = true, Available = true;
        public int Plays;
        public readonly List<long> PlayTimes = [];
        private int x;
        private long now;
        public Fixture()
        {
            Runtime = new(_ => Available ? new FootstepFrame(1, 1, 1, x, 0) : null,
                () => Foreground, Keys.Contains, () => now, () => { Plays++; PlayTimes.Add(now); }, () => { }, Speech.Add, _ => { });
            Runtime.Enable();
        }
        public void Tick(int position, uint input, int interval = 33) { x = position; now += interval; Runtime.OnInput(1, input); }
    }
}
