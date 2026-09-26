namespace ChronoTriggerAccessibility.Mod.Menus;

/// <summary>
/// Text the game draws as one label per line after splitting it with 0x40FC80: the ending
/// windows (0x2B06B0) and the save/load notice window (0x21AA40) both create one font-0x0C
/// label for every segment this split returns, including empty ones.
/// </summary>
public static class NativeTextLines
{
    /// <summary>The byte 0x40FC80 breaks lines on.</summary>
    public const char Separator = '\\';

    /// <summary>Splits exactly as 0x40FC80 does: on every 0x5C, keeping leading and interior
    /// empty segments but not the run of empty segments at the end, which it counts first and
    /// never allocates. Text without a separator is copied as one segment, even when empty.</summary>
    public static IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.IndexOf(Separator, StringComparison.Ordinal) < 0)
        {
            return [text];
        }

        var segments = text.Split(Separator);
        var count = segments.Length;
        while (count > 0 && segments[count - 1].Length == 0)
        {
            count--;
        }

        return segments[..count];
    }

    /// <summary>The lines a sighted player can read: drawn lines that are not blank. Blank ones
    /// are spacing, such as the empty line the English first-clear message starts with.</summary>
    public static IReadOnlyList<string> Visible(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Select(line => line.Trim()).Where(line => line.Length != 0).ToArray();
    }
}
