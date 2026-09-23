namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>Native actor contact at RVA 178980, before terrain collision. Actor
/// +20 is a camera cache here; script-call and drawing flags are not collision gates.</summary>
public sealed class FieldActorCollisionRules(FieldNavigationSnapshot field)
{
    private readonly FieldActorSnapshot[] candidates = field.Actors
        .Where(a => a.Index > 0 && a.CoordinatesCoherent && a.ActivationBinding != 0 &&
            !a.IsPartyMember && (a.ClassTag & 0x80) == 0)
        .OrderByDescending(a => a.Index).ToArray();

    public bool BlocksProbe(int x, int y, IReadOnlySet<int>? contactDestinations = null)
    {
        foreach (var actor in candidates)
        {
            // The final party-slot comparison leaves carry in the original
            // subtraction: an empty slot (80) subtracts one from the Y gap.
            var dy = (long)actor.FineY - y - (field.LastPartySlotRaw > actor.Index * 2 ? 1 : 0);
            var vertical = actor.ActivationEnabled == 0 ? field.ActorCollisionRadius : 224;
            var dx = (long)actor.FineX - x - (long)actor.CollisionOffsetX * 16 - 1;
            if (Math.Abs(dy) >= vertical || Math.Abs(dx) >= field.ActorCollisionRadius) continue;
            // The first matching slot wins, including an unloaded touch marker.
            // Native touch is dispatched even when this contact blocks movement.
            // Navigation may intentionally bump only its selected touch target;
            // preserve first-contact precedence rather than skipping that slot.
            return (actor.LoadedFlag & 1) != 0 && contactDestinations?.Contains(actor.Index) != true;
        }
        return false;
    }

    public bool BlocksMove(int fromX, int fromY, int x, int y, IReadOnlySet<int>? contactDestinations = null)
    {
        if (x != fromX) return BlocksProbe(x + Math.Sign(x - fromX) * 112, y - 64, contactDestinations);
        var head = field.SceneId == 0x163 && fromX > 0xC8F && fromX < 0xD80 &&
            fromY > 0x14DF && fromY < 0x1590 ? 96 : 112;
        return BlocksProbe(x, y < fromY ? y - head : y, contactDestinations);
    }

    public static int Radius(int scene, int fairFlags = 0) => scene switch
    {
        10 or 0x1A or 0x48 or 0x87 or 0x8C or 0xE7 or 0xEC or 0xED or 0xF2 or
        0x113 or 0x124 or 0x149 or 0x14A or 0x163 or 0x17B or 0x199 or 0x19F or
        0x1D1 or 0x21A or 0x21B or 0x273 or 0x26C or 0x26A => 224,
        5 when (fairFlags & 0x3F) != 0 => 224,
        _ => 160,
    };
}
