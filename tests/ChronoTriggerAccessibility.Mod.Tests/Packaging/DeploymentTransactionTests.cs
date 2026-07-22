using System.Diagnostics;
using System.Text;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Packaging;

public sealed class DeploymentTransactionTests
{
    private const string GameExecutable = @"G:\SteamLibrary\steamapps\common\Chrono Trigger\Chrono Trigger.exe";
    private const string InstalledReloadedRoot = @"C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release";
    private const string RuntimeRoot = @"C:\Users\User\AppData\Local\ChronoTriggerAccessibility\dotnet-x86";

    [Theory]
    [InlineData("AfterModSwap")]
    [InlineData("AfterProfileCommit")]
    [InlineData("AfterLauncherCommit")]
    [InlineData("FinalVerification")]
    public void Injected_failure_restores_every_preexisting_artifact(string failurePoint)
    {
        var fixture = DeploymentFixture.Create(seedExistingArtifacts: true);
        try
        {
            var originalMod = SnapshotDirectory(fixture.ModDirectory);
            var originalProfile = File.ReadAllBytes(fixture.ProfilePath);
            var originalLauncher = File.ReadAllBytes(fixture.LauncherPath);

            var result = fixture.Deploy("-FailureInjectionPoint", failurePoint);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains($"Injected deployment failure at {failurePoint}", result.Output, StringComparison.Ordinal);
            AssertDirectorySnapshot(originalMod, fixture.ModDirectory);
            Assert.Equal(originalProfile, File.ReadAllBytes(fixture.ProfilePath));
            Assert.Equal(originalLauncher, File.ReadAllBytes(fixture.LauncherPath));
            AssertNoTransactionArtifacts(fixture.ReloadedRoot, fixture.LauncherDirectory);
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
            AssertNoTransactionArtifacts(fixture.ReloadedRoot, fixture.LauncherDirectory);
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

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(fixture.ModDirectory, "SHA256SUMS.txt")));
            Assert.True(File.Exists(fixture.ProfilePath));
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(fixture.RepositoryRoot, "Launch Chrono Trigger Accessible.ps1")),
                File.ReadAllBytes(fixture.LauncherPath));
            AssertNoTransactionArtifacts(fixture.ReloadedRoot, fixture.LauncherDirectory);
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
            ModDirectory = Path.Combine(ReloadedRoot, "Mods", "chrono.trigger.accessibility");
            ProfilePath = Path.Combine(ReloadedRoot, "Apps", "chrono trigger.exe", "AppConfig.json");
            LauncherPath = Path.Combine(LauncherDirectory, "Launch Chrono Trigger Accessible.ps1");
        }

        public string RepositoryRoot { get; }
        public string TemporaryRoot { get; }
        public string PackageDirectory { get; }
        public string ReloadedRoot { get; }
        public string LauncherDirectory { get; }
        public string ModDirectory { get; }
        public string ProfilePath { get; }
        public string LauncherPath { get; }

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
                "-LauncherDestinationDirectory", LauncherDirectory
            };
            arguments.AddRange(extraArguments);
            return RunPowerShell(
                RepositoryRoot,
                Path.Combine(RepositoryRoot, "tools", "Deploy-Mod.ps1"),
                arguments.ToArray());
        }

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
                "{\"AppId\":\"chrono trigger.exe\",\"EnabledMods\":[\"other.mod\"],\"SortedMods\":[\"other.mod\"],\"Custom\":\"restore profile exactly\"}");

            Directory.CreateDirectory(LauncherDirectory);
            File.WriteAllText(LauncherPath, "# restore this launcher exactly\r\n");
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
