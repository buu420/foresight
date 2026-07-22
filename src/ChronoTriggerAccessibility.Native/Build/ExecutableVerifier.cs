using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Native.Hooks;

namespace ChronoTriggerAccessibility.Native.Build;

public sealed class ExecutableVerifier
{
    public ExecutableVerificationResult Verify(
        string executablePath,
        ExecutableIdentity expectedIdentity,
        IReadOnlyList<HookContract> hooks)
    {
        ArgumentNullException.ThrowIfNull(expectedIdentity);
        ArgumentNullException.ThrowIfNull(hooks);

        try
        {
            using var image = PeImage.OpenRead(executablePath);
            return Verify(image, expectedIdentity, hooks);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return ExecutableVerificationResult.Failed($"Could not verify executable: {exception.Message}");
        }
    }

    public ExecutableVerificationResult Verify(
        ReadOnlyMemory<byte> executableBytes,
        ExecutableIdentity expectedIdentity,
        IReadOnlyList<HookContract> hooks)
    {
        ArgumentNullException.ThrowIfNull(expectedIdentity);
        ArgumentNullException.ThrowIfNull(hooks);

        try
        {
            using var image = PeImage.FromBytes(executableBytes.Span);
            return Verify(image, expectedIdentity, hooks);
        }
        catch (BadImageFormatException exception)
        {
            return ExecutableVerificationResult.Failed($"Could not verify executable: {exception.Message}");
        }
    }

    private static ExecutableVerificationResult Verify(
        PeImage image,
        ExecutableIdentity expectedIdentity,
        IReadOnlyList<HookContract> hooks)
    {
        var failures = new List<string>();
        if (!string.Equals(image.Sha256, expectedIdentity.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"Executable SHA-256 was {image.Sha256}; expected {expectedIdentity.Sha256}.");
        }

        if (image.Machine != expectedIdentity.Machine)
        {
            failures.Add($"Executable machine was {image.Machine}; expected {expectedIdentity.Machine}.");
        }

        if (image.ImageBase != expectedIdentity.ImageBase)
        {
            failures.Add($"Executable image base was 0x{image.ImageBase:X}; expected 0x{expectedIdentity.ImageBase:X}.");
        }

        if (hooks.Select(hook => hook.Id).Distinct().Count() != hooks.Count)
        {
            failures.Add("Hook catalog contains duplicate symbolic identities.");
        }

        foreach (var hook in hooks)
        {
            VerifyHook(image, hook, failures);
        }

        return failures.Count == 0
            ? ExecutableVerificationResult.Passed(new VerifiedExecutable(hooks))
            : ExecutableVerificationResult.Failed(failures);
    }

    private static void VerifyHook(PeImage image, HookContract hook, ICollection<string> failures)
    {
        try
        {
            if (hook.ExpectedBytes.IsDefaultOrEmpty)
            {
                failures.Add($"{hook.Symbol}: expected-byte contract is empty.");
                return;
            }

            var section = image.GetSectionContaining(hook.Rva, hook.ExpectedBytes.Length);
            if (!section.IsExecutable)
            {
                failures.Add($"{hook.Symbol}: RVA 0x{hook.Rva:X8} is not in an executable PE section.");
                return;
            }

            var actualBytes = image.ReadBytesAtRva(hook.Rva, hook.ExpectedBytes.Length);
            if (!actualBytes.AsSpan().SequenceEqual(hook.ExpectedBytes.AsSpan()))
            {
                failures.Add(
                    $"{hook.Symbol}: bytes at RVA 0x{hook.Rva:X8} were {Convert.ToHexString(actualBytes)}; " +
                    $"expected {Convert.ToHexString(hook.ExpectedBytes.AsSpan())}.");
            }
        }
        catch (BadImageFormatException exception)
        {
            failures.Add($"{hook.Symbol}: {exception.Message}");
        }
    }
}

public sealed class ExecutableVerificationResult
{
    private ExecutableVerificationResult(
        IReadOnlyList<string> failures,
        VerifiedExecutable? verifiedExecutable)
    {
        Failures = failures;
        VerifiedExecutable = verifiedExecutable;
    }

    public bool Succeeded => VerifiedExecutable is not null;

    public IReadOnlyList<string> Failures { get; }

    public VerifiedExecutable? VerifiedExecutable { get; }

    internal static ExecutableVerificationResult Passed(VerifiedExecutable executable) =>
        new(Array.Empty<string>(), executable);

    internal static ExecutableVerificationResult Failed(string failure) =>
        Failed([failure]);

    internal static ExecutableVerificationResult Failed(IEnumerable<string> failures) =>
        new(new ReadOnlyCollection<string>(failures.ToArray()), null);
}

public sealed class VerifiedExecutable
{
    internal VerifiedExecutable(IEnumerable<HookContract> hooks)
    {
        Hooks = new ReadOnlyCollection<HookContract>(hooks.ToArray());
    }

    public IReadOnlyList<HookContract> Hooks { get; }

    public IReadOnlyDictionary<HookId, nuint> ResolveInProcessAddresses(nuint moduleBase)
    {
        if (moduleBase == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleBase), "The loaded module base cannot be zero.");
        }

        var addresses = new Dictionary<HookId, nuint>(Hooks.Count);
        foreach (var hook in Hooks)
        {
            addresses.Add(hook.Id, checked(moduleBase + hook.Rva));
        }

        return new ReadOnlyDictionary<HookId, nuint>(addresses);
    }
}
