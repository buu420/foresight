using System.Text.Json;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Bootstrap;

public sealed class ModManifestTests
{
    [Fact]
    public void ManifestDeclaresExactReloadedContract()
    {
        var manifestPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "ChronoTriggerAccessibility.Mod", "ModConfig.json"));

        Assert.True(File.Exists(manifestPath), $"Expected manifest at {manifestPath}.");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;

        Assert.Equal("chrono.trigger.accessibility", root.GetProperty("ModId").GetString());
        Assert.Equal("ChronoTriggerAccessibility.Mod.dll", root.GetProperty("ModDll").GetString());
        Assert.False(root.GetProperty("CanUnload").GetBoolean());
        Assert.Equal(["reloaded.sharedlib.hooks"], ReadStrings(root, "ModDependencies"));
        Assert.Equal(["chrono trigger.exe"], ReadStrings(root, "SupportedAppId"));
    }

    private static string[] ReadStrings(JsonElement root, string propertyName) =>
        root.GetProperty(propertyName).EnumerateArray().Select(element => element.GetString()!).ToArray();
}
