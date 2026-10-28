# Vortex passage implementation plan

> Execute inline with superpowers:executing-plans. User approved the scope and
> public release. Source is accessibility-mod; the parent game is deployment only.

Goal: guide through the current randomized vortex room using native passages and
required visible controls in both modes. Spec: ../specs/2026-10-28-vortex-passages-design.md.

1. [ ] Preserve the six exact route captures and fresh protected-file hashes.
   Reproduce the failures with the shipped0.3.82 DLLs. Validate Atel0633/0634/
   0637/0659/0660 and Ghidra173750/175EE0, preserving native guards and masks.
2. [ ] Add capture-backed VortexPassageTests and embedded raw fixtures. Include
   native movement along the full Palace route, both modes and native loss of
   control, doorway equivalence, unknown/disabled authority, unrelated scenes and
   neutral labels. Distinguish static637/660 data and synthetic bindings.
3. [ ] Implement only reproduced defects: bounded scene633 projection through
   OceanPalaceTargets; the659->660 look-ahead link in SceneConnectionRouter;
   VortexRoomTargets for current passage labels, verified634 doorway grouping and
   any required660 opening target. Do not expose future rooms or automate Confirm.
4. [ ] Verify focused RED/GREEN, full source tests and a bounded independent
   Claude Opus5.5/max review. No product builds or memory writes by the reviewer.
   Resolve material findings, version0.3.83 and pinned manifest hashes; document
   actual evidence, installation/retest instructions and live limits.
5. [ ] Build immutable standalone/AMM packages; verify exact x86 DLLs, installer,
   archive closure and source/deployment identity. Integrate and deploy with
   backups, preserving saves, settings, narration and unrelated mods.
6. [ ] Publish GitHub beta and AMM catalog with normal non-forced pushes. Verify
   all asset digests and anonymous downloads, catalog phases, public index and
   historical records. Write handoff and final receipts. No tester-only stopping.

Review focus: native repeating-plane bounds; current guarded random connection
versus another seed; mandatory battle continuations as look-ahead only; no false
off-route speech at actual touch contacts; closed-door/unknown authority; no
unseen item/enemy or next-room disclosure. Preserve previous0.3.82 lab behavior.
