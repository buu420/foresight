using System.Diagnostics;
using System.Text;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Packaging;

public sealed class DeploymentTransactionTests
{
    private const string InstalledGameExecutable = @"G:\SteamLibrary\steamapps\common\Chrono Trigger\Chrono Trigger.exe";
    private const string InstalledReloadedRoot = @"C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release";
    private const string RuntimeRoot = @"C:\Users\User\AppData\Local\ChronoTriggerAccessibility\dotnet-x86";

    [Theory]
    [InlineData("AfterModSwap")]
    [InlineData("AfterProfileCommit")]
    [InlineData("AfterLauncherCommit")]
    [InlineData("AfterAsiLoaderCommit")]
    [InlineData("AfterAutoLaunchCommit")]
    [InlineData("FinalVerification")]
    public void Injected_failure_restores_every_preexisting_artifact(string failurePoint)
    {
        var fixture = DeploymentFixture.Create(seedExistingArtifacts: true);
        try
        {
            var originalMod = SnapshotDirectory(fixture.ModDirectory);
            var originalProfile = File.ReadAllBytes(fixture.ProfilePath);
            var originalLauncher = File.ReadAllBytes(fixture.LauncherPath);
            var originalAsiLoader = File.ReadAllBytes(fixture.AsiLoaderPath);
            var originalBootstrapper = File.ReadAllBytes(fixture.BootstrapperPath);

            var result = fixture.Deploy("-FailureInjectionPoint", failurePoint);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains($"Injected deployment failure at {failurePoint}", result.Output, StringComparison.Ordinal);
            AssertDirectorySnapshot(originalMod, fixture.ModDirectory);
            Assert.Equal(originalProfile, File.ReadAllBytes(fixture.ProfilePath));
            Assert.Equal(originalLauncher, File.ReadAllBytes(fixture.LauncherPath));
            Assert.Equal(originalAsiLoader, File.ReadAllBytes(fixture.AsiLoaderPath));
            Assert.Equal(originalBootstrapper, File.ReadAllBytes(fixture.BootstrapperPath));
            AssertNoTransactionArtifacts(fixture.ReloadedRoot, fixture.LauncherDirectory, fixture.GameDirectory);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Theory]
    [InlineData("AfterModSwap")]
    [InlineData("AfterProfileCommit")]
    [InlineData("AfterLauncherCommit")]
    [InlineData("AfterAsiLoaderCommit")]
    [InlineData("AfterAutoLaunchCommit")]
    [InlineData("FinalVerification")]
    public void Failed_first_install_removes_every_new_target(string failurePoint)
    {
        var fixture = DeploymentFixture.Create(seedExistingArtifacts: false);
        try
        {
            var result = fixture.Deploy("-FailureInjectionPoint", failurePoint);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains($"Injected deployment failure at {failurePoint}", result.Output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(fixture.ModDirectory));
            Assert.False(File.Exists(fixture.ProfilePath));
            Assert.False(File.Exists(fixture.LauncherPath));
            Assert.False(File.Exists(fixture.AsiLoaderPath));
            Assert.False(File.Exists(fixture.BootstrapperPath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(fixture.ProfilePath)!));
            Assert.False(Directory.Exists(Path.Combine(fixture.ReloadedRoot, "Apps")));
            Assert.False(Directory.Exists(fixture.LauncherDirectory));
            AssertNoTransactionArtifacts(fixture.ReloadedRoot, fixture.LauncherDirectory, fixture.GameDirectory);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Successful_deployment_removes_all_transaction_backups()
    {
        var fixture = DeploymentFixture.Create(seedExistingArtifacts: true);
        try
        {
            var result = fixture.Deploy();

            Assert.True(result.ExitCode == 0, $"Deployment failed.{Environment.NewLine}{result.Output}");
            Assert.True(File.Exists(Path.Combine(fixture.ModDirectory, "SHA256SUMS.txt")));
            Assert.True(File.Exists(fixture.ProfilePath));
            using (var profile = System.Text.Json.JsonDocument.Parse(File.ReadAllText(fixture.ProfilePath)))
                Assert.False(profile.RootElement.GetProperty("AutoInject").GetBoolean());
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(fixture.RepositoryRoot, "Launch Chrono Trigger Accessible.ps1")),
                File.ReadAllBytes(fixture.LauncherPath));
            Assert.Equal("A51C630B2EA3D78AD55A330EA64D510C8C0737F620BE65AD7503B61840D59E37", HashFile(fixture.AsiLoaderPath));
            Assert.Equal("1A9F704549F66E357C0D22C395B57FE4E7BD5248521DBB40E566D2EE1CA809AB", HashFile(fixture.BootstrapperPath));
            AssertNoTransactionArtifacts(fixture.ReloadedRoot, fixture.LauncherDirectory, fixture.GameDirectory);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Verification_rejects_a_missing_automatic_startup_loader()
    {
        var fixture = DeploymentFixture.Create(seedExistingArtifacts: false);
        try
        {
            var deploy = fixture.Deploy();
            Assert.True(deploy.ExitCode == 0, $"Deployment failed.{Environment.NewLine}{deploy.Output}");

            File.Delete(fixture.AsiLoaderPath);
            var verify = fixture.Verify();

            Assert.NotEqual(0, verify.ExitCode);
            Assert.Contains("winmm.dll", verify.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Deployment_refuses_an_unrelated_existing_winmm_proxy_without_changing_anything()
    {
        var fixture = DeploymentFixture.Create(seedExistingArtifacts: true);
        try
        {
            File.WriteAllText(fixture.AsiLoaderPath, "unrelated pre-existing proxy");
            var originalMod = SnapshotDirectory(fixture.ModDirectory);
            var originalProfile = File.ReadAllBytes(fixture.ProfilePath);
            var originalLauncher = File.ReadAllBytes(fixture.LauncherPath);
            var originalAsiLoader = File.ReadAllBytes(fixture.AsiLoaderPath);
            var originalBootstrapper = File.ReadAllBytes(fixture.BootstrapperPath);

            var deploy = fixture.Deploy();

            Assert.NotEqual(0, deploy.ExitCode);
            Assert.Contains("Refusing to replace", deploy.Output, StringComparison.OrdinalIgnoreCase);
            AssertDirectorySnapshot(originalMod, fixture.ModDirectory);
            Assert.Equal(originalProfile, File.ReadAllBytes(fixture.ProfilePath));
            Assert.Equal(originalLauncher, File.ReadAllBytes(fixture.LauncherPath));
            Assert.Equal(originalAsiLoader, File.ReadAllBytes(fixture.AsiLoaderPath));
            Assert.Equal(originalBootstrapper, File.ReadAllBytes(fixture.BootstrapperPath));
            AssertNoTransactionArtifacts(fixture.ReloadedRoot, fixture.LauncherDirectory, fixture.GameDirectory);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Deployment_refuses_an_unrelated_existing_bootstrapper_without_changing_anything()
    {
        var fixture = DeploymentFixture.Create(seedExistingArtifacts: true);
        try
        {
            File.WriteAllText(fixture.BootstrapperPath, "unrelated pre-existing bootstrapper");
            var originalMod = SnapshotDirectory(fixture.ModDirectory);
            var originalProfile = File.ReadAllBytes(fixture.ProfilePath);
            var originalLauncher = File.ReadAllBytes(fixture.LauncherPath);
            var originalAsiLoader = File.ReadAllBytes(fixture.AsiLoaderPath);
            var originalBootstrapper = File.ReadAllBytes(fixture.BootstrapperPath);

            var deploy = fixture.Deploy();

            Assert.NotEqual(0, deploy.ExitCode);
            Assert.Contains("Refusing to replace", deploy.Output, StringComparison.OrdinalIgnoreCase);
            AssertDirectorySnapshot(originalMod, fixture.ModDirectory);
            Assert.Equal(originalProfile, File.ReadAllBytes(fixture.ProfilePath));
            Assert.Equal(originalLauncher, File.ReadAllBytes(fixture.LauncherPath));
            Assert.Equal(originalAsiLoader, File.ReadAllBytes(fixture.AsiLoaderPath));
            Assert.Equal(originalBootstrapper, File.ReadAllBytes(fixture.BootstrapperPath));
            AssertNoTransactionArtifacts(fixture.ReloadedRoot, fixture.LauncherDirectory, fixture.GameDirectory);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Verification_rejects_a_stale_global_reloaded_loader_path()
    {
        var fixture = DeploymentFixture.Create(seedExistingArtifacts: false);
        try
        {
            var deploy = fixture.Deploy();
            Assert.True(deploy.ExitCode == 0, $"Deployment failed.{Environment.NewLine}{deploy.Output}");
            File.WriteAllText(
                fixture.ReloadedConfigPath,
                "{\"LoaderPath32\":\"C:\\\\Stale\\\\Reloaded.Mod.Loader.dll\",\"LauncherPath\":\"C:\\\\Stale\\\\Reloaded-II.exe\",\"ApplicationConfigDirectory\":\"C:\\\\Stale\\\\Apps\",\"ModConfigDirectory\":\"C:\\\\Stale\\\\Mods\"}");

            var verify = fixture.Verify();

            Assert.NotEqual(0, verify.ExitCode);
            Assert.Contains("ReloadedII.json", verify.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("LoaderPath32", verify.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static Dictionary<string, byte[]> SnapshotDirectory(string directory) =>
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(directory, path),
                File.ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);

    private static string HashFile(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    private static void AssertDirectorySnapshot(IReadOnlyDictionary<string, byte[]> expected, string directory)
    {
        var actual = SnapshotDirectory(directory);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var (relativePath, bytes) in expected)
            Assert.Equal(bytes, actual[relativePath]);
    }

    private static void AssertNoTransactionArtifacts(params string[] roots)
    {
        var debris = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
            .Where(path =>
                Path.GetFileName(path).Contains(".staging.", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(path).Contains(".backup.", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(path).Contains(".transaction.", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(debris);
    }

    private sealed class DeploymentFixture : IDisposable
    {
        private DeploymentFixture(string repositoryRoot, string temporaryRoot)
        {
            RepositoryRoot = repositoryRoot;
            TemporaryRoot = temporaryRoot;
            PackageDirectory = Path.Combine(temporaryRoot, "package", "chrono.trigger.accessibility");
            ReloadedRoot = Path.Combine(temporaryRoot, "Reloaded-II");
            LauncherDirectory = Path.Combine(temporaryRoot, "launcher");
            GameDirectory = Path.Combine(temporaryRoot, "game");
            GameExecutable = Path.Combine(GameDirectory, "Chrono Trigger.exe");
            ModDirectory = Path.Combine(ReloadedRoot, "Mods", "chrono.trigger.accessibility");
            ProfilePath = Path.Combine(ReloadedRoot, "Apps", "chrono trigger.exe", "AppConfig.json");
            LauncherPath = Path.Combine(LauncherDirectory, "Launch Chrono Trigger Accessible.ps1");
            AsiLoaderPath = Path.Combine(GameDirectory, "winmm.dll");
            BootstrapperPath = Path.Combine(GameDirectory, "Reloaded.Mod.Loader.Bootstrapper.asi");
            ReloadedConfigPath = Path.Combine(temporaryRoot, "ReloadedConfig", "ReloadedII.json");
        }

        public string RepositoryRoot { get; }
        public string TemporaryRoot { get; }
        public string PackageDirectory { get; }
        public string ReloadedRoot { get; }
        public string LauncherDirectory { get; }
        public string GameDirectory { get; }
        public string GameExecutable { get; }
        public string ModDirectory { get; }
        public string ProfilePath { get; }
        public string LauncherPath { get; }
        public string AsiLoaderPath { get; }
        public string BootstrapperPath { get; }
        public string ReloadedConfigPath { get; }

        public static DeploymentFixture Create(bool seedExistingArtifacts)
        {
            var fixture = new DeploymentFixture(
                FindRepositoryRoot(),
                Path.Combine(Path.GetTempPath(), $"chrono-trigger-accessibility-transaction-{Guid.NewGuid():N}"));

            try
            {
                Directory.CreateDirectory(fixture.ReloadedRoot);
                File.Copy(
                    Path.Combine(InstalledReloadedRoot, "Reloaded-II.exe"),
                    Path.Combine(fixture.ReloadedRoot, "Reloaded-II.exe"));
                SeedSharedHookDependency(fixture.ReloadedRoot);
                SeedAutoLaunchDependency(fixture.ReloadedRoot);
                SeedReloadedBootstrapConfiguration(fixture.ReloadedRoot, fixture.ReloadedConfigPath);
                Directory.CreateDirectory(fixture.GameDirectory);
                File.Copy(InstalledGameExecutable, fixture.GameExecutable);

                var package = RunPowerShell(
                    fixture.RepositoryRoot,
                    Path.Combine(fixture.RepositoryRoot, "tools", "Package-Mod.ps1"),
                    "-OutputDirectory", fixture.PackageDirectory,
                    "-Configuration", "Release",
                    "-SkipBuild");
                Assert.Equal(0, package.ExitCode);

                if (seedExistingArtifacts)
                    fixture.SeedExistingArtifacts();

                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        public (int ExitCode, string Output) Deploy(params string[] extraArguments)
        {
            var arguments = new List<string>
            {
                "-PackageDirectory", PackageDirectory,
                "-ReloadedRoot", ReloadedRoot,
                "-GameExecutable", GameExecutable,
                "-RuntimeRoot", RuntimeRoot,
                "-LauncherDestinationDirectory", LauncherDirectory,
                "-ReloadedConfigPath", ReloadedConfigPath
            };
            arguments.AddRange(extraArguments);
            return RunPowerShell(
                RepositoryRoot,
                Path.Combine(RepositoryRoot, "tools", "Deploy-Mod.ps1"),
                arguments.ToArray());
        }

        public (int ExitCode, string Output) Verify() =>
            RunPowerShell(
                RepositoryRoot,
                Path.Combine(RepositoryRoot, "tools", "Verify-Deployment.ps1"),
                "-ReloadedRoot", ReloadedRoot,
                "-GameExecutable", GameExecutable,
                "-RuntimeRoot", RuntimeRoot,
                "-LauncherPath", LauncherPath,
                "-ReloadedConfigPath", ReloadedConfigPath);

        public void Dispose()
        {
            if (Directory.Exists(TemporaryRoot))
                Directory.Delete(TemporaryRoot, recursive: true);
        }

        private void SeedExistingArtifacts()
        {
            Directory.CreateDirectory(Path.Combine(ModDirectory, "nested"));
            File.WriteAllText(Path.Combine(ModDirectory, "legacy.txt"), "restore this mod");
            File.WriteAllBytes(Path.Combine(ModDirectory, "nested", "state.bin"), [0x00, 0x7F, 0xFF]);

            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            File.WriteAllText(
                ProfilePath,
                "{\"AppId\":\"chrono trigger.exe\",\"AutoInject\":true,\"EnabledMods\":[\"other.mod\"],\"SortedMods\":[\"other.mod\"],\"Custom\":\"restore profile exactly\"}");

            Directory.CreateDirectory(LauncherDirectory);
            File.WriteAllText(LauncherPath, "# restore this launcher exactly\r\n");

            File.Copy(
                Path.Combine(RepositoryRoot, "native", "ultimate-asi-loader", "v6.9.0", "win-x86", "UltimateAsiLoader.dll"),
                AsiLoaderPath);
            File.Copy(
                Path.Combine(InstalledReloadedRoot, "Loader", "X86", "Bootstrapper", "Reloaded.Mod.Loader.Bootstrapper.dll"),
                BootstrapperPath);
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
                System.Text.Json.JsonSerializer.Serialize(new
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
}
