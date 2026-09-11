# World navigation and transition recovery

The user authorized the existing U/O, J/L, K, I, P navigation design and F8
footsteps on the world map, including Resume and return to a local area.
Implementation and testing continue in this task; the user controls the game.

1. Preserve input edges across slow captures. Add a regression where idle
   observation consumes 600 ms, then K is pressed. It must still announce its
   selection. Measure pauses from completion of our callback, not its start.
   Add a regression where motion capture is unavailable and F8 still toggles.
2. Capture the current world walking task, rather than reusing a field pointer.
   Audited tick: RVA 264B56, direct CALL 264C40, bytes E8 E5 00 00 00.
   Direction consumer: RVA 26536F, OR EDI,[ESI+331C], bytes 0B BE 1C 33 00 00.
   Read held input through the same native getter used by the original code.
   Preserve all registers/flags; inject only directions into the current pad.
   Add a separate instruction-site contract for the one audited OR instruction;
   retain strict direct-CALL checks for existing hooks.
3. Use native positions and live collision/entrance data. World task guards are
   D+20980 bit 7 clear, D+2E27C=1, D+2E27E=0, D+2E280=0. Read the player
   globals D+2E283/285, which native walking updates and entrance checks consume.
   Live capture established D+2E04E is transient script-actor scratch outside its
   immediate call and must not identify the player. Read live layer-two map
   at D+23800 (96x64 bytes), properties at D+25000 (512 bytes), and eight-byte
   entrance records at D+25E00 with count D+2FB38. Recheck identity/control
   after reads. Finish the native world-id/camera/activation audit before using
   those fields. Never infer active mode from persistent world bytes alone.
4. Build a cardinal world graph with eight-pixel steps. Native 267E40/2775D0
   sample the two chips at (x/8-1,y/8-1) and (x/8,y/8-1); either property &3
   blocks walking. Test both nibble halves, the two-chip footprint, obstacle
   detours, intermediate player positions, and blocked goals against decoded
   shipped-map fixtures. Native coordinates, not screen resolution, drive paths.
5. Discover enabled entrances only when the native viewport contains their
   visible footprint. Keep stable destination selection, retire disabled records,
   resolve names from the game's local text, and preserve meaningful separate
   entrances. Bind current early-story objectives to eligible entrances. Route
   arrival leaves confirmation/interaction to the player.
6. Share navigation selection controls and the F8 setting across both sources.
   Source changes cancel old movement, rearm key edges, and reset partial stride.
   Test field -> world -> same field, held keys, manual input, unavailable
   captures, menus, and focus loss. Announce the entered area on each transition.
7. Verify native instruction/ABI contracts and movement replays, then run the
   Release suite and package/deployment checks. Install the next version with
   the game closed, verify installed hashes and unchanged game executable, and
   ask the user to test Resume, walking, F8, and a world/local-area round trip.
   Live sound and transition behavior remain unverified until that test.
