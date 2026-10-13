using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Conservative physical floor components for guide eligibility only.
/// Every possible half of a split tile can join every compatible neighbour, in
/// either direction. Body collision, actors, exits and forced floor movement are
/// deliberately omitted: disconnection is proof, connection is not a route.
/// Walking always uses the full native graph. Staged passages are checked there.</summary>
public sealed class FieldRegionIndex
{
    private int width, height;
    private byte[] shapes = [], layers = [];
    private int[] parents = [];

    public void Update(FieldMapSnapshot map)
    {
        if (width == map.Width && height == map.Height && shapes.AsSpan().SequenceEqual(map.CollisionShapes) &&
            layers.AsSpan().SequenceEqual(map.CollisionLayers)) return;
        width = map.Width; height = map.Height;
        shapes = map.CollisionShapes.ToArray(); layers = map.CollisionLayers.ToArray();
        parents = Enumerable.Repeat(-1, checked(width * height * 3)).ToArray();
        for (var cell = 0; cell < width * height; cell++)
        foreach (var region in Regions(cell))
        for (var input = 1; input <= 3; input++)
            if (FieldCollisionRules.TryEnter(input, region, out var output))
                parents[cell * 3 + output - 1] = cell * 3 + output - 1;
        for (var cell = 0; cell < width * height; cell++)
        {
            Join(cell, cell);
            if (cell % width + 1 < width) { Join(cell, cell + 1); Join(cell + 1, cell); }
            if (cell / width + 1 < height) { Join(cell, cell + width); Join(cell + width, cell); }
        }
    }

    /// <summary>Null means the captured pose has no floor-component proof.
    /// False is a definite separation even in this permissive approximation.</summary>
    public bool? Connected(NavigationPoint from, NavigationPoint to)
    {
        var a = Node(from); var b = Node(to);
        return a < 0 || b < 0 ? null : Root(a) == Root(b);
    }

    private int Node(NavigationPoint p)
    {
        if (p.X < 0 || p.Y < 0 || p.X / 256 >= width || p.Y / 256 >= height || p.Layer is < 1 or > 3) return -1;
        var node = (p.Y / 256 * width + p.X / 256) * 3 + p.Layer - 1;
        return node < parents.Length && parents[node] >= 0 ? node : -1;
    }

    private IEnumerable<FieldCollisionRegion> Regions(int cell)
    {
        // 178C50 chooses just the alternate half for shape 1, just the primary
        // for 0/30+, and one of the two for 2..29. Ignoring its corner boundary
        // over-connects split tiles; it can never invent a disconnection.
        var shape = shapes[cell] >> 2; var layer = layers[cell];
        if (shape != 1) yield return new(layer & 3, layer & 4);
        if (shape is >= 1 and <= 29) yield return new(layer >> 3 & 3, layer & 32);
    }

    private void Join(int from, int to)
    {
        for (var input = 1; input <= 3; input++)
        {
            var a = from * 3 + input - 1;
            if (parents[a] < 0) continue;
            foreach (var region in Regions(to))
                if (FieldCollisionRules.TryEnter(input, region, out var output))
                {
                    var b = to * 3 + output - 1;
                    if (parents[b] >= 0) parents[Root(b)] = Root(a);
                }
        }
    }

    private int Root(int node)
    {
        var root = node;
        while (parents[root] != root) root = parents[root];
        while (node != root) { var next = parents[node]; parents[node] = root; node = next; }
        return root;
    }
}
