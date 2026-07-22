using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class BuilderCaptureScopeTests
{
    [Fact]
    public void CorrelatesTypedObservationsIntoAnImmutableSnapshot()
    {
        using var scope = new BuilderCaptureScope();
        Assert.True(scope.TryRecordLocalizedText(0x1000, BuilderTextPart.Label, "Archive", out var localizedError), localizedError);
        Assert.True(scope.TryRecordLocalizedText(0x1000, BuilderTextPart.Help, "Browse records", out var helpError), helpError);
        Assert.True(scope.TryRecordConstructedControl(0x1000, 0, enabled: true, visible: true, out var controlError), controlError);
        Assert.True(scope.TryRecordManagerKeyBinding(0x2000, 0x1000, 7, out var bindingError), bindingError);
        Assert.True(scope.TryRecordFocus(0x2000, 7, out var focusError), focusError);
        Assert.True(scope.TryRecordRenderedString(0x1000, BuilderRenderSource.Value, "3 entries", out var renderedError), renderedError);

        Assert.True(scope.TryCreateSnapshot("Archive status", out var result), result.Diagnostic);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Snapshot);
        var control = Assert.Single(result.Snapshot!.Controls);
        Assert.Equal("Archive", control.Label);
        Assert.Equal("3 entries", control.Value);
        Assert.Equal("Browse records", control.Help);
        Assert.Equal(7, control.Key);
        Assert.Equal(0, control.Position);
        Assert.Equal(1, control.Count);
        Assert.True(control.Enabled);
        Assert.True(control.Visible);
        Assert.Equal(7, result.Snapshot.FocusedKey);
    }

    [Fact]
    public void RejectsNestedScopesOnTheOwningThread()
    {
        using var scope = new BuilderCaptureScope();
        Assert.Throws<InvalidOperationException>(() => new BuilderCaptureScope());
    }

    [Fact]
    public async Task RejectsCrossThreadAccessWithoutTimingSensitiveWaits()
    {
        using var ownerReady = new ManualResetEventSlim();
        using var crossThreadDone = new ManualResetEventSlim();
        BuilderCaptureScope? scope = null;
        var owner = new Thread(() =>
        {
            using var ownedScope = new BuilderCaptureScope();
            scope = ownedScope;
            ownerReady.Set();
            crossThreadDone.Wait();
        });
        owner.Start();
        ownerReady.Wait();

        var results = await Task.Run(() =>
        {
            var recorded = scope!.TryRecordConstructedControl(0x1000, 0, true, true, out var recordedError) ? string.Empty : recordedError;
            var snapshot = scope.TryCreateSnapshot(null, out var result) ? string.Empty : result.Diagnostic;
            return (recorded, snapshot, RecordDisposeError(scope));
        });
        crossThreadDone.Set();
        owner.Join();

        Assert.Contains("thread", results.recorded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("thread", results.snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("thread", results.Item3, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisposalIsFinallySafeAndStopsNewObservations()
    {
        var scope = new BuilderCaptureScope();
        scope.Dispose();
        scope.Dispose();

        Assert.False(scope.TryRecordConstructedControl(0x1000, 0, true, true, out var error));
        Assert.Contains("disposed", error, StringComparison.OrdinalIgnoreCase);
        Assert.False(scope.TryCreateSnapshot(null, out var result));
        Assert.Null(result.Snapshot);
        Assert.Contains("disposed", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsInvalidObservationsAndLeavesNoPartialSnapshot()
    {
        using var scope = new BuilderCaptureScope();
        Assert.False(scope.TryRecordLocalizedText(0x1000, BuilderTextPart.Label, " ", out var blankError));
        Assert.Contains("blank", blankError, StringComparison.OrdinalIgnoreCase);
        Assert.False(scope.TryRecordRenderedString(0x1000, (BuilderRenderSource)99, "value", out var sourceError));
        Assert.Contains("source", sourceError, StringComparison.OrdinalIgnoreCase);
        Assert.True(scope.TryRecordConstructedControl(0x1000, 0, true, true, out _));
        Assert.True(scope.TryRecordManagerKeyBinding(0x2000, 0x1000, 0, out _));
        Assert.False(scope.TryRecordManagerKeyBinding(0x2000, 0x1001, 0, out var duplicateKeyError));
        Assert.Contains("duplicate", duplicateKeyError, StringComparison.OrdinalIgnoreCase);
        Assert.False(scope.TryRecordManagerKeyBinding(0x2000, 0x1000, 1, out var duplicateControlError));
        Assert.Contains("bound", duplicateControlError, StringComparison.OrdinalIgnoreCase);

        Assert.False(scope.TryCreateSnapshot(null, out var result));
        Assert.Null(result.Snapshot);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic));
    }

    [Fact]
    public void RejectsAmbiguousCorrelationWithNoPartialSnapshot()
    {
        using var scope = new BuilderCaptureScope();
        Assert.True(scope.TryRecordLocalizedText(0x1000, BuilderTextPart.Label, "First", out _));
        Assert.True(scope.TryRecordLocalizedText(0x1000, BuilderTextPart.Label, "Second", out var duplicateError) == false);
        Assert.Contains("ambiguous", duplicateError, StringComparison.OrdinalIgnoreCase);
        Assert.True(scope.TryRecordConstructedControl(0x1000, 0, true, true, out _));
        Assert.True(scope.TryRecordManagerKeyBinding(0x2000, 0x1000, 0, out _));

        Assert.False(scope.TryCreateSnapshot(null, out var result));
        Assert.Null(result.Snapshot);
        Assert.Contains("ambiguous", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnapshotAndModelConstructorsDefensivelyCopyLists()
    {
        var controls = new List<MenuControlSnapshot>
        {
            new("First", null, null, 0, 0, 1, true, true),
        };
        var snapshot = new MenuStatusSnapshot("ready", controls, 0);
        controls.Clear();

        Assert.Single(snapshot.Controls);
        Assert.Throws<NotSupportedException>(() => ((IList<MenuControlSnapshot>)snapshot.Controls).Add(
            new("Second", null, null, 1, 1, 2, true, true)));
    }

    private static string RecordDisposeError(BuilderCaptureScope scope)
    {
        try
        {
            scope.Dispose();
            return string.Empty;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }
}
