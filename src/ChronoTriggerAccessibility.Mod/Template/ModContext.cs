using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;

namespace ChronoTriggerAccessibility.Mod.Template;

public sealed class ModContext
{
    public required IModLoader ModLoader { get; init; }
    public required IReloadedHooks Hooks { get; init; }
    public required ILogger Logger { get; init; }
    public required IModConfig ModConfig { get; init; }
    public required IMod Owner { get; init; }
}
