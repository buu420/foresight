# Separate movie narration, 0.3.37

The user's voice now plays on its own audio track at the same time as the original movie. The original soundtrack has no ducking, gain adjustment, limiter, remix, or re-encoding. Original AAC audio and H.264 video packets are copied. Only the narration track is newly encoded. All 40 reviewed cues and their original timestamps are retained for movies 001–003; no new ViddyScribe jobs or voice generation were needed.

The game uses one Media Foundation session and presentation clock for video, original audio, and narration. Its own pause, resume, skip, completion, and teardown apply to the whole presentation. There is no independent narration timer or external audio player.

## Native verification

The Ghidra export of `libcocos2d.dll` shows:

| RVA | Verified behavior |
| --- | --- |
| `0x286D9E` | VideoPlayer constructs its decoded movie path with GetTempPathA and `tmp.mp4`. |
| `0x287FAA` | setFileName decodes the selected DAT into that MP4, then calls OpenURL. |
| `0x2878D8` | OpenURL resolves the source, obtains its presentation descriptor, builds the topology, and passes it to the media session. |
| `0x287578` | cdecl topology builder takes source, presentation descriptor, window, and output pointer. It visits every selected stream. |
| `0x2870C8` | Each selected stream gets a source node and an output renderer. |
| `0x287427` | Audio receives MFCreateAudioRendererActivate; video receives MFCreateVideoRendererActivate. |

The new hook changes stream selection only for an exact SHA-256 match of a reviewed separate-track movie. It checks the loaded DLL's SHA-256 (`8A53EF5BFFD345D4EEE0E2D91152C539FCEB71725DB44A72D2E7513DF55ECD2F`) and the topology function's relocation-free entry bytes `55 8B EC 51 51`. The game executable remains unchanged. Media Foundation exposes the new mono narration, original stereo soundtrack, and video as three streams; the mod selects both audio streams and preserves the selected video.

Microsoft's [playback topology documentation](https://learn.microsoft.com/en-us/windows/win32/medfound/creating-playback-topologies) describes this source/renderer arrangement. An x86 harness also executed the actual game DLL topology function with the new movie: video plus both audio streams initialized, started, paused, and resumed successfully. Its presentation clock did not advance while paused. A separate x86 managed probe activated and disabled the actual Reloaded detour against the game DLL, using its loaded shared-hooks controller and the production COM adapter. The intercepted native call selected both audio streams, produced six topology nodes, and set opening narration readiness only after success.

## Recovery

The opening's screen-reader fallback is suppressed only when the current opening's two-track selection and native topology creation succeeded and the installed DAT still matches the manifest. Each topology build clears previous readiness. Ghidra confirms the opening's init at game RVA `0x2AE970` creates its player before the existing SceneManager::create observer starts the fallback check.

Missing packs and unsupported movie backends leave the optional movie hook inactive. Narration failures are reported locally; they do not fault the shared boundary used by menus, battle, and navigation. If the native two-track topology fails, the mod restores the original stream selection and retries the normal movie topology, leaving fallback speech enabled.

## Installation and validation

The local pack lives in `artifacts/research/audio-description-0337/pack`. The installer requires mod 0.3.37 or newer before installing its version-2 manifest. With the game closed, restore a previous pack, deploy the mod, then install the replacement:

```powershell
py -3 tools/Build-DescribedMovies.py restore --game ..
# Package and deploy mod 0.3.37 with the normal deployment tools.
py -3 tools/Build-DescribedMovies.py install --pack artifacts/research/audio-description-0337/pack --game ..
```

Originals and previous narrated files remain in `Accessibility/AudioDescriptions/backups`. Unknown edits are never overwritten. No game movies or private voice recordings are redistributed in the standard mod package.

Per-movie reports verify identical original audio and video packet hashes, PTS, DTS, and packet durations. The builder also preserves the original AAC edit list, so FFmpeg cannot extend its end trim to a complete AAC frame. Each output has exactly two audio streams. The narration waveform is silent outside its cues and retains the original voice speed and pitch. All 2,232 C# tests and 10 Python tests passed. Tests cover packet preservation, installation/restore, version compatibility, stream selection, rollback, optional backend failure, current-playback readiness, and fallback recovery. A runtime initialization regression checks that optional DLL hooks coexist with exact required EXE-hook validation and cannot hide a missing required hook.

Coverage is still movies 001–003. Remaining movies and NPC-action descriptions retain the [previous report's pending status](audio-descriptions-0336.md#coverage-still-pending). This correction does not add further scripts. Native harness checks are not a human listening review or a full live-game playback test.
