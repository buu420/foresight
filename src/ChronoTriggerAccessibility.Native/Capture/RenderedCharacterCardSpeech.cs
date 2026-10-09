using System.Globalization;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>Formats native character-card Label groups, preserving their displayed values.</summary>
public static class RenderedCharacterCardSpeech
{
    public static string Format(IReadOnlyList<string> lines, bool renderedName = true)
    {
        if (!lines.Contains(":")) return string.Join(", ", lines);
        var clauses = new List<string>();
        var words = new List<string>();
        foreach (var line in lines.Append(":"))
        {
            if (line != ":") { words.Add(line); continue; }
            if (words.Count > 0)
            {
                var clause = string.Join(" ", words).Replace("/ ", "/", StringComparison.Ordinal);
                // The first group is a saved character name on current-party cards.
                // Reserve cards omit it. Never interpret a renamed character as a stat.
                if ((!renderedName || clauses.Count > 0) && IsKnockedOut(words[0], words.Skip(1)))
                    clause = "Knocked out";
                clauses.Add(clause);
            }
            words.Clear();
        }
        return string.Join(". ", clauses);
    }

    public static bool IsKnockedOut(string label, IEnumerable<string> tokens)
    {
        if (label != "HP") return false;
        var values = string.Concat(tokens).Replace(" ", "", StringComparison.Ordinal).Split('/');
        return values.Length is 1 or 2 &&
            int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hp) && hp == 0 &&
            (values.Length == 1 || int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out var maximum) && maximum > 0);
    }
}
