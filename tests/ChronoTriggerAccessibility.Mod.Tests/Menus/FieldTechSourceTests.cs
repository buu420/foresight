using System.Globalization;
using System.Text.Json;
using ChronoTriggerAccessibility.Mod.Menus;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Menus;

public sealed class FieldTechSourceTests
{
    [Theory]
    [InlineData("character", 0x24D94E48u, "Lucca LV 4. HP 56/101. MP 10/18. Dual Techs. Select a tech")]
    // This older sparse replay did not capture Node::getName at +178.
    // It cannot prove the background row identities required by the new reader.
    [InlineData("slurp", 0x24D96E40u, null)]
    public void ReadsAndRefreshesOnlyTechSelectionsWithCompleteNativeIdentity(string name, uint manager, string? expected)
    {
        using var stream = typeof(FieldTechSourceTests).Assembly.GetManifestResourceStream("TechFrames")!;
        using var document = JsonDocument.Parse(stream);
        var frame = document.RootElement.GetProperty("frames").GetProperty(name);
        var source = new FieldSubmenuSource(new Replay(frame.GetProperty("segments")));
        var node = frame.GetProperty("node").GetUInt32();
        source.BindImageBase(frame.GetProperty("imageBase").GetUInt32());
        var result = source.Capture(node);
        if (expected is null) Assert.Null(result);
        else
        {
            Assert.NotNull(result);
            Assert.Equal("Techs", result.Title);
            Assert.Equal(expected, result.Text);
        }
        Assert.True(source.OwnsManager(node, manager));
        Assert.False(source.OwnsManager(node, 0x123456));
    }

    private sealed class Replay : IReadableMemory
    {
        private readonly Dictionary<nuint, byte> bytes = [];
        public Replay(JsonElement segments)
        {
            foreach (var property in segments.EnumerateObject())
            {
                var at = uint.Parse(property.Name.Split(':')[0], NumberStyles.HexNumber);
                var data = Convert.FromHexString(property.Value.GetString()!);
                for (var i = 0; i < data.Length; i++) bytes[at + (nuint)i] = data[i];
            }
        }
        public bool TryRead(nuint address, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
                if (!bytes.TryGetValue(address + (nuint)i, out destination[i])) return false;
            return true;
        }
    }
}
