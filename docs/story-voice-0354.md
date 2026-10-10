# Recorded story narration in 0.3.54

The story-action observer now selects Fish-generated recordings made from the
creator's approved natural-voice reference. The recordings are embedded in the
mod; players need no Fish account, API key, network access or voice-generation
software. The private model, credentials and original human recording are not
included in the source or downloads.

The pack contains 124 clips: named and appearance variants for 57 reviewed
party-animation mappings, and ten variants for seven exact opening/fair actions.
It uses the existing [native action proof and visibility rules](story-actions-0353.md).
This changes the voice of the supported descriptions; selected scene coverage
remains incomplete.

Default character names use the named recording after the native introduction.
Before introductions, and for custom character names, recordings use a physical
appearance label. Logs and braille retain the current character name. The pack
manifest binds every cue ID to its text, SHA-256, size and exact PCM duration;
corrupt, partial or mismatched recordings fall back to the screen reader.

Descriptions play on a dedicated waveOut device, separate from footsteps. Later
game speech waits for the driver to mark the recording complete. Initial dialogue
choices wait behind a just-flushed description. Menus, battle resets, scene changes,
backgrounding and shutdown cancel recordings without advancing the game. If the
driver stalls past the recorded duration plus two seconds, playback is stopped,
the caption is spoken through Prism and queued speech is released. Deferred Prism
failures reach the existing independent accessible error boundary.

Installed NVDA 2026.1 cannot report speech completion through this Prism build.
The mod therefore uses its existing native dialogue boundaries and does not
estimate how long previous screen-reader speech takes. Advancing while a previous
line is still being read can overlap that line with a voice clip. Following speech
is ordered against actual recording completion. No game timing or Confirm input
is changed.

The pinned [Prism NVDA backend](https://github.com/ethindp/prism/blob/v0.17.3/source/backends/nvda.cpp)
and NVDA 2026.1 source were checked for completion and braille behavior. Tests
exercise actual semantic dispatch, native-call counts, cancellation, queue ordering,
driver stalls, fallback, and x86/x64 WAVEHDR ownership. Local transcription checked
all 124 clips, with no large text mismatch, silent clip or clipped sample. A real
x86 Windows waveOut check played the curtains cue and observed driver completion.
In-game listening of the new recordings remains to be tested.
