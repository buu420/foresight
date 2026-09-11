using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class NavigationTextCaptureTests
{
    private const nuint Image = 0x400000, Manager = 0x100000, Banks = 0x110000, World = 0x120000,
        Lines = 0x130000, Actors = 0x200000;

    [Fact]
    public void WorldLabelsUseTheLoadedLocaleAndCurrentCharacterNames()
    {
        var memory = Memory().String(Lines + 3 * 24, "<NAME_CRO>'s House")
            .String(Lines + 6 * 24, "Truce Inn").String(Lines + 109 * 24, "65,000,000 B.C.")
            .String(Actors + 0x1908, "Alex");
        var capture = new NavigationTextCapture(memory);
        Assert.Equal("Alex's House", capture.WorldName(Image, 3));
        Assert.Equal("Truce Inn", capture.WorldName(Image, 6));
        Assert.Equal("65,000,000 B.C.", capture.WorldName(Image, 109));
        Assert.Null(capture.WorldName(Image, 112));
    }

    [Fact]
    public void FieldNamesIdentifyTheirBankByItsOwnMessageKeyAndUseSceneMinusOne()
    {
        var memory = Memory().Word(Banks + 5 * 4, 0x140000).Word(0x140000, 0x150000)
            .Word(0x140004, 0x150000 + 24 * 512).String(0x150000, "MSG_DEBUG_MAP_01,Crono's House\\1F")
            .String(0x150000 + 11 * 24, "MSG_DEBUG_MAP_12,Truce\\Inn");
        Assert.Equal("Truce, Inn", new NavigationTextCapture(memory).FieldName(Image, 12));
    }

    [Fact]
    public void CorruptVectorAndMissingNameNeverFabricateALabel()
    {
        var memory = Memory().String(Lines + 3 * 24, "<NAME_CRO>'s House");
        var capture = new NavigationTextCapture(memory);
        Assert.Null(capture.WorldName(Image, 3));
        memory.Word(World + 4, (uint)Lines + 1);
        Assert.Null(capture.WorldName(Image, 6));
    }

    private static NavigationMemory Memory() => new NavigationMemory()
        .Word(Image + 0x41C3D8, (uint)Manager).Word(Image + 0x41B4C4, (uint)Actors)
        .Word(Manager, (uint)Banks).Word(Manager + 4, (uint)Banks + 71 * 4)
        .Add(Banks, new byte[71 * 4]).Word(Banks + 70 * 4, (uint)World)
        .Word(World, (uint)Lines).Word(World + 4, (uint)Lines + 112 * 24);
}
