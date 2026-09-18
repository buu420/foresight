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
        Assert.Equal("Chrono Trigger Accessibility", root.GetProperty("ModName").GetString());
        Assert.Equal("Chrono Trigger Accessibility Project", root.GetProperty("ModAuthor").GetString());
        Assert.Equal("0.3.26", root.GetProperty("ModVersion").GetString());
        Assert.Equal("Screen-reader access for Chrono Trigger startup, menus, Settings, Extras, field dialogue, battles, and local/world navigation.", root.GetProperty("ModDescription").GetString());
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
        Assert.Equal(string.Empty, root.GetProperty("ProjectUrl").GetString());

        Assert.Equal(
            "B5D79623055547324577599841C3DED7801E949C6C4A185A40EB7BB4A7DAA80C",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))));
    }

    private static string[] ReadStrings(JsonElement root, string propertyName) =>
        root.GetProperty(propertyName).EnumerateArray().Select(element => element.GetString()!).ToArray();
}
