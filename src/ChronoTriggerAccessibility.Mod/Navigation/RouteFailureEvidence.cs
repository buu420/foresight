using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Diagnostic snapshot of a failed route, built only from the captures a field
/// frame was made from: the live MapTable planes (FieldMapCapture, DB1F0), the actor
/// snapshots, the story words and the target's approaches. Terrain previews and other
/// generated floors are never included. It does not read memory or affect movement.</summary>
public static class RouteFailureEvidence
{
    public const int MaximumEncodedLength = 98304;
    public const int MaximumSnapshots = 8;
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = false };

    public static string? Kind(IEnumerable<string> speech)
    {
        var lines = speech.ToArray();
        return lines.Any(s => s.StartsWith("No route to ", StringComparison.Ordinal)) ? "no-route" :
            lines.Contains("Navigation stopped: movement is blocked.") ? "movement-blocked" :
            lines.Any(s => s.Contains(" is blocking the way. ", StringComparison.Ordinal)) ? "creature-blocked" :
            lines.Contains("The way is temporarily blocked. Guidance continues when it clears.") ? "temporarily-blocked" : null;
    }

    public static Func<string, string?, string?> For(FieldNavigationSnapshot field, FieldMapSnapshot map,
        FieldStoryState? story, IReadOnlyList<NavigationTarget> targets, IReadOnlyList<FieldTreasure>? treasures = null) => (kind, targetId) =>
    {
        var target = targets.FirstOrDefault(t => t.Id == targetId);
        object Evidence(bool withMap) => new
        {
            kind,
            mapSource = withMap ? "live-maptable-planes" : "omitted-over-size-bound",
            field = new
            {
                scene = field.SceneId, sceneCoherent = field.SceneIdCoherent, control = field.ControlFlag,
                inputMode = field.InputMode, leader = field.LeadPlayerActorIndex, collisionRadius = field.ActorCollisionRadius,
                lastPartySlotRaw = field.LastPartySlotRaw, contactActorRaw = field.ContactActorRaw,
                confirmActorRaw = field.ConfirmActorRaw,
            },
            story = story is null ? null : new
            {
                point = story.Point, globals = story.Globals, locals = story.Locals, extended = story.Extended,
            },
            target = target is null ? null : new
            {
                id = target.Id, label = target.Label, category = target.Category.ToString(),
                position = target.Position, approaches = target.ApproachPoints, visible = target.Visible,
                discovered = target.Discovered, guideAvailable = target.GuideAvailable,
                restriction = target.GuidanceRestriction, transition = target.Transition?.Id,
                contact = target.ContactPosition, confirm = target.ConfirmFacings is not null || target.ApproachConfirms is not null,
            },
            actors = field.Actors,
            treasures = treasures?.Select(t => new { index = t.Index, x = t.FineX, y = t.FineY, chest = t.IsChest }),
            map = withMap ? map : null,
        };
        var encoded = Encode(Evidence(true));
        return encoded.Length <= MaximumEncodedLength ? encoded : Encode(Evidence(false));
    };

    public static string Encode(object evidence)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            JsonSerializer.Serialize(gzip, evidence, Options);
        return Convert.ToBase64String(output.ToArray());
    }

    public static string Decode(string encoded)
    {
        using var input = new GZipStream(new MemoryStream(Convert.FromBase64String(encoded)), CompressionMode.Decompress);
        using var reader = new StreamReader(input, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
