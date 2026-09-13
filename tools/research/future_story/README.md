# Installed PC area research

Run from the repository with Python 3 (standard library only):

```powershell
py -3 tools/research/future_story/audit.py --game-root 'X:\SteamLibrary\steamapps\common\Chrono Trigger' --output artifacts/research/future-story-audit.json
```

Choose the actual game path. The tool reads the executable and ARC1 resource archive, validates the supported executable hash, and writes a research report to the chosen output path. It does not control the game or change installed files. No original game assets or dialogue are included in this directory.

Format reference: [GitExl/CTViewer](https://github.com/GitExl/CTViewer), commit `2e5a206e09f0028fd5a1ca6cb9a9ed18bfc64fea`. Its MIT notice is retained in `CTViewer-LICENSE.txt`.

The operand table is a research aid, with explicit PC corrections in `decode_verified.py`. In particular, opcode 55 reads the story counter; 56 with global index zero and 5A write it. Actor startup follows the initialization return. Following possible branches reports candidate instructions, not whether a branch runs in the current save. Always inspect call sites and conditions before turning a reported actor into a navigation target. The runtime uses its own coherent state capture, never this offline decoder.

See `docs/future-story-navigation.md` at the repository root for the audited gates and remaining live-test limits.
