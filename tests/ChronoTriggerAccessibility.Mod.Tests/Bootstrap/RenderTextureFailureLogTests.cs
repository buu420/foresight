using System.Text.Json;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Bootstrap;

public sealed class RenderTextureFailureLogTests
{
    private static readonly RenderTextureFailure Failure = new(1216, 800,
        new(1.5f, 0x3FC00000, false, null), new(2048, 2048, 16777216, 16777216, null),
        new(100, 200, 300, 400, 500, 600, null));

    [Fact]
    public void AFailureIsFlushedAsOneJsonLineWithItsSessionAndExactRequest()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ct-texture-log-" + Guid.NewGuid());
        try
        {
            var messages = new List<string>();
            var log = new RenderTextureFailureLog(folder, "test-build", messages.Add);
            log.Ready();
            log.Record(Failure);
            // Read immediately, while the writer object is alive: no Dispose/exit is needed.
            var lines = File.ReadAllLines(Path.Combine(folder, "Accessibility", "Logs", RenderTextureFailureLog.FileName));
            Assert.Equal(2, lines.Length);
            using var ready = JsonDocument.Parse(lines[0]);
            using var failure = JsonDocument.Parse(lines[1]);
            Assert.Equal("render-texture-ready", ready.RootElement.GetProperty("kind").GetString());
            var root = failure.RootElement;
            Assert.Equal("render-texture-failure", root.GetProperty("kind").GetString());
            Assert.Equal("test-build", root.GetProperty("version").GetString());
            Assert.Equal(Environment.ProcessId, root.GetProperty("processId").GetInt32());
            Assert.Equal(ready.RootElement.GetProperty("sessionId").GetString(), root.GetProperty("sessionId").GetString());
            Assert.Equal(1216, root.GetProperty("failure").GetProperty("Width").GetInt32());
            Assert.Equal(16777216ul, root.GetProperty("failure").GetProperty("Estimate").GetProperty("RgbaBytes").GetUInt64());
            Assert.Equal(2, messages.Count);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void ANewSessionAppendsInsteadOfReplacingThePreviousFailure()
    {
        var lines = new List<string>();
        var first = new RenderTextureFailureLog("unused", "test", _ => { }, lines.Add);
        first.Record(Failure);
        var second = new RenderTextureFailureLog("unused", "test", _ => { }, lines.Add);
        second.Ready();
        Assert.Equal(2, lines.Count);
        using var failure = JsonDocument.Parse(lines[0]);
        using var ready = JsonDocument.Parse(lines[1]);
        Assert.NotEqual(failure.RootElement.GetProperty("sessionId").GetString(), ready.RootElement.GetProperty("sessionId").GetString());
    }

    [Fact]
    public void DiskFailureStillRecordsTheEvidenceInTheGameplayLog()
    {
        var messages = new List<string>();
        var log = new RenderTextureFailureLog("unused", "test", messages.Add, _ => throw new IOException("full"));
        log.Record(Failure);
        Assert.Contains(messages, text => text.Contains("render-texture-failure") && text.Contains("1216"));
        Assert.Contains(messages, text => text.Contains("file unavailable"));
    }

    [Fact]
    public void GameplayLoggerFailureDoesNotPreventTheDedicatedFileOrEscape()
    {
        var lines = new List<string>();
        var log = new RenderTextureFailureLog("unused", "test", _ => throw new IOException("log"), lines.Add);
        log.Record(Failure);
        Assert.Single(lines);
    }

    [Fact]
    public void InvalidNativeFloatBitsArePreservedWithoutBreakingJson()
    {
        var lines = new List<string>();
        var log = new RenderTextureFailureLog("unused", "test", _ => { }, lines.Add);
        log.Record(Failure with { State = new(float.NaN, 0x7FC00000, null, "invalid native scale") });
        using var json = JsonDocument.Parse(Assert.Single(lines));
        var state = json.RootElement.GetProperty("failure").GetProperty("State");
        Assert.Equal(0x7FC00000u, state.GetProperty("ContentScaleBits").GetUInt32());
        Assert.Equal("NaN", state.GetProperty("ContentScaleFactor").GetString());
    }
}
