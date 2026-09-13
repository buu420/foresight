# Countable footsteps, 0.3.10

The player confirmed that sequential manual navigation worked, but footsteps
did not give a reliable way to count the instructed distance. The 0.3.9 tracker
used 384 fine units per beat (24 rendered pixels), while speech uses 256 units
locally and 128 on the world map. A six-step local replay produced four beats
when walking and three when running at a 16 ms input interval.

The 2026-09-13 19.54.28 Reloaded log also recorded rate-limited movement and two
rejected audio requests by 14:56:23. The old 250 ms limiter and subsequent modulo
could discard whole strides. A size-one DropOldest channel and PlaySound's
single active sound could drop further beats.

## Distance and direction boundaries

Navigation and footsteps now share their map-specific unit constants. The
counter retains every measured whole unit and the remaining fraction. Pausing
without moving preserves that fraction. Scripted displacement, unavailable
capture, jumps, identity changes, and clock discontinuities still discard it.
Held input against a wall does not produce sound.

World walking may finish its native eight-pixel step after key release. The
existing Ghidra decompilation at RVA 264C40 updates D+2E283/285 after walking and
then divides those coordinates by eight for the map. The counter admits at most
one additional world step for 250 ms after the preceding accepted direction;
the native capture still requires player control and rejects scripted movement.

A typed manual-leg endpoint and instruction revision align counting with I, K,
turns, returning to the route, and route corrections. Each new instruction
starts a fresh count from the sampled position. Navigation can accept turns
within 1/16 of a step and arrivals within 1/8. At a completed endpoint, the
counter includes the last full beat promised by the old instruction before
starting the next count. A fractional instruction does not create an extra
whole beat: a 2.5-step leg has two full beats and a partial distance before the
spoken turn or arrival. Navigation pathfinding and movement writes are unchanged.

## Audio delivery

The five existing 240 ms recordings are unchanged. Eight dedicated waveOut
voices allow their tails to overlap. Buffers are pinned and prepared on the
background worker; device calls never run in the input hook. Stops affect only
these voice handles. The queue retains up to 32 requests without silently
evicting an older beat. Beats captured together get distinct attacks at least
45 ms apart. Each request's planned time includes that deliberate spacing, so
the worker does not mark its own pacing as stale. Cancelled requests are dropped.
If playback falls over 150 ms behind its planned time or the queue fills,
pending counting is cancelled and the player is told to stop and press K for
a fresh count. Playback can resume; temporary pressure does not disable it.

A transient missing capture resets motion without repeatedly cutting off the
last real footstep. Menus, dialogue, focus changes, area changes, and F8 still
cancel the mod's audio. Device or queue failure is announced without stopping
navigation. No new external dependency is required.

The API behavior was checked against Microsoft's documentation for
[PlaySound and SND_NOSTOP](https://learn.microsoft.com/en-us/previous-versions/dd743680(v=vs.85)),
[waveOutOpen](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-waveoutopen), and
[waveOutWrite buffer ownership](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-waveoutwrite).
Buffers are unprepared before being freed; a driver cleanup failure retains
memory that could still be in use rather than freeing it prematurely.

## Verification

Regression tests cover walking and running at both distance scales, partial
distance through a stop, release-step completion, duplicate native samples,
stationary walls, transitions, F8, and manual-guidance alignment. Tests reproduce
five whole beats followed by one whole beat across early native turn/arrival
tolerances, and verify that fractional legs do not gain a whole extra beat.

A separate 32-bit audio-device check played 20 requests at 80 ms and 45 ms
spacing, including stop/restart, with all 20 accepted, none rejected, and clean
buffer/device teardown. This validates the device path, not the user's ability
to distinguish fast beats in-game. The revised in-game counting still needs
player testing. Worker tests additionally cover all five beats in a batch,
temporarily busy voices, and recovery from queue pressure. Evidence is under
`artifacts/research/footsteps-0310`.

The final Release suite passed 1,091 tests: 128 Core, 532 Native, 423 Mod, and
8 Prism, with no failures or skipped tests. One earlier run hit the existing
background-start test's thread-pool identity assertion; that isolated test and
the subsequent full run passed. No startup scheduling code was changed.
