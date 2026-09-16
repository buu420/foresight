using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class VehicleNavigationCaptureTests
{
    private const nuint Image = 0x400000, Context = 0x100000, Data = 0x200000,
        Renderer = 0x300000, Actors = 0x500000, EpochActor = 0xB30 + 5 * 0x40, DactylActor = 0xB30 + 9 * 0x40;

    [Fact]
    public void EpochFlightIsReadAtItsOwnTaskBoundary()
    {
        var motion = VehicleNavigationCapture.Motion(Flying(VehicleKind.Epoch), Image, Context, VehicleKind.Epoch, out var stage);
        Assert.NotNull(motion);
        Assert.Equal("ready", stage);
        Assert.Equal(VehicleKind.Epoch, motion.Kind);
        Assert.Equal((int)EpochActor, motion.ActorOffset);
        Assert.Equal(3, motion.ActorState);
        Assert.Equal(520, motion.PixelX);
        Assert.Equal(600, motion.PixelY);
        Assert.True(motion.Wings);
    }

    [Fact]
    public void DactylFlightIsReadAtItsOwnTaskBoundary()
    {
        var motion = VehicleNavigationCapture.Motion(Flying(VehicleKind.Dactyl), Image, Context, VehicleKind.Dactyl, out var stage);
        Assert.NotNull(motion);
        Assert.Equal("ready", stage);
        Assert.Equal(VehicleKind.Dactyl, motion.Kind);
        Assert.Equal((int)DactylActor, motion.ActorOffset);
        Assert.Equal(720, motion.PixelX);
        Assert.Equal(480, motion.PixelY);
        Assert.False(motion.Wings);
    }

    [Theory]
    [InlineData(0x2E27C, 8)]     // time gauge requested: input is not read
    [InlineData(0x2E27C, 0)]     // input disabled during fades and scripts
    [InlineData(0x2E27E, 0)]     // walking: the Epoch task tick is a parked vehicle
    [InlineData(0x2E27E, 4)]     // boarding walk
    [InlineData(0x2E27E, 5)]     // disembarking
    [InlineData(0x2E280, 1)]     // scripted trigger running
    [InlineData(0x20980, 0x80)]  // no player control
    public void ControlPredicatesGateEveryVehicleFrame(int offset, int value)
    {
        var memory = Flying(VehicleKind.Epoch).Byte(Data + (nuint)offset, (byte)value);
        Assert.Null(VehicleNavigationCapture.Motion(memory, Image, Context, VehicleKind.Epoch, out _));
    }

    [Fact]
    public void ParkedRisingLandingOrWinglessEpochIsNotControllableFlight()
    {
        Assert.Null(VehicleNavigationCapture.Motion(Flying(VehicleKind.Epoch).Byte(Data + 0x2E294, 0xA0), Image, Context, VehicleKind.Epoch, out var parked));
        Assert.Equal("vehicle flight state", parked);
        Assert.Null(VehicleNavigationCapture.Motion(Flying(VehicleKind.Epoch).Byte(Data + 0x2E294, 0xC0), Image, Context, VehicleKind.Epoch, out var wingless));
        Assert.Equal("vehicle flight state", wingless);
        foreach (var state in new byte[] { 0, 1, 2, 5, 6, 7, 8 })
        {
            Assert.Null(VehicleNavigationCapture.Motion(Flying(VehicleKind.Epoch).Byte(Data + 0x2E000 + EpochActor + 2, state),
                Image, Context, VehicleKind.Epoch, out var stage));
            Assert.Equal("vehicle actor", stage);
        }
        Assert.NotNull(VehicleNavigationCapture.Motion(Flying(VehicleKind.Epoch).Byte(Data + 0x2E000 + EpochActor + 2, 4), Image, Context, VehicleKind.Epoch, out _));
        Assert.Null(VehicleNavigationCapture.Motion(Flying(VehicleKind.Dactyl).Byte(Data + 0x2E29E, 0xE0), Image, Context, VehicleKind.Dactyl, out var landing));
        Assert.Equal("vehicle flight state", landing);
    }

    [Fact]
    public void TheCurrentActorMustBeTheVehicleTaskNotAnotherScriptActor()
    {
        var wrongActor = Flying(VehicleKind.Epoch).Short(Data + 0x2E04E, 0xB30);
        Assert.Null(VehicleNavigationCapture.Motion(wrongActor, Image, Context, VehicleKind.Epoch, out var stage));
        Assert.Equal("vehicle actor", stage);
        var wrongKind = Flying(VehicleKind.Epoch);
        Assert.Null(VehicleNavigationCapture.Motion(wrongKind, Image, Context, VehicleKind.Dactyl, out _));
        var misaligned = Flying(VehicleKind.Epoch).Short(Data + 0x2E04E, (ushort)(EpochActor + 8));
        Assert.Null(VehicleNavigationCapture.Motion(misaligned, Image, Context, VehicleKind.Epoch, out _));
    }

    [Fact]
    public void ATransitionDuringTheReadInvalidatesTheFlightSample()
    {
        var memory = Flying(VehicleKind.Epoch);
        memory.BeforeRead = address => { if (address == Data + 0x2E290) memory.Byte(Data + 0x2E294, 0xA0); };
        Assert.Null(VehicleNavigationCapture.Motion(memory, Image, Context, VehicleKind.Epoch, out _));
    }

    [Fact]
    public void AFlightSegmentCarriesTheNativeCommittedEndpointAndRejectsAnUnreadableTarget()
    {
        var memory = Flying(VehicleKind.Epoch).Byte(Data + 0x2E000 + EpochActor + 2, 4)
            .Short(Data + 0x2E290, 607).Short(Data + 0x2E292, 209)
            .Short(Data + 0x2E000 + EpochActor + 0x2A, 611).Short(Data + 0x2E000 + EpochActor + 0x2C, 213);
        var motion = VehicleNavigationCapture.Motion(memory, Image, Context, VehicleKind.Epoch, out _);
        Assert.NotNull(motion);
        Assert.Equal(new WorldPixelPoint(611, 213), motion.SegmentEnd);
        Assert.Null(VehicleNavigationCapture.Motion(memory.Short(Data + 0x2E000 + EpochActor + 0x2A, 1536),
            Image, Context, VehicleKind.Epoch, out _));
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(1, 4)]
    [InlineData(2, 16)]
    [InlineData(6, 2)]
    public void BlackOmenRequiresTheCurrentErasNativePresenceBitAndALiveContactTask(int world, byte mask)
    {
        const nuint omenActor = 0xD30;
        var memory = Walking().Word(Context + 0x3324, (uint)world).Word(Renderer + 0x290, (uint)world)
            .Short(Data + 0x2E100, (ushort)(496 + world)).Byte(Data + 0x2FBA6, 212).Byte(Data + 0x2FBAE, mask)
            .Add(Data + 0x2EB30, new byte[64 * 64]).Short(Data + 0x2E000 + omenActor, 0x49CF)
            .Short(Data + 0x2E2A2, 643).Short(Data + 0x2E2A4, 389)
            .Add(Data + 0x34946, [8, 0, 9, 0, 10, 0, 11, 0])
            .Add(Data + 0x3494E, [4, 0, 5, 0, 6, 0, 7, 0])
            .Add(Data + 0x3324C, [1, 0, 2, 0, 3, 0, 4, 0])
            .Add(Data + 0x33254, [5, 0, 6, 0, 7, 0, 8, 0])
            .Add(Data + 0x3325C, [9, 0, 10, 0, 11, 0, 12, 0])
            .Add(Data + 0x34956, [12, 0, 13, 0, 14, 0, 15, 0]);
        var state = VehicleNavigationCapture.State(memory, Image, Context)!;
        Assert.NotNull(state.BlackOmen);
        Assert.Equal((643, 389, (int)omenActor), (state.BlackOmen.X, state.BlackOmen.Y, state.BlackOmen.ActorOffset));
        Assert.Equal(new VehicleContactShape(8, 9, 10, 11), state.EpochShape);
        Assert.Equal(new VehicleContactShape(4, 5, 6, 7), state.DactylShape);
        Assert.Equal(new VehicleContactShape(1, 2, 3, 4), state.PartyBoardingShape);
        Assert.Equal(new VehicleContactShape(5, 6, 7, 8), state.EpochBoardingShape);
        Assert.Equal(new VehicleContactShape(9, 10, 11, 12), state.DactylBoardingShape);
        Assert.Equal(new VehicleContactShape(12, 13, 14, 15), state.BlackOmen.Shape);
        // Stale coordinates and even a stale actor may remain during scene setup;
        // the current era's native spawn condition is also required.
        Assert.Null(VehicleNavigationCapture.State(memory.Byte(Data + 0x2FBAE, 0), Image, Context)!.BlackOmen);
        Assert.Null(VehicleNavigationCapture.State(memory.Byte(Data + 0x2FBAE, mask)
            .Short(Data + 0x2E000 + omenActor, 0), Image, Context)!.BlackOmen);
        Assert.Null(VehicleNavigationCapture.State(memory.Short(Data + 0x2E000 + omenActor, 0x49CF)
            .Byte(Data + 0x2FBA6, 211), Image, Context)!.BlackOmen);
        Assert.Null(VehicleNavigationCapture.State(memory.Byte(Data + 0x2FBA6, 212)
            .Short(Data + 0x2E2A2, 1536), Image, Context)!.BlackOmen);
    }

    [Fact]
    public void ParkedVehicleStateReportsPresenceInThisEraAndTheGamesPromptFlags()
    {
        var memory = Walking().Byte(Data + 0x2E294, 0xA0).Short(Data + 0x2E29F, 496).Short(Data + 0x2E290, 520).Short(Data + 0x2E292, 600)
            .Byte(Data + 0x2E29E, 0x80).Short(Data + 0x2E29A, 720).Short(Data + 0x2E29C, 480)
            .Byte(Actors + 0x109B5, 1).Byte(Actors + 0x109B6, 0);
        var state = VehicleNavigationCapture.State(memory, Image, Context);
        Assert.NotNull(state);
        Assert.True(state.EpochPresent);
        Assert.True(state.EpochWings);
        Assert.Equal((520, 600), (state.EpochX, state.EpochY));
        Assert.False(state.DactylsPresent); // world 0
        Assert.True(state.BoardingPrompt);
        Assert.False(state.LandingPrompt);
        Assert.Equal(0, state.Transport);
        Assert.Equal(1, state.MasterAction);
        Assert.False(VehicleNavigationCapture.State(memory.Short(Data + 0x2E29F, 497), Image, Context)!.EpochPresent);
        var prehistory = memory.Word(Context + 0x3324, 3).Word(Renderer + 0x290, 3).Short(Data + 0x2E100, 499).Short(Data + 0x2E29F, 499);
        var dactyls = VehicleNavigationCapture.State(prehistory, Image, Context);
        Assert.True(dactyls!.DactylsPresent);
        Assert.Equal((720, 480), (dactyls.DactylX, dactyls.DactylY));
        Assert.Null(VehicleNavigationCapture.State(memory.Word(Renderer + 0x290, 1), Image, Context));
    }

    [Fact]
    public void WalkingCaptureCarriesTheVehicleStateAndFlightCaptureCarriesTheVehicleMotion()
    {
        var walking = WorldNavigationCapture.Capture(Full(Walking()).Byte(Data + 0x2E294, 0xA0).Short(Data + 0x2E29F, 496)
            .Short(Data + 0x2E290, 520).Short(Data + 0x2E292, 600), Image, Context, out var stage);
        Assert.Equal("ready", stage);
        Assert.NotNull(walking);
        Assert.Null(walking.Vehicle);
        Assert.True(walking.Vehicles!.EpochPresent);
        Assert.Equal(400, walking.Motion.PixelX);

        var flight = WorldNavigationCapture.CaptureFlight(Full(Flying(VehicleKind.Epoch)), Image, Context, VehicleKind.Epoch, out stage);
        Assert.Equal("ready", stage);
        Assert.NotNull(flight);
        Assert.NotNull(flight.Vehicle);
        Assert.Equal(VehicleKind.Epoch, flight.Vehicle.Kind);
        Assert.Equal(520, flight.Motion.PixelX);
        Assert.Equal(600, flight.Motion.PixelY);
        Assert.Single(flight.Entrances);
        Assert.NotNull(flight.Story);
        Assert.Equal(2, flight.Vehicles!.Transport);
        Assert.Null(WorldNavigationCapture.Capture(Full(Flying(VehicleKind.Epoch)), Image, Context, out _));
        Assert.Null(WorldNavigationCapture.CaptureFlight(Full(Walking()), Image, Context, VehicleKind.Epoch, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorldStoryPredicatesHaveTheSameRosterAndGoldDuringWalkingAndFlight(bool flying)
    {
        var memory = Full(flying ? Flying(VehicleKind.Epoch) : Walking()).Word(Actors + 0x1A24, 3456);
        var party = new[] { 3, 1, 4, 0, 2, 5, 6, 255, 255 };
        for (var i = 0; i < party.Length; i++) memory.Word(Actors + 0x1324Cu + (nuint)(i * 4), (uint)party[i]);
        var snapshot = flying
            ? WorldNavigationCapture.CaptureFlight(memory, Image, Context, VehicleKind.Epoch, out _)
            : WorldNavigationCapture.Capture(memory, Image, Context, out _);
        Assert.NotNull(snapshot);
        Assert.Equal(party, snapshot.Story!.Party);
        Assert.Equal(3456, snapshot.Story.Gold);
    }

    private static NavigationMemory Walking() => new NavigationMemory()
        .Word(Image + 0x41B4BC, (uint)Data).Word(Image + 0x41B4C4, (uint)Actors)
        .Word(Context, (uint)Data).Word(Context + 0x40, (uint)Actors)
        .Word(Context + 0x1E68, (uint)Renderer).Word(Context + 0x3324, 0).Word(Renderer + 0x290, 0)
        .Short(Data + 0x2E100, 496).Short(Data + 0x2E04E, 0xB30)
        .Byte(Data + 0x20980, 0).Byte(Data + 0x2E27C, 1).Byte(Data + 0x2E27E, 0).Byte(Data + 0x2E280, 0)
        .Short(Data + 0x2E283, 400).Short(Data + 0x2E285, 304)
        .Byte(Data + 0x2E294, 0).Short(Data + 0x2E29F, 0).Short(Data + 0x2E290, 0).Short(Data + 0x2E292, 0)
        .Byte(Data + 0x2E29E, 0).Short(Data + 0x2E29A, 0).Short(Data + 0x2E29C, 0)
        .Byte(Actors + 0x109B5, 0).Byte(Actors + 0x109B6, 0);

    private static NavigationMemory Flying(VehicleKind kind)
    {
        var memory = Walking().Byte(Data + 0x2E27E, (byte)kind)
            .Short(Data + 0x2E000 + EpochActor, 0x42DD).Byte(Data + 0x2E000 + EpochActor + 2, 3)
            .Short(Data + 0x2E000 + DactylActor, 0x4CC5).Byte(Data + 0x2E000 + DactylActor + 2, 3)
            .Short(Data + 0x2E000 + EpochActor + 0x2A, 520).Short(Data + 0x2E000 + EpochActor + 0x2C, 600)
            .Short(Data + 0x2E000 + DactylActor + 0x2A, 720).Short(Data + 0x2E000 + DactylActor + 0x2C, 480)
            .Short(Data + 0x2E290, 520).Short(Data + 0x2E292, 600).Short(Data + 0x2E29A, 720).Short(Data + 0x2E29C, 480);
        return kind == VehicleKind.Epoch
            ? memory.Byte(Data + 0x2E294, 0xE0).Short(Data + 0x2E29F, 496).Short(Data + 0x2E04E, (ushort)EpochActor)
            : memory.Word(Context + 0x3324, 3).Word(Renderer + 0x290, 3).Short(Data + 0x2E100, 499)
                .Byte(Data + 0x2E29E, 0xC0).Short(Data + 0x2E04E, (ushort)DactylActor);
    }

    private static NavigationMemory Full(NavigationMemory memory)
    {
        const nuint dll = 0x10000000, director = 0x800000, view = 0x810000, vtable = 0x820000,
            node = 0x830000, children = 0x840000;
        memory.Add(Data + 0x23800, new byte[6144]).Add(Data + 0x25000, new byte[512])
            .Byte(Data + 0x2FB38, 1).Add(Data + 0x25E00, [153, 19, 6, 12, 0, 0, 52, 28])
            .Word(Actors + 0x110B0, 3).Byte(Data + 0x2FBAD, 0)
            .Word(Image + 0x385BBC, (uint)dll + 0x19529E).Word(Image + 0x385B40, (uint)dll + 0x195375)
            .Add(dll + 0x195375, [0x55, 0x8B, 0xEC, 0x8B, 0x49, 0x78, 0x85, 0xC9])
            .Add(dll + 0x24C138, [0x55, 0x8B, 0xEC, 0x83, 0x79, 0x58, 0x01])
            .Word(dll + 0x87D47C, (uint)director).Word(director + 0x78, (uint)view)
            .Word(view, (uint)vtable).Word(vtable + 0x44, (uint)dll + 0x24C138).Word(view + 0x58, 1)
            .Word(Renderer + 0x160, (uint)children).Word(Renderer + 0x164, (uint)children + 4).Word(children, (uint)node);
        Float(view + 0x18, 3834); Float(view + 0x1C, 2087); Float(view + 0x20, 587.8677368f); Float(view + 0x24, 320);
        Float(view + 0x50, 6.52187538f); Float(view + 0x54, 6.52187538f);
        memory.Add(node, new byte[0x1B0]).String(node + 0x178, "worldmap").Word(node + 0x16C, (uint)Renderer).Byte(node + 0x1AD, 1);
        memory.Add(Renderer + 0x20, new byte[0x140]).Word(Renderer + 0x16C, 0).Byte(Renderer + 0x1AD, 1).Byte(Renderer + 0x1AE, 1);
        Float(node + 0x38, 1.875f); Float(node + 0x3C, 1.6666666269f); Float(node + 0x44, 53.9338684f);
        Float(Renderer + 0x38, 1); Float(Renderer + 0x3C, 1); Float(Renderer + 0x64, 293.9338684f); Float(Renderer + 0x68, 160);
        Float(Renderer + 0x2606C, -272); Float(Renderer + 0x26070, 384);
        return memory;
        void Float(nuint address, float value) => memory.Word(address, BitConverter.SingleToUInt32Bits(value));
    }
}
