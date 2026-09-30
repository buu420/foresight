using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Packaging;

/// <summary>
/// Covers <c>tools\Deploy-Mod.ps1</c> and <c>tools\Verify-Deployment.ps1</c> for the
/// portable Reloaded-II layout: a trimmed loader tree plus a native proxy and helper
/// inside the game folder, with no external Reloaded-II installation.
///
/// Deployment is exercised against a synthetic game folder built in the temp
/// directory. The real <c>Chrono Trigger.exe</c> is copied into it because the
/// deployer is fail-closed on that file's SHA-256, and weakening that check for
/// the sake of testing would defeat its purpose. Tests skip when the real
/// executable is not reachable.
/// </summary>
public sealed class DeploymentTransactionTests
{
    private const string SupportedGameSha256 =
        "8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7";

    [Fact]
    public void Deployment_produces_the_complete_portable_layout()
    {
        using var fixture = DeploymentFixture.TryCreate();
        if (fixture is null) return;

        var result = fixture.Deploy();
        Assert.True(result.ExitCode == 0, $"Deployment failed.\n{result.Output}");

        foreach (var relative in new[]
                 {
                     @"Reloaded-II\Loader\X86\Reloaded.Mod.Loader.dll",
                     @"Reloaded-II\Loader\X86\Reloaded.Mod.Loader.runtimeconfig.json",
                     @"Reloaded-II\Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll",
                     @"Reloaded-II\Mods\reloaded.sharedlib.hooks\ModConfig.json",
                     @"Reloaded-II\Mods\chrono.trigger.accessibility\ChronoTriggerAccessibility.Mod.dll",
                     @"Reloaded-II\Mods\chrono.trigger.accessibility\prism.dll",
                     @"Reloaded-II\Apps\chrono trigger.exe\AppConfig.json",
                     @"winmm.dll",
                     @"Accessibility\Bootstrap\Foresight.Bootstrap.exe",
                     @"Foresight-SHA256SUMS.txt",
                 })
        {
            var path = Path.Combine(fixture.GameRoot, relative);
            Assert.True(File.Exists(path), $"Deployment did not produce: {relative}");
        }
        Assert.False(File.Exists(Path.Combine(fixture.GameRoot,
            @"Accessibility\Launcher\ChronoTriggerAccessibility.Installer.exe")));
    }

    [Fact]
    public void Deployed_app_config_enables_the_mod_and_its_hook_dependency()
    {
        using var fixture = DeploymentFixture.TryCreate();
        if (fixture is null) return;

        var result = fixture.Deploy();
        Assert.True(result.ExitCode == 0, $"Deployment failed.\n{result.Output}");

        var configPath = Path.Combine(
            fixture.GameRoot, @"Reloaded-II\Apps\chrono trigger.exe\AppConfig.json");
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));

        Assert.Equal("chrono trigger.exe", document.RootElement.GetProperty("AppId").GetString());

        var enabled = document.RootElement.GetProperty("EnabledMods")
            .EnumerateArray().Select(element => element.GetString()).ToArray();

        // Without a Reloaded launcher GUI nothing resolves the mod's declared
        // dependency, so the hook library has to be enabled explicitly.
        Assert.Contains("reloaded.sharedlib.hooks", enabled);
        Assert.Contains("chrono.trigger.accessibility", enabled);
    }

    [Fact]
    public void Every_deployed_loader_binary_is_x86()
    {
        using var fixture = DeploymentFixture.TryCreate();
        if (fixture is null) return;

        var result = fixture.Deploy();
        Assert.True(result.ExitCode == 0, $"Deployment failed.\n{result.Output}");

        var loaderDirectory = Path.Combine(fixture.GameRoot, @"Reloaded-II\Loader");
        var offenders = Directory
            .EnumerateFiles(loaderDirectory, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Where(path => ReadPeMachine(path) != 0x014C)
            .ToArray();

        // Chrono Trigger is a 32-bit process. An x64 payload cannot be loaded into
        // it and cannot be converted, so this has to hold for every file.
        Assert.True(offenders.Length == 0,
            "Non-x86 loader binaries were deployed: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Deployment_never_modifies_the_game_executable()
    {
        using var fixture = DeploymentFixture.TryCreate();
        if (fixture is null) return;

        var gameExe = Path.Combine(fixture.GameRoot, "Chrono Trigger.exe");
        var before = Sha256(gameExe);

        var result = fixture.Deploy();
        Assert.True(result.ExitCode == 0, $"Deployment failed.\n{result.Output}");

        Assert.Equal(before, Sha256(gameExe));
        Assert.Equal(SupportedGameSha256, Sha256(gameExe));
    }

    [Theory]
    [InlineData("AfterMoveAside")]
    [InlineData("AfterPayloadCopy")]
    [InlineData("BeforeVerification")]
    public void Injected_failure_restores_every_preexisting_directory(string failurePoint)
    {
        using var fixture = DeploymentFixture.TryCreate();
        if (fixture is null) return;

        // Seed recognisable content in each directory deployment moves aside.
        var sentinels = new Dictionary<string, string>
        {
            [Path.Combine(fixture.GameRoot, @"Reloaded-II\Loader\sentinel.txt")] = "previous-loader",
            [Path.Combine(fixture.GameRoot, @"Reloaded-II\Mods\chrono.trigger.accessibility\sentinel.txt")] = "previous-mod",
            [Path.Combine(fixture.GameRoot, @"Accessibility\Launcher\sentinel.txt")] = "unrelated-launcher-file",
            [Path.Combine(fixture.GameRoot, @"Accessibility\AudioDescriptions\sentinel.txt")] = "existing-narration",
            [Path.Combine(fixture.GameRoot, @"save\sentinel.txt")] = "existing-save",
            [Path.Combine(fixture.GameRoot, @"Reloaded-II\Apps\chrono trigger.exe\AppConfig.json")] = "previous-config",
        };
        foreach (var (path, content) in sentinels)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        var result = fixture.Deploy(failureInjectionPoint: failurePoint);
        Assert.True(result.ExitCode != 0,
            $"Deployment was expected to fail at {failurePoint}.\n{result.Output}");
        Assert.Contains($"Injected failure: {failurePoint}", result.Output, StringComparison.Ordinal);

        foreach (var (path, content) in sentinels)
        {
            Assert.True(File.Exists(path),
                $"Rollback lost a pre-existing file at {failurePoint}: {path}\n{result.Output}");
            Assert.Equal(content, File.ReadAllText(path));
        }
        Assert.False(File.Exists(Path.Combine(fixture.GameRoot, "winmm.dll")));
        Assert.False(File.Exists(Path.Combine(fixture.GameRoot,
            @"Accessibility\Bootstrap\Foresight.Bootstrap.exe")));
    }

    [Fact]
    public void Deployment_refuses_a_foreign_proxy_without_changing_it()
    {
        using var fixture = DeploymentFixture.TryCreate();
        if (fixture is null) return;

        var proxy = Path.Combine(fixture.GameRoot, "winmm.dll");
        File.WriteAllText(proxy, "another mods loader");
        var before = Sha256(proxy);
        var result = fixture.Deploy();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("winmm.dll", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, Sha256(proxy));
        Assert.False(Directory.Exists(Path.Combine(fixture.GameRoot, "Reloaded-II")));
    }

    [Fact]
    public void Updating_a_recognized_installation_preserves_narration_and_rolls_back_proxy()
    {
        using var fixture = DeploymentFixture.TryCreate();
        if (fixture is null) return;
        var initial = fixture.Deploy();
        Assert.True(initial.ExitCode == 0, initial.Output);
        var proxy = Path.Combine(fixture.GameRoot, "winmm.dll");
        var before = Sha256(proxy);
        var manifest = File.ReadAllText(Path.Combine(fixture.GameRoot, "Foresight-SHA256SUMS.txt"));
        var narration = Path.Combine(fixture.GameRoot, @"Accessibility\AudioDescriptions\installed-movies.json");
        Directory.CreateDirectory(Path.GetDirectoryName(narration)!);
        File.WriteAllText(narration, "existing narration manifest");

        var failed = fixture.Deploy("BeforeVerification");
        Assert.NotEqual(0, failed.ExitCode);
        Assert.Contains("Injected failure: BeforeVerification", failed.Output, StringComparison.Ordinal);
        Assert.Equal(before, Sha256(proxy));
        Assert.Equal(manifest, File.ReadAllText(Path.Combine(fixture.GameRoot, "Foresight-SHA256SUMS.txt")));
        Assert.Equal("existing narration manifest", File.ReadAllText(narration));
        var update = fixture.Deploy();
        Assert.True(update.ExitCode == 0, update.Output);
    }

    [Theory]
    [InlineData("winmm.dll")]
    [InlineData(@"Accessibility\Bootstrap\Foresight.Bootstrap.exe")]
    public void Verification_rejects_a_changed_bootstrap_binary(string relative)
    {
        using var fixture = DeploymentFixture.TryCreate();
        if (fixture is null) return;
        var deployed = fixture.Deploy();
        Assert.True(deployed.ExitCode == 0, deployed.Output);
        var path = Path.Combine(fixture.GameRoot, relative);
        using (var stream = new FileStream(path, FileMode.Append)) stream.WriteByte(1);

        var verified = fixture.Verify();
        Assert.NotEqual(0, verified.ExitCode);
        Assert.Contains("SHA256", verified.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Developer_deployment_never_registers_the_old_launcher()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), @"tools\Deploy-Mod.ps1"));
        Assert.DoesNotContain("Start-Process -FilePath $installer", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-ForesightLegacyMigration", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Verification_fails_when_the_mod_payload_is_missing()
    {
        using var fixture = DeploymentFixture.TryCreate();
        if (fixture is null) return;

        var deployed = fixture.Deploy();
        Assert.True(deployed.ExitCode == 0, $"Deployment failed.\n{deployed.Output}");

        File.Delete(Path.Combine(fixture.GameRoot,
            @"Reloaded-II\Mods\chrono.trigger.accessibility\ChronoTriggerAccessibility.Mod.dll"));

        var verified = fixture.Verify();
        Assert.True(verified.ExitCode != 0,
            $"Verification passed despite a missing mod DLL.\n{verified.Output}");
        Assert.Contains("FAIL", verified.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Verification_fails_when_a_loader_binary_is_not_x86()
    {
        using var fixture = DeploymentFixture.TryCreate();
        if (fixture is null) return;

        var deployed = fixture.Deploy();
        Assert.True(deployed.ExitCode == 0, $"Deployment failed.\n{deployed.Output}");

        // Rewrite the machine field to x64 in place; everything else stays valid.
        var loaderDll = Path.Combine(fixture.GameRoot,
            @"Reloaded-II\Loader\X86\Reloaded.Mod.Loader.dll");
        var bytes = File.ReadAllBytes(loaderDll);
        var peOffset = BitConverter.ToInt32(bytes, 0x3C);
        BitConverter.GetBytes((ushort)0x8664).CopyTo(bytes, peOffset + 4);
        File.WriteAllBytes(loaderDll, bytes);

        var verified = fixture.Verify();
        Assert.True(verified.ExitCode != 0,
            $"Verification passed despite an x64 loader binary.\n{verified.Output}");
    }

    [Theory]
    [InlineData("*.ps1")]
    [InlineData("*.psm1")]
    [InlineData("*.cs")]
    [InlineData("*.cpp")]
    [InlineData("*.h")]
    public void Repository_sources_contain_no_absolute_machine_paths(string searchPattern)
    {
        var repositoryRoot = FindRepositoryRoot();

        // Machine-specific roots. The mod must be installable by any user from any
        // location, so every path is derived from the script or assembly location,
        // or supplied as a parameter. Stale literals like G:\SteamLibrary are the
        // exact reason the mod stopped launching once the game moved.
        // A C# verbatim string doubles the separator, so both forms are matched.
        var pattern = new Regex(
            @"[A-Za-z]:\\\\?(Users|Program Files|Program Files \(x86\)|Games|SteamLibrary)",
            RegexOptions.IgnoreCase);

        var offenders = new List<string>();
        foreach (var file in Directory
                     .EnumerateFiles(repositoryRoot, searchPattern, SearchOption.AllDirectories)
                     .Where(path => !IsExcludedPath(path, repositoryRoot)))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var trimmed = line.TrimStart();
                // Comments may legitimately cite a path as an example or a warning.
                if (trimmed.StartsWith('#') || trimmed.StartsWith("//")) continue;
                if (pattern.IsMatch(line))
                {
                    offenders.Add($"{Path.GetRelativePath(repositoryRoot, file)}:{index + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"Absolute machine paths found in {searchPattern} sources:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    private static bool IsExcludedPath(string path, string repositoryRoot)
    {
        var relative = Path.GetRelativePath(repositoryRoot, path);
        var separator = Path.DirectorySeparatorChar;
        foreach (var excluded in new[] { ".git", "artifacts", ".build", ".worktrees", "bin", "obj", "docs" })
        {
            if (relative.StartsWith(excluded + separator, StringComparison.OrdinalIgnoreCase) ||
                relative.Contains(separator + excluded + separator, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static ushort ReadPeMachine(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        stream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset >= stream.Length) return 0;
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550) return 0;
        return reader.ReadUInt16();
    }

    private static string Sha256(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ChronoTriggerAccessibility.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository root.");
    }

    /// <summary>
    /// A throwaway game folder containing a real copy of the supported executable,
    /// so the fail-closed hash check in the deployer runs exactly as it does in
    /// production.
    /// </summary>
    private sealed class DeploymentFixture : IDisposable
    {
        private DeploymentFixture(string root, string repositoryRoot, string packageDirectory)
        {
            Root = root;
            RepositoryRoot = repositoryRoot;
            PackageDirectory = packageDirectory;
            GameRoot = Path.Combine(root, "game");
        }

        public string Root { get; }
        public string RepositoryRoot { get; }
        public string PackageDirectory { get; }
        public string GameRoot { get; }

        /// <summary>
        /// Returns null when the prerequisites for a real deployment are absent, so
        /// the suite stays green on a machine without the game or a built payload.
        /// </summary>
        public static DeploymentFixture? TryCreate()
        {
            var repositoryRoot = FindRepositoryRoot();

            var sourceExe = Path.Combine(Directory.GetParent(repositoryRoot)!.FullName, "Chrono Trigger.exe");
            var package = Path.Combine(repositoryRoot, @"artifacts\package\chrono.trigger.accessibility");
            var native = Path.Combine(repositoryRoot, @".build\native");

            if (!File.Exists(sourceExe)) return null;
            if (!File.Exists(Path.Combine(package, "ChronoTriggerAccessibility.Mod.dll"))) return null;
            if (!File.Exists(Path.Combine(native, "winmm.dll"))) return null;
            if (!File.Exists(Path.Combine(native, "Foresight.Bootstrap.exe"))) return null;
            var sourceRuntime = Path.Combine(Directory.GetParent(repositoryRoot)!.FullName,
                @"Accessibility\Runtime\dotnet\x86");
            if (!File.Exists(Path.Combine(sourceRuntime, "dotnet.exe"))) return null;

            var root = Path.Combine(Path.GetTempPath(), $"cta-deploy-{Guid.NewGuid():N}");
            var fixture = new DeploymentFixture(root, repositoryRoot, package);
            Directory.CreateDirectory(fixture.GameRoot);
            File.Copy(sourceExe, Path.Combine(fixture.GameRoot, "Chrono Trigger.exe"));
            // Runtime discovery/architecture is checked, but fixture tests never
            // execute the game or the CLR. Copy only the PE files those checks use.
            var runtime = Path.Combine(fixture.GameRoot, @"Accessibility\Runtime\dotnet\x86");
            foreach (var source in new[] { Path.Combine(sourceRuntime, "dotnet.exe") }
                         .Concat(Directory.EnumerateFiles(Path.Combine(sourceRuntime, "host"), "hostfxr.dll", SearchOption.AllDirectories))
                         .Concat(Directory.EnumerateFiles(Path.Combine(sourceRuntime, "shared"), "coreclr.dll", SearchOption.AllDirectories)))
            {
                var target = Path.Combine(runtime, Path.GetRelativePath(sourceRuntime, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
            return fixture;
        }

        public ProcessResult Deploy(string failureInjectionPoint = "None") => RunPowerShell(
            Path.Combine(RepositoryRoot, @"tools\Deploy-Mod.ps1"),
            "-GameRoot", GameRoot,
            "-PackageDirectory", PackageDirectory,
            "-SkipIfeo",
            "-SkipNativeBuild",
            "-FailureInjectionPoint", failureInjectionPoint);

        public ProcessResult Verify() => RunPowerShell(
            Path.Combine(RepositoryRoot, @"tools\Verify-Deployment.ps1"),
            "-GameRoot", GameRoot, "-SkipRegistryCheck");

        private ProcessResult RunPowerShell(string script, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.Environment["PSModulePath"] = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "Modules");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(script);
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start powershell.exe.");
            var output = new StringBuilder();
            output.Append(process.StandardOutput.ReadToEnd());
            output.Append(process.StandardError.ReadToEnd());
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, output.ToString());
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A throwaway temp folder; losing the cleanup race is not a test failure.
            }
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
