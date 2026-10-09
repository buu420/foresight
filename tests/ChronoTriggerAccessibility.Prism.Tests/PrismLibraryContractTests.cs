using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace ChronoTriggerAccessibility.Prism.Tests;

public sealed class PrismLibraryContractTests
{
    private const string PrismSha256 = "6A84322E42D1B4123E2E66E9887CFDF0CDEA2A972FA40FC7B7185AEC77F5178A";

    [Fact]
    public void StagedPrismDllIsTheReviewedWin32Artifact()
    {
        var path = FindStagedPrismDll();

        Assert.True(File.Exists(path), $"Expected reviewed Prism DLL at '{path}'.");
        Assert.Equal(PrismSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    [Fact]
    public void StagedPrismDllIsPe32AndExportsTheRequiredCAbi()
    {
        var path = FindStagedPrismDll();
        var exports = ReadPeExports(path, out var machine);

        Assert.Equal(0x014C, machine);
        Assert.Subset(
            exports,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "prism_init",
                "prism_shutdown",
                "prism_registry_create_best",
                "prism_backend_output",
                "prism_backend_stop",
                "prism_backend_free",
            });
    }

    [Fact]
    public void ResolverAcceptsOnlyTheExactAssemblySiblingHash()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var copiedLibrary = Path.Combine(directory.FullName, "prism.dll");
            File.Copy(FindStagedPrismDll(), copiedLibrary);
            Assert.Equal(copiedLibrary, PrismLibraryResolver.GetValidatedAssemblySiblingPath(directory.FullName));

            using (var stream = File.Open(copiedLibrary, FileMode.Open, FileAccess.ReadWrite))
            {
                stream.Position = stream.Length - 1;
                var original = stream.ReadByte();
                stream.Position = stream.Length - 1;
                stream.WriteByte((byte)(original ^ 0xFF));
            }

            Assert.Throws<FileLoadException>(() => PrismLibraryResolver.GetValidatedAssemblySiblingPath(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static HashSet<string> ReadPeExports(string path, out ushort machine)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.ASCII, leaveOpen: false);
        reader.BaseStream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        reader.BaseStream.Position = peOffset;
        Assert.Equal(0x00004550u, reader.ReadUInt32());
        machine = reader.ReadUInt16();
        reader.BaseStream.Position = peOffset + 20;
        var optionalHeaderSize = reader.ReadUInt16();
        var optionalHeaderOffset = peOffset + 24;
        reader.BaseStream.Position = optionalHeaderOffset;
        Assert.Equal(0x10Bu, reader.ReadUInt16());
        reader.BaseStream.Position = optionalHeaderOffset + 96;
        var exportRva = reader.ReadUInt32();
        reader.ReadUInt32();

        var sections = new List<(uint VirtualAddress, uint VirtualSize, uint RawSize, uint RawOffset)>();
        reader.BaseStream.Position = optionalHeaderOffset + optionalHeaderSize;
        var sectionCountOffset = peOffset + 6;
        reader.BaseStream.Position = sectionCountOffset;
        var sectionCount = reader.ReadUInt16();
        reader.BaseStream.Position = optionalHeaderOffset + optionalHeaderSize;
        for (var index = 0; index < sectionCount; index++)
        {
            reader.BaseStream.Position += 8;
            var virtualSize = reader.ReadUInt32();
            var virtualAddress = reader.ReadUInt32();
            var rawSize = reader.ReadUInt32();
            var rawOffset = reader.ReadUInt32();
            reader.BaseStream.Position += 16;
            sections.Add((virtualAddress, virtualSize, rawSize, rawOffset));
        }

        long RvaToOffset(uint rva)
        {
            var section = sections.Single(section => rva >= section.VirtualAddress && rva < section.VirtualAddress + Math.Max(section.VirtualSize, section.RawSize));
            return section.RawOffset + rva - section.VirtualAddress;
        }

        reader.BaseStream.Position = RvaToOffset(exportRva) + 24;
        var nameCount = reader.ReadUInt32();
        reader.BaseStream.Position = RvaToOffset(exportRva) + 32;
        var namesRva = reader.ReadUInt32();
        var exports = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < nameCount; index++)
        {
            reader.BaseStream.Position = RvaToOffset(namesRva + ((uint)index * sizeof(uint)));
            reader.BaseStream.Position = RvaToOffset(reader.ReadUInt32());
            var bytes = new List<byte>();
            byte value;
            while ((value = reader.ReadByte()) != 0)
            {
                bytes.Add(value);
            }

            exports.Add(Encoding.ASCII.GetString(bytes.ToArray()));
        }

        return exports;
    }

    private static string FindStagedPrismDll()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "native", "prism", "v0.17.3", "win-x86", "prism.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine("native", "prism", "v0.17.3", "win-x86", "prism.dll");
    }
}
