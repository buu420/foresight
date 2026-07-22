using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using ChronoTriggerAccessibility.Native.Build;
using ChronoTriggerAccessibility.Native.Hooks;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Build;

public sealed class ExecutableVerifierTests
{
    private const string InstalledExecutable = @"G:\SteamLibrary\steamapps\common\Chrono Trigger\Chrono Trigger.exe";

    [Fact]
    public void InstalledExecutable_HasExactSupportedIdentityAndHookBytesInExecutableText()
    {
        Assert.True(File.Exists(InstalledExecutable), $"Installed game executable was not found at '{InstalledExecutable}'.");

        using var image = PeImage.OpenRead(InstalledExecutable);

        Assert.Equal("8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7", image.Sha256);
        Assert.Equal(Machine.I386, image.Machine);
        Assert.Equal(0x00400000UL, image.ImageBase);

        foreach (var hook in GameVersionCatalog.Hooks)
        {
            var section = image.GetSectionContaining(hook.Rva, hook.ExpectedBytes.Length);
            Assert.Equal(".text", section.Name);
            Assert.True(section.IsExecutable, $"{hook.Symbol} is not inside an executable section.");
            Assert.Equal(hook.ExpectedBytes.ToArray(), image.ReadBytesAtRva(hook.Rva, hook.ExpectedBytes.Length));
        }
    }

    [Fact]
    public void Verify_ExactInstalledBuild_ReturnsOneAtomicVerifiedExecutable()
    {
        var result = new ExecutableVerifier().Verify(
            InstalledExecutable,
            GameVersionCatalog.Executable,
            GameVersionCatalog.Hooks);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Failures));
        Assert.Empty(result.Failures);
        var verified = Assert.IsType<VerifiedExecutable>(result.VerifiedExecutable);
        Assert.Equal(GameVersionCatalog.Hooks.Count, verified.Hooks.Count);

        const nuint moduleBase = 0x50000000;
        var addresses = verified.ResolveInProcessAddresses(moduleBase);
        Assert.Equal(GameVersionCatalog.Hooks.Count, addresses.Count);
        foreach (var hook in GameVersionCatalog.Hooks)
        {
            Assert.Equal(moduleBase + hook.Rva, addresses[hook.Id]);
        }
    }

    [Fact]
    public void Verify_OneCorruptHookByte_RejectsTheEntireHookTransaction()
    {
        var bytes = File.ReadAllBytes(InstalledExecutable);
        var corruptHook = GameVersionCatalog.Hooks[7];
        int fileOffset;
        using (var image = PeImage.FromBytes(bytes))
        {
            fileOffset = image.RvaToFileOffset(corruptHook.Rva, corruptHook.ExpectedBytes.Length);
        }

        bytes[fileOffset] ^= 0xFF;
        var fixtureIdentity = GameVersionCatalog.Executable with
        {
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
        };

        var result = new ExecutableVerifier().Verify(
            bytes,
            fixtureIdentity,
            GameVersionCatalog.Hooks);

        Assert.False(result.Succeeded);
        Assert.Null(result.VerifiedExecutable);
        Assert.Contains(result.Failures, failure => failure.Contains(corruptHook.Symbol, StringComparison.Ordinal));
    }
}
