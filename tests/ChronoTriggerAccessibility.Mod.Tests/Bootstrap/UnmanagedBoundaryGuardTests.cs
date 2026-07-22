using ChronoTriggerAccessibility.Mod.Diagnostics;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Bootstrap;

public sealed class UnmanagedBoundaryGuardTests
{
    [Fact]
    public void ManagedExceptionNeverCrossesVoidBoundaryAndFaultIsReportedOnce()
    {
        var log = new AccessibilityRuntimeTests.RecordingLog();
        var fatal = new AccessibilityRuntimeTests.RecordingFatalError();
        var guard = new UnmanagedBoundaryGuard(log, fatal);

        var first = Record.Exception(() => guard.Run("title update", () => throw new InvalidOperationException("broken")));
        var second = Record.Exception(() => guard.Run("title update", () => throw new InvalidOperationException("again")));

        Assert.Null(first);
        Assert.Null(second);
        Assert.True(guard.IsFaulted);
        Assert.Equal(2, log.Errors.Count);
        Assert.Single(fatal.Messages);
    }

    [Fact]
    public void ManagedExceptionNeverCrossesReturningBoundaryAndReturnsFallback()
    {
        var guard = new UnmanagedBoundaryGuard(
            new AccessibilityRuntimeTests.RecordingLog(),
            new AccessibilityRuntimeTests.RecordingFatalError());

        var result = guard.Run("scene create", () => throw new InvalidOperationException("broken"), fallback: 27);

        Assert.Equal(27, result);
    }

    [Fact]
    public void DiagnosticSinkFailureAlsoCannotEscapeBoundary()
    {
        var guard = new UnmanagedBoundaryGuard(new ThrowingLog(), new ThrowingFatalError());

        var exception = Record.Exception(() => guard.Run("callback", () => throw new InvalidOperationException("broken")));

        Assert.Null(exception);
        Assert.True(guard.IsFaulted);
    }

    private sealed class ThrowingLog : IModLog
    {
        public void Info(string message) => throw new InvalidOperationException("log");
        public void Error(string message) => throw new InvalidOperationException("log");
    }

    private sealed class ThrowingFatalError : IAccessibleFatalError
    {
        public void Show(string message) => throw new InvalidOperationException("fatal");
    }
}
