using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ChronoTriggerAccessibility.Native.Tests.Support;

/// <summary>Reads field scripts from the installed resources.bin (ARC1, format checked against
/// GitExl/CTViewer; same reader as tools/research/future_story/assets.py). Nothing is written.
/// The repository lives inside the game folder, so the supported install is the fixture.</summary>
internal static class InstalledFieldScripts
{
    public const string SupportedResourceSha256 = "09914BF4A8944C0708C947E7EB5683AEE4ED48BC6D5D1D75FA07B68131653C7E";
    private static readonly Lazy<string> Resource = new(FindResource);
    private static readonly Lazy<Dictionary<string, (uint Start, uint Size)>> Directory = new(ReadDirectory);
    private static readonly Dictionary<int, byte[]> Cache = [];

    public static byte[] Atel(int script)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(script, out var cached)) return cached;
            var (start, size) = Directory.Value[$"Game/field/atel/Atel_{script:0000}.dat"];
            return Cache[script] = Block(start, size);
        }
    }

    private static string FindResource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "ChronoTriggerAccessibility.slnx"))) continue;
            var path = Path.Combine(directory.Parent!.FullName, "resources.bin");
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            if (hash != SupportedResourceSha256)
                throw new InvalidOperationException($"Installed resources.bin {hash} is not the supported {SupportedResourceSha256}.");
            return path;
        }
        throw new InvalidOperationException("The installed game's resources.bin was not found above the repository.");
    }

    private static Dictionary<string, (uint, uint)> ReadDirectory()
    {
        var header = Decode(ReadRaw(0, 16), 0);
        if (Encoding.ASCII.GetString(header, 0, 4) != "ARC1") throw new InvalidDataException("resources.bin is not ARC1.");
        var data = Block(BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)), BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)));
        var entries = new Dictionary<string, (uint, uint)>();
        var count = BinaryPrimitives.ReadInt32LittleEndian(data);
        for (var index = 0; index < count; index++)
        {
            var entry = data.AsSpan(4 + index * 12);
            var name = BinaryPrimitives.ReadInt32LittleEndian(entry);
            var end = Array.IndexOf(data, (byte)0, name);
            entries[Encoding.UTF8.GetString(data, name, end - name)] =
                (BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]), BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]));
        }
        return entries;
    }

    private static byte[] Block(uint offset, uint length)
    {
        var data = Decode(ReadRaw(offset, length), offset);
        using var input = new GZipStream(new MemoryStream(data, 4, data.Length - 4), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        if (output.Length != BinaryPrimitives.ReadUInt32BigEndian(data)) throw new InvalidDataException("ARC1 block length mismatch.");
        return output.ToArray();
    }

    private static byte[] ReadRaw(uint offset, uint length)
    {
        using var stream = File.OpenRead(Resource.Value);
        stream.Seek(offset, SeekOrigin.Begin);
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static byte[] Decode(byte[] data, uint blockSeed)
    {
        var seed = 0x19000000u + blockSeed;
        for (var index = 0; index < data.Length; index++)
        {
            seed = seed * 0x41C64E6Du + 12345u;
            data[index] ^= (byte)(seed >> 24);
        }
        return data;
    }
}
