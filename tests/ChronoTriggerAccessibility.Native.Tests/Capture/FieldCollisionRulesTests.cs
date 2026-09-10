using System.Text.Json;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class FieldCollisionRulesTests
{
    [Fact]
    public void Matches2560ResultsFromTheExactNativeCollisionRoutine()
    {
        using var stream = typeof(FieldCollisionRulesTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Native.Tests.Capture.navigation-collision-native-vectors.json")!;
        var vectors = JsonSerializer.Deserialize<int[][]>(stream)!;
        Assert.Equal(2560, vectors.Length);
        foreach (var v in vectors)
        {
            var result = FieldCollisionRules.Region((byte)(v[0] * 4), (byte)v[1], v[2], v[3]);
            Assert.Equal(v[4], result.Layer);
            Assert.Equal(v[5], result.NeutralMask);
        }
    }

    [Theory]
    [InlineData(1, 0, 0, false, 1)]
    [InlineData(1, 2, 0, false, 1)]
    [InlineData(1, 3, 0, true, 3)]
    [InlineData(3, 2, 0, true, 2)]
    [InlineData(1, 0, 4, true, 1)]
    [InlineData(2, 0, 32, true, 2)]
    [InlineData(3, 1, 4, false, 3)]
    public void NativeLayerAcceptancePreservesNeutralAndUsesTransitionLayer(
        int current, int layer, int neutral, bool accepted, int next)
    {
        Assert.Equal(accepted, FieldCollisionRules.TryEnter(current, new(layer, neutral), out var actual));
        Assert.Equal(next, actual);
    }
}
