# Epoch narration in 0.3.62

Movie 006, the first Epoch travel sequence, now has a separate narration master
in the approved creator voice. It reuses the movie-session injection and
lifecycle handling documented in [the original implementation](movie-narration-0356.md).
Original music, effects, timing, skip and player controls are preserved.

The decoded original is 15,679,299 bytes, lasts 25.0583 seconds and has SHA-256
`95F627AFE3DA97D9924CD96349329F1C0F488ADEF1D2BAB4D5F36A0481782509`.
The master is mono 24 kHz 16-bit PCM with 601,399 samples; it ends before the
original movie ends. Its resource declaration pins its size and full digest.

| Start (seconds) | Narration |
|---|---|
| 0.8 | In a metal hangar, the Epoch waits. |
| 5.7 | The red-haired boy leaps into the cockpit. |
| 10.0 | He grips the controls. |
| 12.6 | The engine blasts a yellow-white flame. |
| 17.0 | He smiles. |
| 18.5 | Three thrusters flare orange, then yellow. |
| 21.9 | A flash. The hangar is empty. |

The script uses appearance rather than a default character name. Local frames
and a Descript scratch review of the exact native film were checked. A canopy
closing was not established, so the script makes no claim about it. Whole-second
stills do not establish every movement; the leap, control poses and engine changes
were checked against the denser local sequence.

The field also describes three verified Nu actions: approaching the party,
raising both arms with its mouth open, and settling beneath the Epoch. Those
cues require the matching installed script and visible Nu, followed by proof
that the native movement or pose actually took effect. Repeated ordinary
conversations can describe the wake-and-settle actions again.

Fish generation used the same approved model/reference as the existing pack.
The ten new raw clips passed local transcription, non-silence and clipping
checks; each movie clip fits its reviewed window. The 156 previous WAVs are
unchanged. No native film, source voice recording, model ID, API key or private
upload URL is shipped. The exact public timing metadata is in
[movie-narration-0362.json](movie-narration-0362.json).

Muted x86 probes passed the exact movie/master through the game's native
six-node topology: natural completion, pause/resume and early close completed
without failed events, and both audio branches had continuous sample delivery.
These standalone probes did not launch the game or exercise its managed hook
registration in a live session. No new in-game playback, screen-reader listening
or controller test was performed.
