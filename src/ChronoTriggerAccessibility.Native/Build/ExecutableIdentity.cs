using System.Reflection.PortableExecutable;

namespace ChronoTriggerAccessibility.Native.Build;

public sealed record ExecutableIdentity
{
    public ExecutableIdentity(string sha256, Machine machine, ulong imageBase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.", nameof(sha256));
        }

        Sha256 = sha256.ToUpperInvariant();
        Machine = machine;
        ImageBase = imageBase;
    }

    public string Sha256 { get; init; }

    public Machine Machine { get; init; }

    public ulong ImageBase { get; init; }
}
