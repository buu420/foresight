namespace ChronoTriggerAccessibility.Native.Capture;

public readonly record struct FieldCollisionRegion(int Layer, int NeutralMask);

/// <summary>Exact terrain-region selection at RVA 178C50 and layer acceptance at 178FF0.
/// Coordinates are native fixed point: 256 units per 16-pixel map tile.</summary>
public static class FieldCollisionRules
{
    private static readonly int[] Shifts = [130,2,2,130,129,1,1,129,131,3,3,131,129,1,1,129,131,3,3,131,130,2];
    private static readonly int[] Offsets = [61,3,3,61,125,3,-61,61,62,35,3,29,61,-61,3,125,30,3,35,61,61,3];

    public static FieldCollisionRegion Region(byte property0, byte property2, int fineX, int fineY)
    {
        var shape = property0 >> 2;
        var x = fineX & 255;
        var y = fineY & 255;
        var alternate = shape == 1;
        if (shape is >= 2 and < 24)
        {
            var index = shape - 2;
            var value = x >> (Shifts[index] & 127);
            if ((Shifts[index] & 128) != 0) value ^= 255;
            alternate = (((value + Offsets[index] - (y >> 2)) ^ ((index % 4 >= 2) ? 255 : 0)) & 128) != 0;
        }
        else alternate = shape switch
        {
            24 => (x & 128) == 0,
            25 => (y & 128) == 0,
            26 => (x & 128) == 0 && (y & 128) != 0,
            27 => (x & 128) != 0 && (y & 128) != 0,
            28 => (x & 128) != 0 && (y & 128) == 0,
            29 => (x & 128) == 0 && (y & 128) == 0,
            _ => alternate,
        };
        return alternate ? new((property2 >> 3) & 3, property2 & 32) : new(property2 & 3, property2 & 4);
    }

    public static bool TryEnter(int currentLayer, FieldCollisionRegion region, out int nextLayer)
    {
        nextLayer = currentLayer;
        if (currentLayer is < 1 or > 3) return false;
        if (region.NeutralMask != 0) return currentLayer != 3;
        if (region.Layer == 0 || (region.Layer != 3 && region.Layer != currentLayer && currentLayer != 3)) return false;
        nextLayer = region.Layer;
        return true;
    }
}
