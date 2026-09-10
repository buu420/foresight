using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Appearance labels audited against the installed PC c000..c262 sprite
/// assemblies and bitmaps. These describe appearances, without inferring story
/// identities, rewards, or an actor's role from its script.</summary>
public static class FieldVisualLabels
{
    private static readonly HashSet<int> Objects =
    [
        93,94,95,97,98,99,100,101,102,104,105,106,107,108,110,111,112,113,114,115,
        117,118,119,120,121,122,123,124,125,126,127,128,129,130,131,132,134,135,136,
        137,138,139,140,141,142,143,144,145,146,147,148,149,150,151,153,154,155,156,
        157,158,159,160,161,162,163,166,167,168,169,172,173,174,175,176,177,178,179,
        180,181,185,188,190,192,193,194,200,201,202,203,206,207,208,212,219,221,
        231,232,233,234,235,236,237,238,
    ];

    public static (string Label, NavigationCategory Category) Describe(FieldActorSnapshot actor)
    {
        var type = actor.ClassTag & 0x7F;
        if (type is 5 or 6) return ("Creature", NavigationCategory.People);
        var visual = actor.VisualIndex + (type == 4 ? 7 : 0);
        if (type is < 0 or > 4 || visual is < 0 or > 262) return ("Interactable", NavigationCategory.Objects);
        if (visual is 66 or 182 or 183 or 184 or 197) return ("Cat", NavigationCategory.People);
        if (visual is 63 or 75 or 84 or 103 or 133 or 165 or 171 or 191 or 217) return ("Creature", NavigationCategory.People);
        if (!Objects.Contains(visual)) return ("Person", NavigationCategory.People);
        var label = visual switch
        {
            99 or 166 => "Door", 100 => "Gravestone", 101 or 157 or 161 => "Opening",
            102 or 129 or 130 or 175 or 194 => "Statue", 115 => "Barrel",
            145 or 160 or 168 or 193 => "Rock", 146 => "Sword", 169 => "Paper",
            174 or 176 => "Device", 159 or 203 or 231 or 232 or 233 or 234 => "Balloon",
            94 or 95 or 143 => "Swirling light", 119 or 120 or 134 or 140 or 178 or 201 or 202 or 235 or 236 or 237 or 238 => "Sparkle",
            _ => "Object",
        };
        return (label, NavigationCategory.Objects);
    }
}
