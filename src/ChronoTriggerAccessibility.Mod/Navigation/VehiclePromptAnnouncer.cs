using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Speaks the game's own vehicle prompts. The world layer draws an icon above
/// the party while A+109B5 (standing on a vehicle) or A+109B6 (hovering where landing
/// is legal) is set; this announces each rising edge once. The mod never presses
/// Confirm, so boarding and landing stay the player's decision.</summary>
public sealed class VehiclePromptAnnouncer(Action<string> speak)
{
    private nint context;
    private bool boarding, landing, omen;

    public void Reset() { context = 0; boarding = landing = omen = false; }

    /// <param name="playerX">Walking pixel position used to name the vehicle under the party.</param>
    public void Observe(nint currentContext, WorldVehicleState? state, int playerX = -1, int playerY = -1)
    {
        if (state is null) { Reset(); return; }
        if (context != currentContext) { context = currentContext; boarding = landing = omen = false; }
        var walking = state.Transport == 0 && state.MasterAction == 1;
        if (state.BoardingPrompt && walking && !boarding)
            speak($"On the {VehicleUnder(state, playerX, playerY)}. Press Confirm to board.");
        boarding = state.BoardingPrompt && walking;
        var flying = state.Transport is 2 or 3 && state.MasterAction == 1;
        var atOmen = flying && state.Transport == 2 && state.BlackOmen?.ContactPrompt == true;
        if (atOmen && !omen) speak("Black Omen. Press Confirm to open the boarding choice.");
        omen = atOmen;
        if (state.LandingPrompt && flying && !atOmen && !landing) speak("Landing possible. Press Confirm to land.");
        landing = state.LandingPrompt && flying && !atOmen;
    }

    private static string VehicleUnder(WorldVehicleState state, int x, int y)
    {
        if (!state.DactylsPresent) return VehicleStoryRouting.EpochLabel;
        if (!state.EpochPresent) return VehicleStoryRouting.DactylLabel;
        if (x < 0 || y < 0) return VehicleStoryRouting.EpochLabel;
        var epoch = Math.Abs(state.EpochX - x) + Math.Abs(state.EpochY - y);
        var dactyl = Math.Abs(state.DactylX - x) + Math.Abs(state.DactylY - y);
        return dactyl < epoch ? VehicleStoryRouting.DactylLabel : VehicleStoryRouting.EpochLabel;
    }
}
