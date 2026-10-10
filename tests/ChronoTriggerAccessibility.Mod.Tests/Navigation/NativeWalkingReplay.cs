using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Independent single-frame oracle for held walking input, transcribed
/// from Ghidra 175E90/1761C0/176810/176AE0 and checked by native_slide.py.
/// It does not use FieldNavigationGraph, its edge endpoints or its slide simulation.
/// These replays cover plain floor; actor contacts are tested separately.</summary>
internal static class NativeWalkingReplay
{
    public static NavigationPoint Move(FieldMapSnapshot map, NavigationPoint from, NavigationDirection command, int speed = 16)
    {
        Assert.True(speed is 16 or 32);
        var (dx, dy) = command switch
        {
            NavigationDirection.East => (1, 0), NavigationDirection.West => (-1, 0),
            NavigationDirection.North => (0, -1), NavigationDirection.South => (0, 1), _ => (0, 0),
        };
        if ((dx, dy) == (0, 0)) return from;
        bool Clear(int x, int y) => Enter(x, y, out _);
        bool Enter(int x, int y, out int layer)
        {
            layer = from.Layer;
            if (x < 0 || y < 0 || x / 256 >= map.Width || y / 256 >= map.Height) return false;
            var index = y / 256 * map.Width + x / 256;
            return FieldCollisionRules.TryEnter(from.Layer,
                FieldCollisionRules.Region(map.CollisionShapes[index], map.CollisionLayers[index], x, y), out layer);
        }
        // These captured routes avoid moving strips; do not pretend
        // this terrain-only oracle reproduces a native floor push.
        Assert.Equal(0, map.TerrainFlags[from.Y / 256 * map.Width + from.X / 256] & 12);
        var x = from.X; var y = from.Y;
        int mx, my;
        if (dx != 0)
        {
            var ahead = x + dx * (112 + speed); var beside = x + dx * 112;
            var foot = Clear(ahead, y); var head = Clear(ahead, y - 112);
            if (foot && head) { mx = dx * speed; my = 0; }
            else if (foot)
            {
                if (!Clear(ahead, y + 16)) return from;
                mx = Clear(ahead, y - 96) ? dx * speed : 0; my = 16;
                if (mx == 0 && !Clear(beside, y + 16)) return from;
            }
            else if (head)
            {
                my = -16;
                if (Clear(ahead, y - 16))
                {
                    if (!Clear(ahead, y - 128)) return from;
                    mx = dx * speed;
                }
                else
                {
                    if (!Clear(beside, y - 128)) return from;
                    mx = 0;
                }
            }
            else return from;
        }
        else
        {
            var row = dy > 0 ? y + speed : y - 112 - speed;
            var still = dy > 0 ? y : y - 112;
            var left = Clear(x - 112, row); var right = Clear(x + 112, row);
            if (left && right) { mx = 0; my = dy * speed; }
            else if (!left && right)
            {
                mx = 16;
                if (Clear(x - 96, row))
                {
                    if (!Clear(x + 128, row)) return from;
                    my = dy * speed;
                }
                else
                {
                    if (!Clear(x + 128, still)) return from;
                    my = 0;
                }
            }
            else if (left)
            {
                mx = -16;
                // The down mover carries into the second ADC: X + 0x61.
                if (Clear(x + (dy > 0 ? 97 : 96), row))
                {
                    if (!Clear(x - 128, row)) return from;
                    my = dy * speed;
                }
                else
                {
                    if (!Clear(x - 128, still)) return from;
                    my = 0;
                }
            }
            else return from;
        }
        return Enter(x + mx, y + my, out var nextLayer) ? new(x + mx, y + my, nextLayer) : from;
    }
}
