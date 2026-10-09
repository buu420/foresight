using System.Security.Cryptography;
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

        Assert.Equal(
            [
                "ModId",
                "ModName",
                "ModAuthor",
                "ModVersion",
                "ModDescription",
                "ModDll",
                "ModIcon",
                "ModR2RManagedDll32",
                "ModR2RManagedDll64",
                "ModNativeDll32",
                "ModNativeDll64",
                "Tags",
                "CanUnload",
                "HasExports",
                "IsLibrary",
                "ReleaseMetadataFileName",
                "IgnoreRegexes",
                "IncludeRegexes",
                "PluginData",
                "IsUniversalMod",
                "ModDependencies",
                "OptionalDependencies",
                "SupportedAppId",
                "ProjectUrl"
            ],
            root.EnumerateObject().Select(property => property.Name));

        Assert.Equal("chrono.trigger.accessibility", root.GetProperty("ModId").GetString());
        Assert.Equal("Foresight (Beta)", root.GetProperty("ModName").GetString());
        Assert.Equal("Buu420", root.GetProperty("ModAuthor").GetString());
        Assert.Equal("0.3.48", root.GetProperty("ModVersion").GetString());
        Assert.Equal("Foresight beta, registry-free installation: screen-reader menus, dialogue, battle information, navigation and bike-race feedback for Chrono Trigger. Navigation: U/O categories, J/L destinations, K repeat, I guidance, P auto-walk, F8 footsteps. Controller: R3 menu, L1/LB and R1/RB categories, D-pad Up/Down destinations, Square/X auto-walk, Cross/A guidance, Circle/B close. Battle: 1/2/3 inspect party member, Shift+H HP, M MP, K repeat. Bike race: Up/Down steer, game Dash action boosts, K or R3 reads status; high/low/middle tones mean Johnny above/below/aligned. See README for beta limitations.", root.GetProperty("ModDescription").GetString());
        Assert.Equal("ChronoTriggerAccessibility.Mod.dll", root.GetProperty("ModDll").GetString());
        Assert.Equal(string.Empty, root.GetProperty("ModIcon").GetString());
        Assert.Equal(string.Empty, root.GetProperty("ModR2RManagedDll32").GetString());
        Assert.Equal(string.Empty, root.GetProperty("ModR2RManagedDll64").GetString());
        Assert.Equal(string.Empty, root.GetProperty("ModNativeDll32").GetString());
        Assert.Equal(string.Empty, root.GetProperty("ModNativeDll64").GetString());
        Assert.Empty(ReadStrings(root, "Tags"));
        Assert.False(root.GetProperty("CanUnload").GetBoolean());
        Assert.False(root.GetProperty("HasExports").GetBoolean());
        Assert.False(root.GetProperty("IsLibrary").GetBoolean());
        Assert.Equal("Sewer56.Update.ReleaseMetadata.json", root.GetProperty("ReleaseMetadataFileName").GetString());
        Assert.Equal([".*\\.json"], ReadStrings(root, "IgnoreRegexes"));
        Assert.Equal(["\\.deps\\.json", "\\.runtimeconfig\\.json", "ModConfig\\.json"], ReadStrings(root, "IncludeRegexes"));
        Assert.Equal(JsonValueKind.Object, root.GetProperty("PluginData").ValueKind);
        Assert.Empty(root.GetProperty("PluginData").EnumerateObject());
        Assert.False(root.GetProperty("IsUniversalMod").GetBoolean());
        Assert.Equal(["reloaded.sharedlib.hooks"], ReadStrings(root, "ModDependencies"));
        Assert.Empty(ReadStrings(root, "OptionalDependencies"));
        Assert.Equal(["chrono trigger.exe"], ReadStrings(root, "SupportedAppId"));
        Assert.Equal("https://github.com/buu420/foresight", root.GetProperty("ProjectUrl").GetString());

        Assert.Equal(
            "850FE6F819647C199DF85E1BBEAF876A28BE808A05719A5AFA09D7A5AA75AF5B",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))));
    }

    private static string[] ReadStrings(JsonElement root, string propertyName) =>
        root.GetProperty(propertyName).EnumerateArray().Select(element => element.GetString()!).ToArray();
}
