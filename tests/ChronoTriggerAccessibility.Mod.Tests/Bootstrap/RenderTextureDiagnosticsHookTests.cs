using System.Buffers.Binary;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Reloaded;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Bootstrap;

public sealed class RenderTextureDiagnosticsHookTests
{
    [Fact]
    public void OptionalDiagnosticsDeclineAnUnauditedProcessWithoutDisablingMenus()
    {
        var messages = new List<string>();
        var hook = new RenderTextureDiagnosticsHook(null!, "unused", "test", messages.Add).Prepare(null!, null!);
        Assert.IsAssignableFrom<IOptionalPreparedHook>(hook);
        hook.Activate();
        Assert.False(hook.IsActive);
        Assert.Single(messages);
        hook.Disable();
    }

    [Fact]
    public void EntryGuardAccountsForRelocationAndRejectsChangedInstructions()
    {
        var bytes = Convert.FromHexString("6A04B8FC614810E8D8AD1400");
        var memory = new Memory(bytes);
        Assert.True(RenderTextureDiagnosticsHook.EntryMatches(memory, 0x10000000));
        Assert.False(RenderTextureDiagnosticsHook.EntryMatches(memory, 0x20000000));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(3, 4), 0x204861FC);
        Assert.True(RenderTextureDiagnosticsHook.EntryMatches(memory, 0x20000000));
        bytes[8] ^= 1;
        Assert.False(RenderTextureDiagnosticsHook.EntryMatches(memory, 0x20000000));
        Assert.False(RenderTextureDiagnosticsHook.EntryMatches(memory, 0xFFFFF000));
    }

    [Fact]
    public void ActivationFailureDisablesTheDetourAndKeepsItsDelegateRoot()
    {
        var inner = new Hook { FailActivation = true };
        var messages = new List<string>();
        var hook = new OptionalTextureDiagnosticHook(inner, () => throw new Exception("should not be ready"), messages.Add);
        hook.Activate();
        Assert.False(hook.IsActive);
        Assert.Single(messages);
        Assert.Contains(inner.Root, hook.LifetimeRoots);
    }

    [Fact]
    public void FailedRollbackIsNotHiddenAsAnOptionalFeatureFailure()
    {
        var inner = new Hook { FailActivation = true, FailDisable = true };
        var hook = new OptionalTextureDiagnosticHook(inner, () => { }, _ => { });
        Assert.Throws<IOException>(() => hook.Activate());
        Assert.True(hook.IsActive);
        Assert.Contains(inner.Root, hook.LifetimeRoots);
    }

    [Fact]
    public void ReadinessLoggingFailureDoesNotDeactivateASuccessfullyInstalledDetour()
    {
        var inner = new Hook();
        var hook = new OptionalTextureDiagnosticHook(inner, () => throw new IOException("disk full"), _ => { });
        hook.Activate();
        Assert.True(hook.IsActive);
        hook.Disable();
        Assert.False(hook.IsActive);
    }

    private sealed class Memory(byte[] bytes) : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination)
        {
            _ = address;
            bytes.CopyTo(destination);
            return true;
        }
    }

    private sealed class Hook : IPreparedHook
    {
        public object Root { get; } = new();
        public bool FailActivation { get; init; }
        public bool FailDisable { get; init; }
        public string Name => "fixture";
        public bool IsActive { get; private set; }
        public IReadOnlyCollection<object> LifetimeRoots => [Root];
        public void Activate() { IsActive = true; if (FailActivation) throw new IOException("activation"); }
        public void Disable() { if (FailDisable) throw new IOException("disable"); IsActive = false; }
    }
}
