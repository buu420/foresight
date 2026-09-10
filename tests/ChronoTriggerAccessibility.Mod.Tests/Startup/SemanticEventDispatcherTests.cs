using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.NewGame;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Runtime;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Startup;

public sealed class SemanticEventDispatcherTests
{
    [Fact]
    public void SemanticEventReachesPrismWithInterruptFlagAndExactDiagnostics()
    {
        var log = new RecordingLog();
        var fatal = new RecordingFatal();
        var session = new RecordingPrismSession();
        var dispatcher = new SemanticEventDispatcher(log, fatal);
        dispatcher.Attach(session);

        dispatcher.Publish(new ScreenEntered(ScreenKind.TitlePrompt));

        var output = Assert.Single(session.Outputs);
        Assert.Equal("Chrono Trigger. Press confirm.", output.Text);
        Assert.True(output.Interrupt);
        Assert.Contains(log.Infos, line => line.Contains(nameof(ScreenEntered), StringComparison.Ordinal));
        Assert.Contains(log.Infos, line => line.Contains(output.Text, StringComparison.Ordinal));
    }

    [Fact]
    public void DistinctCaptureFailuresAreSpokenOnceEachAndShareOneFatalWindow()
    {
        var log = new RecordingLog();
        var fatal = new RecordingFatal();
        var session = new RecordingPrismSession();
        var dispatcher = new SemanticEventDispatcher(log, fatal);
        dispatcher.Attach(session);

        dispatcher.ReportCoverageFailure("Title focus source is unavailable.");
        dispatcher.ReportCoverageFailure("Title focus source is unavailable.");
        dispatcher.ReportCoverageFailure("Settings focus source is unavailable.");
        dispatcher.ReportCoverageFailure("Settings focus source is unavailable.");

        Assert.Equal(2, session.Outputs.Count);
        Assert.All(session.Outputs, output => Assert.True(output.Interrupt));
        Assert.Contains("Title focus source is unavailable", session.Outputs[0].Text, StringComparison.Ordinal);
        Assert.Contains("Settings focus source is unavailable", session.Outputs[1].Text, StringComparison.Ordinal);
        Assert.Equal(2, log.Errors.Count);
        Assert.Single(fatal.Messages);
    }

    [Fact]
    public void NameBatchesKeepRequiredCompanionSpeechQueuedInsteadOfClippingItWithAnotherInterrupt()
    {
        var log = new RecordingLog();
        var fatal = new RecordingFatal();
        var session = new RecordingPrismSession();
        var dispatcher = new SemanticEventDispatcher(log, fatal);
        dispatcher.Attach(session);

        dispatcher.Publish(new NameAccessibilityBatch(
        [
            new NameEntryPresented("Enter a name", "Crono", "Use shown characters"),
            new NameGridFocused("A", "Latin", 0, 0),
        ]));
        dispatcher.Publish(new NameAccessibilityBatch(
        [
            new NewGameNameChanged("Crona"),
            new NameGridFocused("Done", "Latin", 7, 10),
        ]));
        dispatcher.Publish(new NameAccessibilityBatch(
        [
            new NameGridVisibilityChanged(false),
            new NameActionFocused("Accept", 1, 4),
        ]));

        Assert.Equal(6, session.Outputs.Count);
        Assert.Equal(
            new[] { true, false, true, false, true, false },
            session.Outputs.Select(output => output.Interrupt));
        Assert.Contains("Maximum five characters", session.Outputs[0].Text);
        Assert.Equal("A. Latin page, row 1, column 1.", session.Outputs[1].Text);
        Assert.Equal("Name: Crona.", session.Outputs[2].Text);
        Assert.Equal("Done. Latin page, row 8, column 11.", session.Outputs[3].Text);
        Assert.Equal("Character grid closed.", session.Outputs[4].Text);
        Assert.Equal("Accept, 2 of 4", session.Outputs[5].Text);
        Assert.Empty(fatal.Messages);
    }

    private sealed class RecordingPrismSession : IRuntimePrismSession
    {
        public string BackendName => "Test backend";
        public List<(string Text, bool Interrupt)> Outputs { get; } = [];
        public void Output(string text, bool interrupt) => Outputs.Add((text, interrupt));
        public void Dispose() { }
    }

    private sealed class RecordingLog : IModLog
    {
        public List<string> Infos { get; } = [];
        public List<string> Errors { get; } = [];
        public void Info(string message) => Infos.Add(message);
        public void Error(string message) => Errors.Add(message);
    }

    private sealed class RecordingFatal : IAccessibleFatalError
    {
        public List<string> Messages { get; } = [];
        public void Show(string message) => Messages.Add(message);
    }
}
