using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>What the Ocean Palace field shows near the camera's lower edge. Evidence:
/// artifacts/research/bug-reports-20261020/claude-ocean/findings.md.</summary>
public static class OceanPalaceSight
{
    /// <summary>Mune (Atel0220 actor9, load_enemy 98 = c415) and Masa (Atel0397 actor33,
    /// load_enemy 97 = c414) share one frame layout, every frame opaque at least 24 pixels above the
    /// foot point that 17AD30 places on screen. The field shows 192 rows (173B40 keeps a
    /// 25-row window; the camera follows the leader at row 120), so a twin standing up to
    /// 16 pixels below <see cref="FieldEnvironmentCapture.TryViewport"/>'s window still has
    /// its head on screen. Above the top edge the body is higher still and stays hidden.</summary>
    public static bool TwinHeadShown(int scene, FieldActorSnapshot actor, FieldViewport viewport) =>
        (scene, actor.Index, actor.ClassTag & 0x7F, actor.VisualIndex) is (404, 9, 5 or 6, 0x98) or (409, 33, 5 or 6, 0x97) &&
        actor.FineX >= viewport.Left && actor.FineX < viewport.Right &&
        actor.FineY >= viewport.Top && actor.FineY < viewport.Bottom + 16 * 16;
}
