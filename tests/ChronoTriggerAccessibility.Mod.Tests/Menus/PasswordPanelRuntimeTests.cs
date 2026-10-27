using ChronoTriggerAccessibility.Mod.Menus;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Menus;

/// <summary>Frame sequences as PasswordPanelCapture proves them after each field-script
/// instruction. Owners, focus keys and languages are synthetic; the glyphs come from the
/// capture's atlas-proven table.</summary>
public sealed class PasswordPanelRuntimeTests
{
    private const string LatinPanel = "Password panel. Row 1: F, R, O, C, Z. Row 2: A, W, T, L, I.";
    private const string Closed = "Password panel closed.";

    [Fact]
    public void AFirstShownPanelSpeaksItsTitleBothRowsAndTheFocusedLetter()
    {
        var h = new Harness();
        h.Run(Shown(0));
        Assert.Equal([$"{LatinPanel} F, row 1, column 1."], h.Speech);
    }

    [Fact]
    public void FocusMovesSpeakTheLetterRowAndColumnOnlyWhenTheFocusChanges()
    {
        var h = new Harness();
        h.Run(Shown(0), Shown(0), Shown(1), Shown(1), Shown(6), Shown(9));
        Assert.Equal([$"{LatinPanel} F, row 1, column 1.", "R, row 1, column 2.", "W, row 2, column 2.", "I, row 2, column 5."], h.Speech);
    }

    [Fact]
    public void TheHiddenGapAfterAnAcceptedLetterIsSilentAndKeepsTheSession()
    {
        var h = new Harness();
        // 192B00 hides the panel while actor 12 plays E8 04 and waits AD 04; E3 00 still holds.
        h.Run(Shown(8), Hidden(controlRestored: false), Hidden(controlRestored: false), Shown(8), Shown(5));
        Assert.Equal([$"{LatinPanel} L, row 2, column 4.", "A, row 2, column 1."], h.Speech);
    }

    [Fact]
    public void ARejectedLetterClosesThePanelOnceWhenNativeControlReturns()
    {
        var h = new Harness();
        // A rejected letter hides the panel, then E3 01 restores player control.
        h.Run(Shown(0), Hidden(controlRestored: false), Hidden(controlRestored: true), Hidden(controlRestored: true));
        Assert.Equal([$"{LatinPanel} F, row 1, column 1.", Closed], h.Speech);
    }

    [Fact]
    public void ExaminingTheConsoleAgainIsANewSession()
    {
        var h = new Harness();
        h.Run(Shown(0), Hidden(controlRestored: true), Hidden(controlRestored: false), Shown(0));
        Assert.Equal([$"{LatinPanel} F, row 1, column 1.", Closed, $"{LatinPanel} F, row 1, column 1."], h.Speech);
    }

    [Fact]
    public void ThePresentDayHiddenPanelIsSilent()
    {
        var h = new Harness();
        h.Run(Hidden(controlRestored: true), Hidden(controlRestored: false), Hidden(controlRestored: true));
        Assert.Empty(h.Speech);
    }

    [Fact]
    public void SuccessOrTimeoutEndsSilentlyWhenTheSceneIsLeft()
    {
        var h = new Harness();
        // The fourth letter hides the panel without E3 01; the reunion or timeout changes scene.
        h.Run(Shown(5), Hidden(controlRestored: false), Absent(), Hidden(controlRestored: true));
        Assert.Equal([$"{LatinPanel} A, row 2, column 1."], h.Speech);
        h.Run(Shown(5));
        Assert.Equal(2, h.Speech.Count);
        Assert.Equal($"{LatinPanel} A, row 2, column 1.", h.Speech[1]);
    }

    [Fact]
    public void AnOwnerChangeResetsSilently()
    {
        var h = new Harness();
        h.Run(Shown(0, owner: 1), Shown(0, owner: 2));
        Assert.Equal([$"{LatinPanel} F, row 1, column 1.", $"{LatinPanel} F, row 1, column 1."], h.Speech);
        var foreign = new Harness();
        foreign.Run(Shown(0, owner: 1), Hidden(controlRestored: true, owner: 2));
        Assert.Equal([$"{LatinPanel} F, row 1, column 1."], foreign.Speech);
    }

    [Fact]
    public void ForegroundLossResetsAndTheReturnDescribesThePanelAgain()
    {
        var h = new Harness();
        h.Run(Shown(0));
        h.Foreground = false;
        h.Run(Shown(1), Hidden(controlRestored: true));
        h.Foreground = true;
        h.Run(Shown(1));
        Assert.Equal([$"{LatinPanel} F, row 1, column 1.", $"{LatinPanel} R, row 1, column 2."], h.Speech);
    }

    [Fact]
    public void UnknownFramesNeitherCloseNorReset()
    {
        var h = new Harness();
        h.Run(Shown(0), null, Hidden(controlRestored: false), null, Shown(0));
        Assert.Equal([$"{LatinPanel} F, row 1, column 1."], h.Speech);
        h.Run(null, Hidden(controlRestored: true));
        Assert.Equal([$"{LatinPanel} F, row 1, column 1.", Closed], h.Speech);
    }

    [Fact]
    public void EastAsianLanguagesShowQWhereLatinLanguagesShowR()
    {
        var h = new Harness();
        h.Run(Shown(1, language: 0));
        Assert.Equal(["Password panel. Row 1: F, Q, O, C, Z. Row 2: A, W, T, L, I. Q, row 1, column 2."], h.Speech);
    }

    [Fact]
    public void APanelWithoutNativeFocusIsStillDescribed()
    {
        var h = new Harness();
        h.Run(Shown(null), Shown(null), Shown(3));
        Assert.Equal([LatinPanel, "C, row 1, column 4."], h.Speech);
    }

    [Fact]
    public void SpeechNeverJudgesTheEntryOrRevealsTheAnswer()
    {
        var h = new Harness();
        h.Run(Shown(0), Shown(8), Hidden(false), Shown(8), Shown(5), Hidden(false), Shown(5), Shown(1),
            Hidden(false), Shown(1), Shown(5), Hidden(false), Absent(), Shown(4), Hidden(true));
        Assert.NotEmpty(h.Speech);
        foreach (var text in h.Speech)
        {
            foreach (var forbidden in new[] { "correct", "wrong", "accept", "reject", "right", "LARA", "Lara", "L, A, R, A", "entered" })
                Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NativeExecutionRunsOnceBeforeThePollEvenWhenObservationFails()
    {
        var log = new List<string>();
        var diagnostics = new List<string>();
        var runtime = new PasswordPanelRuntime((_, _) => { log.Add("capture"); throw new InvalidOperationException(); },
            () => true, text => log.Add("speak " + text), diagnostics.Add);
        runtime.Enable();
        runtime.Dispatch(1, 0x2D, () => log.Add("original"));
        Assert.Equal(["original", "capture"], log);
        Assert.Single(diagnostics);
    }

    [Fact]
    public void NothingIsCapturedWhileDisabled()
    {
        var h = new Harness();
        h.Runtime.Disable();
        h.Next.Enqueue(Shown(0));
        var called = 0;
        h.Runtime.Dispatch(1, 0x2D, () => called++);
        Assert.Equal(1, called);
        Assert.Equal(0, h.Captures);
        Assert.Empty(h.Speech);
    }

    private static PasswordPanelFrame Shown(int? focus, int language = 1, uint owner = 1) =>
        new(PasswordPanelState.Shown, owner, 0x5000, 4, 0x300000 + owner, 0x320000 + owner, focus, language, false);

    private static PasswordPanelFrame Hidden(bool controlRestored, uint owner = 1) =>
        new(PasswordPanelState.Hidden, owner, 0x5000, 4, 0x300000 + owner, 0x320000 + owner, 0, 1, controlRestored);

    private static PasswordPanelFrame Absent() => new(PasswordPanelState.Absent, 1, 0x5000, 10, 0, 0, null, -1, false);

    private sealed class Harness
    {
        public List<string> Speech { get; } = [];
        public List<string> Diagnostics { get; } = [];
        public Queue<PasswordPanelFrame?> Next { get; } = new();
        public bool Foreground = true;
        public int Captures;
        public PasswordPanelRuntime Runtime { get; }

        public Harness()
        {
            Runtime = new PasswordPanelRuntime((_, _) => { Captures++; return Next.Count == 0 ? null : Next.Dequeue(); },
                () => Foreground, Speech.Add, Diagnostics.Add);
            Runtime.Enable();
        }

        public void Run(params PasswordPanelFrame?[] frames)
        {
            foreach (var frame in frames)
            {
                Next.Enqueue(frame);
                Runtime.Dispatch(1, 0x2D, () => { });
            }
        }
    }
}
