namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Actors whose entire activate handler is a call into another actor's script.
///
/// Script opcode 02 is "call actor function": its first operand is the actor index doubled
/// and its second selects the function. An actor whose activate slot holds nothing but that
/// call and a return does not act on its own at all — confirming it runs the other actor's
/// handler. Truce Market's counter is the case the player hit: scene 118 actor 9 is
/// <c>02 10 11</c>, a call to actor 8 function 1, and actor 8's handler is the shop
/// (<c>E3 00, BB A1 00, C8 81, E3 01, BB A2 00</c>).
///
/// That matters for navigation because the sprite the player recognises is often the one
/// they cannot reach. The Truce clerk stands behind a solid bench, outside
/// <see cref="FieldInteractionRange"/> from every tile of the shop floor, while the counter
/// that runs their script is directly across it. Sending the player to the clerk's own side
/// is impossible; sending them to the counter is what the game intends.
///
/// Only bindings whose target actually opens a menu are listed, because those are the ones
/// where the reachable proxy and the recognisable sprite differ. A scan of every scene found
/// 123 pure activate proxies, of which three call a menu actor:
/// artifacts/research/shop-enemies-0325/navigation/proxies.py.</summary>
public static class FieldActorProxies
{
    private static readonly (int Scene, int Target, int Proxy)[] Bindings =
    [
        (40, 8, 11),     // Melchior's Cabin, 1F
        (118, 8, 9),     // Truce, Market
        (243, 12, 11),   // Keeper's Dome, Epoch Storage Room
    ];

    /// <summary>The actor whose position stands in for this one, if any.</summary>
    public static int? ProxyFor(int scene, int target)
    {
        foreach (var binding in Bindings)
            if (binding.Scene == scene && binding.Target == target) return binding.Proxy;
        return null;
    }

    /// <summary>True when this actor exists only to run another's script, so listing it
    /// separately would offer the same destination twice under a meaningless name.</summary>
    public static bool IsProxy(int scene, int actor)
    {
        foreach (var binding in Bindings)
            if (binding.Scene == scene && binding.Proxy == actor) return true;
        return false;
    }
}
