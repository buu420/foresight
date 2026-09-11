using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

internal static class WorldViewportCapture
{
    // Retail libcocos2d.dll getters were audited alongside the EXE. Resolve its
    // ASLR base through the existing import, then verify the relevant getters.
    internal static FieldViewport? Capture(IReadableMemory memory, nuint image, uint renderer)
    {
        uint U(nuint address) => WorldNavigationCapture.Value(memory, address, 4, out var v) ? v : throw new InvalidDataException();
        float F(nuint address) { var v = BitConverter.UInt32BitsToSingle(U(address)); return float.IsFinite(v) ? v : throw new InvalidDataException(); }
        bool B(nuint address) => WorldNavigationCapture.Value(memory, address, 1, out var v) && v <= 1 ? v == 1 : throw new InvalidDataException();
        bool Bytes(nuint address, ReadOnlySpan<byte> expected)
        {
            Span<byte> actual = stackalloc byte[expected.Length];
            return memory.TryRead(address, actual) && actual.SequenceEqual(expected);
        }
        var getInstance = U(image + 0x385BBC);
        if (getInstance < 0x19529E) return null;
        nuint dll = getInstance - 0x19529E;
        if (U(image + 0x385B40) != dll + 0x195375 ||
            !Bytes(dll + 0x195375, [0x55, 0x8B, 0xEC, 0x8B, 0x49, 0x78, 0x85, 0xC9]) ||
            !Bytes(dll + 0x24C138, [0x55, 0x8B, 0xEC, 0x83, 0x79, 0x58, 0x01])) return null;
        nuint director = U(dll + 0x87D47C), view = U(director + 0x78);
        if (director == 0 || view == 0 || U(U(view) + (nuint)0x44) != dll + 0x24C138) return null;
        var policy = U(view + 0x58);
        double width = F(view + 0x20), height = F(view + 0x24), originX = 0, originY = 0;
        if (policy == 1)
        {
            var sx = F(view + 0x50); var sy = F(view + 0x54);
            if (sx <= 0 || sy <= 0) return null;
            var visibleWidth = F(view + 0x18) / sx; var visibleHeight = F(view + 0x1C) / sy;
            originX = (width - visibleWidth) * .5; originY = (height - visibleHeight) * .5;
            width = visibleWidth; height = visibleHeight;
        }
        if (width is < 64 or > 8192 || height is < 64 or > 8192) return null;
        nuint begin = U((nuint)renderer + 0x160), end = U((nuint)renderer + 0x164), node = 0;
        if (begin == 0 || end < begin || end - begin > 2048 || (end - begin) % 4 != 0) return null;
        var strings = new MsvcStringReader(memory);
        for (var address = begin; address < end; address += 4)
        {
            nuint child = U(address);
            if (child == 0 || !strings.TryRead(child + 0x178, 128, out var name, out _) || name != "worldmap") continue;
            if (node != 0) return null;
            node = child;
        }
        if (node == 0 || U(node + 0x16C) != renderer) return null;
        // Compose the live axis-aligned Node transforms, including Scene's
        // ignore-anchor behavior. Reject rotations/skew instead of guessing.
        double scaleX = 1, scaleY = 1, tx = 0, ty = 0;
        var seen = new HashSet<nuint>();
        while (node != 0)
        {
            if (!seen.Add(node) || seen.Count > 12 || !B(node + 0x1AD) ||
                F(node + 0x20) != 0 || F(node + 0x24) != 0 || F(node + 0x5C) != 0 || F(node + 0x60) != 0) return null;
            var sx = F(node + 0x38); var sy = F(node + 0x3C);
            if (sx <= 0 || sy <= 0) return null;
            var ax = F(node + 0x64); var ay = F(node + 0x68);
            var px = F(node + 0x44) + (B(node + 0x1AE) ? ax : 0) - ax * sx;
            var py = F(node + 0x48) + (B(node + 0x1AE) ? ay : 0) - ay * sy;
            tx = tx * sx + px; ty = ty * sy + py;
            scaleX *= sx; scaleY *= sy;
            node = U(node + 0x16C);
        }
        var cameraX = F((nuint)renderer + 0x2606C); var cameraY = F((nuint)renderer + 0x26070);
        // The chunks use Y up, whereas script/world pixels use Y down.
        var left = (originX - tx) / scaleX - cameraX;
        var right = (originX + width - tx) / scaleX - cameraX;
        var top = cameraY - (originY + height - ty) / scaleY;
        var bottom = cameraY - (originY - ty) / scaleY;
        if (right - left is <= 0 or > 1536 || bottom - top is <= 0 or > 1024 ||
            left is < -3072 or > 3072 || top is < -2048 or > 2048) return null;
        return new((int)Math.Ceiling(left * 16), (int)Math.Ceiling(top * 16),
            (int)Math.Floor(right * 16), (int)Math.Floor(bottom * 16));
    }
}
