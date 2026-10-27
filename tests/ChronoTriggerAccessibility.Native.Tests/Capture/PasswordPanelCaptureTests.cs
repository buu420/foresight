using System.Buffers.Binary;
using System.Security.Cryptography;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>
/// OWNED-MEMORY SCAFFOLDING: every address below is a synthetic owner laid out the way the
/// audited native writers leave it (dispatcher context like StoryActionCapture's; VirtualPad as
/// written by ctor 192C60, scene-4 builder 191F90, show 1929C0 and hide 192B00; nsInput manager
/// vtable 3A5D0C). The numbers are literals here on purpose, so the tests do not reuse the
/// constants of the class under test.
/// ACTUAL DATA EVIDENCE: the two InstalledConsole* tests read Atel_0026 from the supported
/// installed resources.bin (SHA-256 checked by InstalledFieldScripts). Nothing is copied.
/// </summary>
public sealed class PasswordPanelCaptureTests
{
    private const uint Image = 0x400000;
    private const uint Context = 0x1000, Data = 0xA0000, Actors = 0x200000, Field = 0x5000, Renderer = 0x7000;
    private const uint Pad = 0x300000, Layer = 0x310000, Manager = 0x320000;
    private const uint NoFocus = 0x80000000;

    [Fact]
    public void AShownPanelReportsItsFocusLanguageAndScriptedControl()
    {
        var frame = PasswordPanelCapture.Capture(Panel(), Image, Context);
        Assert.Equal(new PasswordPanelFrame(PasswordPanelState.Shown, Context, Field, 4, Pad, Manager, 0, 1, false), frame);
    }

    [Fact]
    public void AHiddenPanelAfterARejectedLetterReportsRestoredControl()
    {
        var frame = PasswordPanelCapture.Capture(Panel(shown: false, control: 1, focus: 8), Image, Context);
        Assert.Equal(new PasswordPanelFrame(PasswordPanelState.Hidden, Context, Field, 4, Pad, Manager, 8, 1, true), frame);
    }

    [Fact]
    public void TheGapAfterAnAcceptedLetterIsHiddenWithScriptedControl()
    {
        // 192B00 hid the panel; actor 12 is between E8 04 and AD 04, so E3 00 still holds.
        var frame = PasswordPanelCapture.Capture(Panel(shown: false, control: 0), Image, Context);
        Assert.Equal(PasswordPanelState.Hidden, frame?.State);
        Assert.False(frame?.ControlRestored);
    }

    [Fact]
    public void ThePresentDayArmedPanelIsHiddenNeverShown()
    {
        // The present-day house builds the same panel (190BD0 case 4) but never runs actor 12's 2D.
        var frame = PasswordPanelCapture.Capture(Panel(shown: false, control: 1), Image, Context);
        Assert.Equal(PasswordPanelState.Hidden, frame?.State);
    }

    [Fact]
    public void AnotherCoherentSceneIsAbsentWithoutClaimingAPanel()
    {
        var frame = PasswordPanelCapture.Capture(Panel(scene: 10), Image, Context);
        Assert.Equal(new PasswordPanelFrame(PasswordPanelState.Absent, Context, Field, 10, 0, 0, null, -1, false), frame);
    }

    [Fact]
    public void SceneFourWithoutAPublishedPadIsAbsent()
    {
        var memory = Panel();
        memory.Word(Image + 0x41C3E0, 0); // 2BBAB0 onExit cleared the current stick
        Assert.Equal(PasswordPanelState.Absent, PasswordPanelCapture.Capture(memory, Image, Context)?.State);
    }

    public static TheoryData<string> IncoherentOwners => new()
    {
        "renderer scene", "pad scene", "global actors", "script id", "script header", "context field",
    };

    [Theory]
    [MemberData(nameof(IncoherentOwners))]
    public void IncoherentOrForeignOwnersAreUnknown(string mutation)
    {
        var memory = Panel();
        switch (mutation)
        {
            case "renderer scene": memory.Word(Renderer + 0x2A0, 3); break;   // 1597E0 has not committed the map
            case "pad scene": memory.Word(Actors + 0x120C0, 3); break;        // the 2BBB20 create argument disagrees
            case "global actors": memory.Word(Image + 0x41B4C4, Actors + 0x1000); break;
            case "script id": memory.Word(Context + 0xBB4, 25); break;        // scene 4 always runs Atel_0026
            case "script header": memory.Byte(Data + 0x12000, 12); break;
            case "context field": memory.Word(Context + 0x850, 0); break;
        }
        Assert.Null(PasswordPanelCapture.Capture(memory, Image, Context));
    }

    public static TheoryData<string> ForeignPads => new()
    {
        "pad vtable", "stick vtable", "generic manager", "pad type", "deferred build", "not armed",
        "mask table", "no layer", "no manager", "manager vtable", "stick below interface",
    };

    [Theory]
    [MemberData(nameof(ForeignPads))]
    public void APadThatIsNotTheBuiltSceneFourPanelIsUnknown(string mutation)
    {
        var memory = Panel();
        switch (mutation)
        {
            case "pad vtable": memory.Word(Pad, Image + 0x3A1228); break;              // VirtualStick4
            case "stick vtable": memory.Word(Pad + 0x290, Image + 0x3A120C); break;
            case "generic manager": memory.Word(Pad + 0x2C4, 0x330000); break;        // 590DE0 built a typed pad
            case "pad type": memory.Word(Pad + 0x2C8, 3); break;                      // 216/217/643 passcode pad
            case "deferred build": memory.Byte(Pad + 0x2CC, 1); break;
            case "not armed": memory.Byte(Pad + 0x2D8, 0); break;
            case "mask table": memory.Word(Pad + 0x2DC, Image + 0x41EDB8 + 0x30); break;
            case "no layer": memory.Word(Pad + 0x2D0, 0); break;
            case "no manager": memory.Word(Pad + 0x2D4, 0); break;
            case "manager vtable": memory.Word(Manager, Image + 0x3A5D10); break;
            case "stick below interface": memory.Word(Image + 0x41C3E0, 0x100); break;
        }
        Assert.Null(PasswordPanelCapture.Capture(memory, Image, Context));
    }

    public static TheoryData<bool, string> DisagreeingVisibility => new()
    {
        { true, "layer hidden" }, { true, "manager locked" }, { true, "pad node hidden" }, { true, "flag not boolean" },
        { false, "layer visible" }, { false, "manager unlocked" },
    };

    [Theory]
    [MemberData(nameof(DisagreeingVisibility))]
    public void VisibilityThatDoesNotAgreeIsUnknown(bool shown, string mutation)
    {
        var memory = Panel(shown: shown);
        switch (mutation)
        {
            case "layer hidden": memory.Byte(Layer + 0x1AD, 0); break;
            case "manager locked": memory.Byte(Manager + 0x290, 1); break;
            case "pad node hidden": memory.Byte(Pad + 0x1AD, 0); break;
            case "flag not boolean": memory.Byte(Pad + 0x2D9, 2); break;
            case "layer visible": memory.Byte(Layer + 0x1AD, 1); break;
            case "manager unlocked": memory.Byte(Manager + 0x290, 0); break;
        }
        Assert.Null(PasswordPanelCapture.Capture(memory, Image, Context));
    }

    [Theory]
    [InlineData(10u)]
    [InlineData(0xFFFFFFFFu)]
    [InlineData(0x7FFFFFFFu)]
    public void AShownFocusOutsideTheGridIsUnknown(uint key) =>
        Assert.Null(PasswordPanelCapture.Capture(Panel(focus: key), Image, Context));

    [Fact]
    public void AShownPanelWithoutNativeFocusHasNoFocusKey()
    {
        var frame = PasswordPanelCapture.Capture(Panel(focus: NoFocus), Image, Context);
        Assert.Equal(PasswordPanelState.Shown, frame?.State);
        Assert.Null(frame?.FocusKey);
    }

    [Theory]
    [InlineData(9u)]
    [InlineData(0xFFFFFFFFu)]
    public void AnUnsupportedLanguageIsUnknown(uint language) =>
        Assert.Null(PasswordPanelCapture.Capture(Panel(language: language), Image, Context));

    [Fact]
    public void AnOwnerThatChangesDuringTheReadIsRejected()
    {
        var memory = Panel();
        var reads = 0;
        // The second read of the manager pointer (the identity reread) sees a different owner.
        memory.BeforeRead = address =>
        {
            if (address == Pad + 0x2D4 && ++reads == 2) memory.Word(Pad + 0x2D4, Manager + 0x1000);
        };
        Assert.Null(PasswordPanelCapture.Capture(memory, Image, Context));
        Assert.True(reads >= 2);
    }

    [Fact]
    public void UnreadableOrMissingOwnersAreUnknown()
    {
        Assert.Null(PasswordPanelCapture.Capture(new NavigationMemory(), Image, Context));
        Assert.Null(PasswordPanelCapture.Capture(Panel(), 0, Context));
        Assert.Null(PasswordPanelCapture.Capture(Panel(), Image, 0));
    }

    [Theory]
    [InlineData(1, 0, "F")]
    [InlineData(1, 1, "R")]
    [InlineData(1, 4, "Z")]
    [InlineData(1, 5, "A")]
    [InlineData(1, 8, "L")]
    [InlineData(1, 9, "I")]
    [InlineData(2, 1, "R")] // de
    [InlineData(3, 1, "R")] // it
    [InlineData(4, 1, "R")] // es
    [InlineData(5, 1, "R")] // fr
    [InlineData(8, 1, "R")] // ko
    [InlineData(0, 1, "Q")] // ja
    [InlineData(6, 1, "Q")] // zh-Hans
    [InlineData(7, 1, "Q")] // zh-Hant
    [InlineData(0, 8, "L")]
    public void TheVisibleGlyphComesFromTheLanguagesAtlas(int language, int key, string expected) =>
        Assert.Equal(expected, PasswordPanelCapture.Glyph(language, key));

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(9, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 10)]
    public void NoGlyphOutsideTheProvenGrid(int language, int key) =>
        Assert.Null(PasswordPanelCapture.Glyph(language, key));

    [Fact]
    public void InstalledConsoleScriptMatchesThePinnedHeader()
    {
        var script = InstalledFieldScripts.Atel(26);
        Assert.Equal(1516, script.Length);
        Assert.Equal("95BBD0916EC4652CA22175A5DCE4576208D296765D4905A2E8385BB377F55F4D", Convert.ToHexString(SHA256.HashData(script)));
        Assert.Equal(PasswordPanelCapture.ScriptHeaderHex, Convert.ToHexString(script, 0, 16));
    }

    [Fact]
    public void InstalledConsoleRestoresControlOnlyOnARejectedLetter()
    {
        var script = InstalledFieldScripts.Atel(26);
        // Header: byte 0 actor count, then 16 u16 function pointers per actor; pointer v -> file v+1.
        Assert.Equal(13, script[0]);
        int Function(int actor, int fn) => BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(1 + actor * 32 + fn * 2)) + 1;
        var activate = Function(12, 1);
        var end = Function(12, 2);
        Assert.Equal(0x550, activate);
        Assert.Equal(0x5EB, end);
        // E3 00 then BB 17 00 (MESK3_023 "Enter password."): scripted control for the whole entry.
        Assert.Equal("E300BB1700", Convert.ToHexString(script, activate, 5));
        var restores = new List<int>();
        for (var offset = activate; offset + 1 < end; offset++)
            if (script[offset] == 0xE3 && script[offset + 1] == 0x01) restores.Add(offset);
        // Exactly the five rejection returns: E8 01 (sound 1), E3 01, 00 return.
        Assert.Equal([0x561, 0x574, 0x590, 0x5A1, 0x5B4], restores);
        foreach (var offset in restores) Assert.Equal("E801E30100", Convert.ToHexString(script, offset - 2, 5));
        // Every accepted letter plays E8 04 and jumps on without touching control.
        foreach (var offset in new[] { 0x559, 0x56C, 0x588, 0x599, 0x5AC })
            Assert.Equal("E80410", Convert.ToHexString(script, offset, 3));
    }

    private static NavigationMemory Panel(int scene = 4, bool shown = true, uint focus = 0, uint language = 1, uint control = 0)
    {
        var m = new NavigationMemory();
        // Dispatcher context and its owners.
        m.Add(Context, new byte[0xBB8]);
        m.Word(Context, Data);
        m.Word(Context + 0x40, Actors);
        m.Word(Context + 0x850, Field);
        m.Word(Context + 0xB9C, Renderer);
        m.Word(Context + 0xBB4, 26);
        m.Add(Field + 0x1000, new byte[0x200]);
        m.Word(Field + 0x1010, (uint)scene);
        m.Word(Field + 0x108C, control);
        m.Add(Renderer + 0x290, new byte[0x20]);
        m.Word(Renderer + 0x2A0, (uint)scene);
        m.Word(Image + 0x41B4C4, Actors);
        m.Word(Actors + 0x120C0, (uint)scene);
        m.Add(Data + 0x12000, Convert.FromHexString("0DA00145025202520252025202520252"));
        m.Word(Image + 0x41C3E0, Pad + 0x290);
        m.Word(Image + 0x3FA168, language);
        // VirtualPad after 192C60 + 191F90 (+ 590DE0 type 0) and show/hide.
        m.Add(Pad, new byte[0x2E0]);
        m.Word(Pad, Image + 0x3A15B0);
        m.Word(Pad + 0x290, Image + 0x3A1594);
        m.Byte(Pad + 0x1AD, 1);
        m.Word(Pad + 0x2D0, Layer);
        m.Word(Pad + 0x2D4, Manager);
        m.Byte(Pad + 0x2D8, 1);
        m.Byte(Pad + 0x2D9, (byte)(shown ? 1 : 0));
        m.Word(Pad + 0x2DC, Image + 0x41EDB8);
        m.Add(Layer, new byte[0x1B0]);
        m.Byte(Layer + 0x1AD, (byte)(shown ? 1 : 0));
        m.Add(Manager, new byte[0x2D0]);
        m.Word(Manager, Image + 0x3A5D0C);
        m.Byte(Manager + 0x290, (byte)(shown ? 0 : 1));
        m.Word(Manager + 0x2C4, focus);
        return m;
    }
}
