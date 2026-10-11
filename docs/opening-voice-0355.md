# Opening narration correction

The development installation's movies 001–003 matched their original source
hashes, while installed-movies.json described narrated versions and retained a
backup path from an older machine. The opening probe correctly rejected that
stale state and used the existing Prism fallback. Restoring the approved 0.3.38
pack repaired the local installation. The original backups were checked before
relocating the manifest's backup path; restoration and installation used the
existing transactional movie tool.

The public correction embeds 22 Fish-generated WAVs for the unchanged
OpeningMovieTimeline entries. StoryVoiceCueCatalog identifies their exact text.
SemanticEventDispatcher selects them only after AccessibilityState accepts the
opening screen and generation. Unknown accepted text still uses Prism. Generation
changes cancel active and queued recordings, covering silent opening exits.
The existing native narration probe continues to suppress all fallback cues when
the installed opening's two audio tracks have successfully initialized.

All 124 preceding story WAVs and their manifest rows were preserved exactly.
Opening recordings use canonical 24 kHz mono PCM16 and the approved natural
voice reference. The pack now contains 146 verified clips. Regression tests check
the exact PCM duration of every opening cue against the next offset, with the
last cue bounded by the movie's verified 158.358208-second duration.

The native movie hook and Core timing did not change. The loaded game library
still matches the SHA-256 pinned by the existing
[Ghidra investigation](audio-descriptions-0337.md). A rebuilt x86 managed probe
executed the production presentation adapter and the actual game topology
builder: both audio streams and video were selected, six nodes were created and
opening narration readiness became true. This follows Microsoft's
[selected-stream topology model](https://learn.microsoft.com/en-us/windows/win32/medfound/creating-playback-topologies).

The fallback retains its existing elapsed-time scheduling. The separate movie
pack uses the native Media Foundation presentation clock for pause and resume.
These checks do not establish a live in-game listening result.
