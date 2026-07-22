using System.Collections.ObjectModel;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace ChronoTriggerAccessibility.Native.Build;

public sealed class PeImage : IDisposable
{
    private readonly byte[] bytes;
    private readonly MemoryStream stream;
    private readonly PEReader reader;

    private PeImage(byte[] bytes, string sha256)
    {
        this.bytes = bytes;
        Sha256 = sha256;
        stream = new MemoryStream(bytes, writable: false);
        reader = new PEReader(stream, PEStreamOptions.LeaveOpen);

        var peHeader = reader.PEHeaders.PEHeader
            ?? throw new BadImageFormatException("The image does not contain a PE optional header.");
        Machine = reader.PEHeaders.CoffHeader.Machine;
        ImageBase = peHeader.ImageBase;
        Sections = new ReadOnlyCollection<PeSection>(reader.PEHeaders.SectionHeaders
            .Select(section => new PeSection(
                section.Name,
                checked((uint)section.VirtualAddress),
                checked((uint)section.VirtualSize),
                checked((uint)section.SizeOfRawData),
                checked((uint)section.PointerToRawData),
                section.SectionCharacteristics))
            .ToArray());
    }

    public string Sha256 { get; }

    public Machine Machine { get; }

    public ulong ImageBase { get; }

    public IReadOnlyList<PeSection> Sections { get; }

    public static PeImage OpenRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > int.MaxValue)
        {
            throw new BadImageFormatException("The PE image is too large to verify safely.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(file));
        file.Position = 0;
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        return new PeImage(bytes, hash);
    }

    public static PeImage FromBytes(ReadOnlySpan<byte> imageBytes)
    {
        var ownedBytes = imageBytes.ToArray();
        return new PeImage(ownedBytes, Convert.ToHexString(SHA256.HashData(ownedBytes)));
    }

    public PeSection GetSectionContaining(uint rva, int byteCount)
    {
        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        foreach (var section in Sections)
        {
            if (section.ContainsVirtualRange(rva, byteCount))
            {
                return section;
            }
        }

        throw new BadImageFormatException($"RVA 0x{rva:X8} with length {byteCount} is not contained in a PE section.");
    }

    public int RvaToFileOffset(uint rva, int byteCount)
    {
        var section = GetSectionContaining(rva, byteCount);
        var delta = (ulong)rva - section.VirtualAddress;
        if (delta + (ulong)byteCount > section.SizeOfRawData)
        {
            throw new BadImageFormatException(
                $"RVA 0x{rva:X8} maps beyond the raw data for section '{section.Name}'.");
        }

        var fileOffset = (ulong)section.PointerToRawData + delta;
        if (fileOffset + (ulong)byteCount > (ulong)bytes.Length || fileOffset > int.MaxValue)
        {
            throw new BadImageFormatException($"RVA 0x{rva:X8} maps beyond the PE file.");
        }

        return checked((int)fileOffset);
    }

    public byte[] ReadBytesAtRva(uint rva, int byteCount)
    {
        var fileOffset = RvaToFileOffset(rva, byteCount);
        return bytes.AsSpan(fileOffset, byteCount).ToArray();
    }

    public void Dispose()
    {
        reader.Dispose();
        stream.Dispose();
    }
}

public sealed record PeSection(
    string Name,
    uint VirtualAddress,
    uint VirtualSize,
    uint SizeOfRawData,
    uint PointerToRawData,
    SectionCharacteristics Characteristics)
{
    public bool IsExecutable => (Characteristics & SectionCharacteristics.MemExecute) != 0;

    internal bool ContainsVirtualRange(uint rva, int byteCount)
    {
        var sectionLength = Math.Max((ulong)VirtualSize, SizeOfRawData);
        var delta = (ulong)rva - VirtualAddress;
        return rva >= VirtualAddress && delta <= sectionLength && delta + (ulong)byteCount <= sectionLength;
    }
}
