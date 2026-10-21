using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChronoTriggerAccessibility.Mod.Diagnostics;

public sealed class RenderTextureFailureLog(string gameRoot, string version, Action<string> diagnostic,
    Action<string>? persist = null)
{
    public const string FileName = "render-texture.log";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
    private readonly string path = Path.Combine(gameRoot, "Accessibility", "Logs", FileName);
    private readonly string sessionId = Guid.NewGuid().ToString("N");
    private readonly object gate = new();

    public void Ready() => Write("render-texture-ready", null);
    public void Record(RenderTextureFailure failure) => Write("render-texture-failure", failure);

    private void Write(string kind, RenderTextureFailure? failure)
    {
        try
        {
            var line = JsonSerializer.Serialize(new
            {
                kind, version, sessionId, processId = Environment.ProcessId,
                utc = DateTimeOffset.UtcNow, scalarsCapturedAfterFailure = true, failure,
            }, JsonOptions);
            try { (persist ?? AppendAndFlush)(line); }
            catch (Exception) { Report("Texture diagnostics file unavailable; retaining evidence in gameplay log."); }
            Report("Texture diagnostics: " + line);
        }
        catch (Exception) { /* Serialization/diagnostic errors cannot reach game code. */ }
    }

    private void AppendAndFlush(string line)
    {
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var file = new FileStream(path, FileMode.Append, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            file.Write(Encoding.UTF8.GetBytes(line + Environment.NewLine));
            file.Flush(flushToDisk: true);
        }
    }

    private void Report(string message)
    {
        try { diagnostic(message); } catch (Exception) { }
    }
}
