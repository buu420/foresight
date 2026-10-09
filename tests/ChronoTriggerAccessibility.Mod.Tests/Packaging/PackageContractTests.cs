using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Packaging;

public sealed class PackageContractTests
{

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

    // The end-to-end deployment test that used to live here exercised the
    // superseded architecture: an external Reloaded-II installation, an Ultimate
    // ASI Loader winmm.dll proxy, and a PowerShell launcher. That design is gone.
    // Equivalent coverage for the portable layout, including transaction rollback,
    // now lives in DeploymentTransactionTests.

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
        const string canonicalHash = "850FE6F819647C199DF85E1BBEAF876A28BE808A05719A5AFA09D7A5AA75AF5B";
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
        // Nested builds must exit completely: reusable MSBuild nodes can retain
        // redirected pipes after packaging finishes and strand the reader joins.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        startInfo.Environment["PSModulePath"] = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "Modules");
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
