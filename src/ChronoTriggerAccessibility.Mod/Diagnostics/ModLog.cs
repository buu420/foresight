using Reloaded.Mod.Interfaces;

namespace ChronoTriggerAccessibility.Mod.Diagnostics;

public interface IModLog
{
    void Info(string message);
    void Error(string message);
}

public sealed class ModLog(ILogger logger, string modId) : IModLog
{
    private readonly ILogger logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly string prefix = $"[{modId}]";

    public void Info(string message) => logger.WriteLine($"{prefix} {message}");

    public void Error(string message) => logger.WriteLine($"{prefix} ERROR: {message}");
}
