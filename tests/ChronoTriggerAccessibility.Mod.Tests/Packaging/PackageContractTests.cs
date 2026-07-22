using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Packaging;

public sealed class PackageContractTests
{
    private const string InstalledGameExecutable = @"G:\SteamLibrary\steamapps\common\Chrono Trigger\Chrono Trigger.exe";
    private const string InstalledReloadedRoot = @"C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release";
    private const string RuntimeRoot = @"C:\Users\User\AppData\Local\ChronoTriggerAccessibility\dotnet-x86";

    [Fact]
    public void Package_contains_only_the_complete_verified_mod_payload()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageScript = Path.Combine(repositoryRoot, "tools", "Package-Mod.ps1");
        Assert.True(File.Exists(packageScript), $"Package script is missing: {packageScript}");

        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"chrono-trigger-accessibility-package-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(temporaryRoot, "chrono.trigger.accessibility");

        try
        {
            var result = RunPowerShell(
                repositoryRoot,
                packageScript,
                "-OutputDirectory", packageRoot,
                "-Configuration", "Release");

            Assert.True(result.ExitCode == 0, $"Packaging failed.{Environment.NewLine}{result.Output}");

            var requiredFiles = new[]
            {
                "ChronoTriggerAccessibility.Mod.deps.json",
                "ChronoTriggerAccessibility.Mod.dll",
                "ChronoTriggerAccessibility.Native.dll",
                "ChronoTriggerAccessibility.Prism.dll",
                "ModConfig.json",
                "prism.dll",
                "LICENSE",
                "NOTICE",
                Path.Combine("LICENSES", "GNU-GPL-3.0.txt"),
                Path.Combine("LICENSES", "Reloaded.Hooks.Definitions-LGPL-3.0.txt"),
                Path.Combine("LICENSES", "Reloaded.SharedLib.Hooks-LGPL-3.0.txt"),
                Path.Combine("LICENSES", "Ultimate-ASI-Loader-MIT.txt"),
                Path.Combine("LICENSES", "concurrentqueue", "LICENSE.md"),
                Path.Combine("LICENSES", "djinni", "LICENSE"),
                Path.Combine("LICENSES", "dr_wav", "LICENSE"),
                Path.Combine("LICENSES", "fmt", "LICENSE"),
                Path.Combine("LICENSES", "moderncom", "AUTHORS.md"),
                Path.Combine("LICENSES", "moderncom", "LICENSE"),
                Path.Combine("LICENSES", "nvdaController", "lgpl-2.1.txt"),
                Path.Combine("LICENSES", "nvgt", "LICENSE.md"),
                Path.Combine("LICENSES", "prism", "mpl-2.0.txt"),
                Path.Combine("LICENSES", "simdutf", "apache-2.0.txt"),
                "README.md",
                "THIRD-PARTY-NOTICES.md",
                "SHA256SUMS.txt"
            };

            foreach (var relativePath in requiredFiles)
                Assert.True(File.Exists(Path.Combine(packageRoot, relativePath)), $"Required package file is missing: {relativePath}");

            var packagedFiles = Directory.GetFiles(packageRoot, "*", SearchOption.AllDirectories);
            Assert.DoesNotContain(packagedFiles, path => string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(packagedFiles, path => path.Contains("x64", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0x014c, ReadPeMachine(Path.Combine(packageRoot, "prism.dll")));

            AssertCanonicalReloadedManifestIsPackagedByteForByte(repositoryRoot, packageRoot);
            AssertPrismLicenseTreeMatchesPinnedSource(repositoryRoot, packageRoot);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(repositoryRoot, "native", "ultimate-asi-loader", "v6.9.0", "LICENSE")),
                File.ReadAllBytes(Path.Combine(packageRoot, "LICENSES", "Ultimate-ASI-Loader-MIT.txt")));
            AssertDepsRuntimeFilesArePresent(packageRoot);
            AssertExactSha256Manifest(packageRoot, packagedFiles);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void Deployment_preserves_unrelated_profile_state_and_verifies_the_complete_install()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageScript = Path.Combine(repositoryRoot, "tools", "Package-Mod.ps1");
        var deployScript = Path.Combine(repositoryRoot, "tools", "Deploy-Mod.ps1");
        var verifyScript = Path.Combine(repositoryRoot, "tools", "Verify-Deployment.ps1");
        var launcherSource = Path.Combine(repositoryRoot, "Launch Chrono Trigger Accessible.ps1");
        Assert.True(File.Exists(deployScript), $"Deploy script is missing: {deployScript}");
        Assert.True(File.Exists(verifyScript), $"Verification script is missing: {verifyScript}");
        Assert.True(File.Exists(launcherSource), $"Launcher is missing: {launcherSource}");

        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"chrono-trigger-accessibility-deploy-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(temporaryRoot, "package", "chrono.trigger.accessibility");
        var reloadedRoot = Path.Combine(temporaryRoot, "Reloaded-II");
        var launcherDestination = Path.Combine(temporaryRoot, "game-launcher");
        var gameDirectory = Path.Combine(temporaryRoot, "game");
        var gameExecutable = Path.Combine(gameDirectory, "Chrono Trigger.exe");
        var reloadedConfigPath = Path.Combine(temporaryRoot, "ReloadedConfig", "ReloadedII.json");

        try
        {
            Directory.CreateDirectory(reloadedRoot);
            File.Copy(Path.Combine(InstalledReloadedRoot, "Reloaded-II.exe"), Path.Combine(reloadedRoot, "Reloaded-II.exe"));
            SeedSharedHookDependency(reloadedRoot);
            SeedAutoLaunchDependency(reloadedRoot);
            SeedReloadedBootstrapConfiguration(reloadedRoot, reloadedConfigPath);
            Directory.CreateDirectory(gameDirectory);
            File.Copy(InstalledGameExecutable, gameExecutable);
            var staleModDirectory = Path.Combine(reloadedRoot, "Mods", "chrono.trigger.accessibility");
            Directory.CreateDirectory(staleModDirectory);
            File.WriteAllText(Path.Combine(staleModDirectory, "stale.dll"), "must be replaced");
            SeedExistingProfile(reloadedRoot);

            var gameHashBefore = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gameExecutable)));
            var packageResult = RunPowerShell(repositoryRoot, packageScript, "-OutputDirectory", packageRoot, "-Configuration", "Release");
            Assert.True(packageResult.ExitCode == 0, $"Packaging failed.{Environment.NewLine}{packageResult.Output}");

            var deployResult = RunPowerShell(
                repositoryRoot,
                deployScript,
                "-PackageDirectory", packageRoot,
                "-ReloadedRoot", reloadedRoot,
                "-GameExecutable", gameExecutable,
                "-RuntimeRoot", RuntimeRoot,
                "-LauncherDestinationDirectory", launcherDestination,
                "-ReloadedConfigPath", reloadedConfigPath);
            Assert.True(deployResult.ExitCode == 0, $"Deployment failed.{Environment.NewLine}{deployResult.Output}");

            var launcherPath = Path.Combine(launcherDestination, "Launch Chrono Trigger Accessible.ps1");
            var verifyResult = RunPowerShell(
                repositoryRoot,
                verifyScript,
                "-ReloadedRoot", reloadedRoot,
                "-GameExecutable", gameExecutable,
                "-RuntimeRoot", RuntimeRoot,
                "-LauncherPath", launcherPath,
                "-ReloadedConfigPath", reloadedConfigPath);
            Assert.True(verifyResult.ExitCode == 0, $"Deployment verification failed.{Environment.NewLine}{verifyResult.Output}");

            var profilePath = Path.Combine(reloadedRoot, "Apps", "chrono trigger.exe", "AppConfig.json");
            using var profile = JsonDocument.Parse(File.ReadAllText(profilePath));
            var root = profile.RootElement;
            Assert.Equal("keep this value", root.GetProperty("CustomProperty").GetString());
            Assert.Equal("keep this nested value", root.GetProperty("PluginData").GetProperty("ExistingPlugin").GetString());
            Assert.Equal("Existing profile name", root.GetProperty("AppName").GetString());
            Assert.Equal("--keep-this", root.GetProperty("AppArguments").GetString());
            Assert.False(root.GetProperty("AutoInject").GetBoolean());
            Assert.Equal(gameExecutable, root.GetProperty("AppLocation").GetString());
            Assert.Equal(Path.GetDirectoryName(gameExecutable), root.GetProperty("WorkingDirectory").GetString());
            Assert.Equal(new[] { "other.accessibility.mod", "chrono.trigger.accessibility" }, root.GetProperty("EnabledMods").EnumerateArray().Select(item => item.GetString()));
            Assert.Equal(new[] { "other.accessibility.mod", "chrono.trigger.accessibility" }, root.GetProperty("SortedMods").EnumerateArray().Select(item => item.GetString()));

            Assert.True(File.Exists(Path.Combine(reloadedRoot, "Mods", "chrono.trigger.accessibility", "SHA256SUMS.txt")));
            Assert.False(File.Exists(Path.Combine(reloadedRoot, "Mods", "chrono.trigger.accessibility", "stale.dll")));
            Assert.True(File.Exists(launcherPath));
            Assert.Equal("A51C630B2EA3D78AD55A330EA64D510C8C0737F620BE65AD7503B61840D59E37", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(gameDirectory, "winmm.dll")))));
            Assert.Equal("1A9F704549F66E357C0D22C395B57FE4E7BD5248521DBB40E566D2EE1CA809AB", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(gameDirectory, "Reloaded.Mod.Loader.Bootstrapper.asi")))));
            var launcherValidation = RunPowerShell(repositoryRoot, launcherPath, "-VerifyOnly");
            Assert.True(launcherValidation.ExitCode == 0, $"Launcher prerequisite validation failed.{Environment.NewLine}{launcherValidation.Output}");
            Assert.Empty(Directory.GetFiles(launcherDestination, "*.exe", SearchOption.AllDirectories));
            Assert.Equal(gameHashBefore, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gameExecutable))));

            var newProfileReloadedRoot = Path.Combine(temporaryRoot, "Reloaded-II-new-profile");
            var newProfileLauncherDestination = Path.Combine(temporaryRoot, "new-profile-launcher");
            var newProfileReloadedConfigPath = Path.Combine(temporaryRoot, "ReloadedConfig-new-profile", "ReloadedII.json");
            Directory.CreateDirectory(newProfileReloadedRoot);
            File.Copy(Path.Combine(InstalledReloadedRoot, "Reloaded-II.exe"), Path.Combine(newProfileReloadedRoot, "Reloaded-II.exe"));
            SeedSharedHookDependency(newProfileReloadedRoot);
            SeedAutoLaunchDependency(newProfileReloadedRoot);
            SeedReloadedBootstrapConfiguration(newProfileReloadedRoot, newProfileReloadedConfigPath);
            var newProfileDeploy = RunPowerShell(
                repositoryRoot,
                deployScript,
                "-PackageDirectory", packageRoot,
                "-ReloadedRoot", newProfileReloadedRoot,
                "-GameExecutable", gameExecutable,
                "-RuntimeRoot", RuntimeRoot,
                "-LauncherDestinationDirectory", newProfileLauncherDestination,
                "-ReloadedConfigPath", newProfileReloadedConfigPath);
            Assert.True(newProfileDeploy.ExitCode == 0, $"New-profile deployment failed.{Environment.NewLine}{newProfileDeploy.Output}");
            using var newProfile = JsonDocument.Parse(File.ReadAllText(Path.Combine(newProfileReloadedRoot, "Apps", "chrono trigger.exe", "AppConfig.json")));
            Assert.Equal(JsonValueKind.Array, newProfile.RootElement.GetProperty("EnabledMods").ValueKind);
            Assert.Equal(JsonValueKind.Array, newProfile.RootElement.GetProperty("SortedMods").ValueKind);
            Assert.Equal("chrono.trigger.accessibility", Assert.Single(newProfile.RootElement.GetProperty("EnabledMods").EnumerateArray()).GetString());
            Assert.Equal("chrono.trigger.accessibility", Assert.Single(newProfile.RootElement.GetProperty("SortedMods").EnumerateArray()).GetString());
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void User_documentation_names_support_output_logs_troubleshooting_and_uninstall()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));

        Assert.Contains("8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7", readme, StringComparison.Ordinal);
        foreach (var backend in new[] { "NVDA", "UI Automation", "OneCore", "PC Talker", "ZDSR", "Boy PC Reader" })
            Assert.Contains(backend, readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Square Enix", readme, StringComparison.Ordinal);
        Assert.Contains("Reloaded-Mod-Loader-II\\Logs", readme, StringComparison.Ordinal);
        Assert.Contains("troubleshooting", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uninstall", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("normal Steam", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("winmm.dll", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reloaded.Mod.Loader.Bootstrapper.asi", readme, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertDepsRuntimeFilesArePresent(string packageRoot)
    {
        var depsPath = Path.Combine(packageRoot, "ChronoTriggerAccessibility.Mod.deps.json");
        using var deps = JsonDocument.Parse(File.ReadAllText(depsPath));
        var targets = deps.RootElement.GetProperty("targets");

        foreach (var target in targets.EnumerateObject())
        foreach (var library in target.Value.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("runtime", out var runtime))
                continue;

            foreach (var asset in runtime.EnumerateObject())
            {
                var fileName = Path.GetFileName(asset.Name);
                Assert.True(File.Exists(Path.Combine(packageRoot, fileName)), $"Runtime dependency is missing: {fileName}");
            }
        }
    }

    private static void AssertCanonicalReloadedManifestIsPackagedByteForByte(string repositoryRoot, string packageRoot)
    {
        const string canonicalHash = "03E59C99C823A6B2F86A66FADF1A2B3FAB9D99DA40E67F4A0EFA89E88766A499";
        var sourcePath = Path.Combine(repositoryRoot, "src", "ChronoTriggerAccessibility.Mod", "ModConfig.json");
        var packagedPath = Path.Combine(packageRoot, "ModConfig.json");
        var sourceBytes = File.ReadAllBytes(sourcePath);
        var packagedBytes = File.ReadAllBytes(packagedPath);

        Assert.Equal(sourceBytes, packagedBytes);
        Assert.Equal(canonicalHash, Convert.ToHexString(SHA256.HashData(packagedBytes)));

        using var document = JsonDocument.Parse(packagedBytes);
        var root = document.RootElement;
        Assert.False(root.GetProperty("CanUnload").GetBoolean());
        Assert.False(root.GetProperty("HasExports").GetBoolean());
        Assert.Equal(string.Empty, root.GetProperty("ModIcon").GetString());
        Assert.Equal(string.Empty, root.GetProperty("ModR2RManagedDll32").GetString());
        Assert.Equal(string.Empty, root.GetProperty("ModR2RManagedDll64").GetString());
        Assert.Equal(string.Empty, root.GetProperty("ModNativeDll32").GetString());
        Assert.Equal(string.Empty, root.GetProperty("ModNativeDll64").GetString());
        Assert.Empty(root.GetProperty("Tags").EnumerateArray());
        Assert.Equal("Sewer56.Update.ReleaseMetadata.json", root.GetProperty("ReleaseMetadataFileName").GetString());
        Assert.Equal([".*\\.json"], root.GetProperty("IgnoreRegexes").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(
            ["\\.deps\\.json", "\\.runtimeconfig\\.json", "ModConfig\\.json"],
            root.GetProperty("IncludeRegexes").EnumerateArray().Select(item => item.GetString()));
        Assert.Empty(root.GetProperty("PluginData").EnumerateObject());
    }

    private static void AssertPrismLicenseTreeMatchesPinnedSource(string repositoryRoot, string packageRoot)
    {
        var sourceRoot = Path.Combine(repositoryRoot, "native", "prism", "v0.17.3", "LICENSES");
        var packageLicenseRoot = Path.Combine(packageRoot, "LICENSES");
        var sourceFiles = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                Path.Combine("concurrentqueue", "LICENSE.md"),
                Path.Combine("djinni", "LICENSE"),
                Path.Combine("dr_wav", "LICENSE"),
                Path.Combine("fmt", "LICENSE"),
                Path.Combine("moderncom", "AUTHORS.md"),
                Path.Combine("moderncom", "LICENSE"),
                Path.Combine("nvdaController", "lgpl-2.1.txt"),
                Path.Combine("nvgt", "LICENSE.md"),
                Path.Combine("prism", "mpl-2.0.txt"),
                Path.Combine("simdutf", "apache-2.0.txt")
            }.Order(StringComparer.Ordinal),
            sourceFiles);

        foreach (var relativePath in sourceFiles)
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(sourceRoot, relativePath)),
                File.ReadAllBytes(Path.Combine(packageLicenseRoot, relativePath)));
        }
    }

    private static void AssertExactSha256Manifest(string packageRoot, string[] packagedFiles)
    {
        var manifestPath = Path.Combine(packageRoot, "SHA256SUMS.txt");
        var expectedLines = packagedFiles
            .Where(path => !string.Equals(path, manifestPath, StringComparison.OrdinalIgnoreCase))
            .Select(path =>
            {
                var relativePath = Path.GetRelativePath(packageRoot, path).Replace('\\', '/');
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                return $"{hash}  {relativePath}";
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        var actualLines = File.ReadAllLines(manifestPath);
        Assert.Equal(expectedLines, actualLines);
    }

    private static void SeedSharedHookDependency(string reloadedRoot)
    {
        var installedDependency = Path.Combine(InstalledReloadedRoot, "Mods", "reloaded.sharedlib.hooks");
        var fakeDependency = Path.Combine(reloadedRoot, "Mods", "reloaded.sharedlib.hooks");
        Directory.CreateDirectory(Path.Combine(fakeDependency, "x86"));
        File.Copy(Path.Combine(installedDependency, "ModConfig.json"), Path.Combine(fakeDependency, "ModConfig.json"));
        File.Copy(
            Path.Combine(installedDependency, "x86", "Reloaded.Hooks.ReloadedII.dll"),
            Path.Combine(fakeDependency, "x86", "Reloaded.Hooks.ReloadedII.dll"));
    }

    private static void SeedAutoLaunchDependency(string reloadedRoot)
    {
        var relativePath = Path.Combine("Loader", "X86", "Bootstrapper", "Reloaded.Mod.Loader.Bootstrapper.dll");
        var destination = Path.Combine(reloadedRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(Path.Combine(InstalledReloadedRoot, relativePath), destination);
    }

    private static void SeedReloadedBootstrapConfiguration(string reloadedRoot, string configPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(
            configPath,
            JsonSerializer.Serialize(new
            {
                LoaderPath32 = Path.Combine(reloadedRoot, "Loader", "X86", "Reloaded.Mod.Loader.dll"),
                LauncherPath = Path.Combine(reloadedRoot, "Reloaded-II.exe"),
                Bootstrapper32Path = Path.Combine(reloadedRoot, "Loader", "X86", "Bootstrapper", "Reloaded.Mod.Loader.Bootstrapper.dll"),
                ApplicationConfigDirectory = Path.Combine(reloadedRoot, "Apps"),
                ModConfigDirectory = Path.Combine(reloadedRoot, "Mods")
            }));
        Directory.CreateDirectory(Path.Combine(reloadedRoot, "Loader", "X86"));
        File.Copy(
            Path.Combine(InstalledReloadedRoot, "Loader", "X86", "Reloaded.Mod.Loader.dll"),
            Path.Combine(reloadedRoot, "Loader", "X86", "Reloaded.Mod.Loader.dll"));
        File.Copy(
            Path.Combine(InstalledReloadedRoot, "Loader", "X86", "Reloaded.Mod.Loader.runtimeconfig.json"),
            Path.Combine(reloadedRoot, "Loader", "X86", "Reloaded.Mod.Loader.runtimeconfig.json"));
    }

    private static void SeedExistingProfile(string reloadedRoot)
    {
        var profileDirectory = Path.Combine(reloadedRoot, "Apps", "chrono trigger.exe");
        Directory.CreateDirectory(profileDirectory);
        File.WriteAllText(
            Path.Combine(profileDirectory, "AppConfig.json"),
            """
            {
              "AppId": "chrono trigger.exe",
              "AppName": "Existing profile name",
              "AppLocation": "C:\\Old\\Chrono Trigger.exe",
              "AppArguments": "--keep-this",
              "EnabledMods": [ "chrono.trigger.accessibility", "other.accessibility.mod", "chrono.trigger.accessibility" ],
              "SortedMods": [ "chrono.trigger.accessibility", "other.accessibility.mod", "chrono.trigger.accessibility" ],
              "WorkingDirectory": "C:\\Old",
              "PluginData": { "ExistingPlugin": "keep this nested value" },
              "CustomProperty": "keep this value"
            }
            """);
    }

    private static int ReadPeMachine(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        stream.Position = 0x3c;
        var peOffset = reader.ReadInt32();
        stream.Position = peOffset;
        Assert.Equal(0x00004550u, reader.ReadUInt32());
        return reader.ReadUInt16();
    }

    private static (int ExitCode, string Output) RunPowerShell(string workingDirectory, string scriptPath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var standardOutputReader = new Thread(() => standardOutput.Append(process.StandardOutput.ReadToEnd()));
        var standardErrorReader = new Thread(() => standardError.Append(process.StandardError.ReadToEnd()));
        standardOutputReader.Start();
        standardErrorReader.Start();
        if (!process.WaitForExit(milliseconds: 120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"PowerShell did not finish within two minutes: {scriptPath}");
        }

        standardOutputReader.Join();
        standardErrorReader.Join();
        return (process.ExitCode, standardOutput.ToString() + standardError);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ChronoTriggerAccessibility.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
