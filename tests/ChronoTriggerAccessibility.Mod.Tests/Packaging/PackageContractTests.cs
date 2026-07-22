using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Packaging;

public sealed class PackageContractTests
{
    private const string GameExecutable = @"G:\SteamLibrary\steamapps\common\Chrono Trigger\Chrono Trigger.exe";
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

        try
        {
            Directory.CreateDirectory(reloadedRoot);
            File.Copy(Path.Combine(InstalledReloadedRoot, "Reloaded-II.exe"), Path.Combine(reloadedRoot, "Reloaded-II.exe"));
            SeedSharedHookDependency(reloadedRoot);
            var staleModDirectory = Path.Combine(reloadedRoot, "Mods", "chrono.trigger.accessibility");
            Directory.CreateDirectory(staleModDirectory);
            File.WriteAllText(Path.Combine(staleModDirectory, "stale.dll"), "must be replaced");
            SeedExistingProfile(reloadedRoot);

            var gameHashBefore = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(GameExecutable)));
            var packageResult = RunPowerShell(repositoryRoot, packageScript, "-OutputDirectory", packageRoot, "-Configuration", "Release");
            Assert.True(packageResult.ExitCode == 0, $"Packaging failed.{Environment.NewLine}{packageResult.Output}");

            var deployResult = RunPowerShell(
                repositoryRoot,
                deployScript,
                "-PackageDirectory", packageRoot,
                "-ReloadedRoot", reloadedRoot,
                "-GameExecutable", GameExecutable,
                "-RuntimeRoot", RuntimeRoot,
                "-LauncherDestinationDirectory", launcherDestination);
            Assert.True(deployResult.ExitCode == 0, $"Deployment failed.{Environment.NewLine}{deployResult.Output}");

            var launcherPath = Path.Combine(launcherDestination, "Launch Chrono Trigger Accessible.ps1");
            var verifyResult = RunPowerShell(
                repositoryRoot,
                verifyScript,
                "-ReloadedRoot", reloadedRoot,
                "-GameExecutable", GameExecutable,
                "-RuntimeRoot", RuntimeRoot,
                "-LauncherPath", launcherPath);
            Assert.True(verifyResult.ExitCode == 0, $"Deployment verification failed.{Environment.NewLine}{verifyResult.Output}");

            var profilePath = Path.Combine(reloadedRoot, "Apps", "chrono trigger.exe", "AppConfig.json");
            using var profile = JsonDocument.Parse(File.ReadAllText(profilePath));
            var root = profile.RootElement;
            Assert.Equal("keep this value", root.GetProperty("CustomProperty").GetString());
            Assert.Equal("keep this nested value", root.GetProperty("PluginData").GetProperty("ExistingPlugin").GetString());
            Assert.Equal("Existing profile name", root.GetProperty("AppName").GetString());
            Assert.Equal("--keep-this", root.GetProperty("AppArguments").GetString());
            Assert.Equal(GameExecutable, root.GetProperty("AppLocation").GetString());
            Assert.Equal(Path.GetDirectoryName(GameExecutable), root.GetProperty("WorkingDirectory").GetString());
            Assert.Equal(new[] { "other.accessibility.mod", "chrono.trigger.accessibility" }, root.GetProperty("EnabledMods").EnumerateArray().Select(item => item.GetString()));
            Assert.Equal(new[] { "other.accessibility.mod", "chrono.trigger.accessibility" }, root.GetProperty("SortedMods").EnumerateArray().Select(item => item.GetString()));

            Assert.True(File.Exists(Path.Combine(reloadedRoot, "Mods", "chrono.trigger.accessibility", "SHA256SUMS.txt")));
            Assert.False(File.Exists(Path.Combine(reloadedRoot, "Mods", "chrono.trigger.accessibility", "stale.dll")));
            Assert.True(File.Exists(launcherPath));
            var launcherValidation = RunPowerShell(repositoryRoot, launcherPath, "-VerifyOnly");
            Assert.True(launcherValidation.ExitCode == 0, $"Launcher prerequisite validation failed.{Environment.NewLine}{launcherValidation.Output}");
            Assert.Empty(Directory.GetFiles(launcherDestination, "*.exe", SearchOption.AllDirectories));
            Assert.Equal(gameHashBefore, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(GameExecutable))));

            var newProfileReloadedRoot = Path.Combine(temporaryRoot, "Reloaded-II-new-profile");
            var newProfileLauncherDestination = Path.Combine(temporaryRoot, "new-profile-launcher");
            Directory.CreateDirectory(newProfileReloadedRoot);
            File.Copy(Path.Combine(InstalledReloadedRoot, "Reloaded-II.exe"), Path.Combine(newProfileReloadedRoot, "Reloaded-II.exe"));
            SeedSharedHookDependency(newProfileReloadedRoot);
            var newProfileDeploy = RunPowerShell(
                repositoryRoot,
                deployScript,
                "-PackageDirectory", packageRoot,
                "-ReloadedRoot", newProfileReloadedRoot,
                "-GameExecutable", GameExecutable,
                "-RuntimeRoot", RuntimeRoot,
                "-LauncherDestinationDirectory", newProfileLauncherDestination);
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
        Assert.Contains("Reloaded-II.exe' --launch 'G:\\SteamLibrary\\steamapps\\common\\Chrono Trigger\\Chrono Trigger.exe'", readme, StringComparison.Ordinal);
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
