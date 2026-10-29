# Frozen Cliffs Implementation Plan

> Execute inline with superpowers:executing-plans. The user approved behavior and
> public release; implementation choices stay within that scope.

Goal: identify and navigate the current Frozen Cliffs cave/chest routes using
native movement, with an opt-in slide assist. Spec: ../specs/2026-10-29-frozen-cliffs-design.md.
Architecture: scoped native feature/guard helpers, a directed slope graph and
ordinary input adjustment, integrated with the existing navigation controller.
Tech: .NET9/x86, native resource/Ghidra evidence and private offline x86 emulation.

1. [x] Preserve the tester log, native identities and fresh protected-file hashes.
   Establish a clean5398-test baseline in .worktrees/frozen-cliffs-0384. Audit
   six cave tiles, native facing, starting/landing poses and all12 chest records.
2. [x] Add FrozenCliffsNavigationTests and embedded initial maps/derived poses.
   RED: list6 stable distinct caves for facings0..3; unknown/stopped owner fails
   closed; Story Events binds the forward605 cave; native North entry, both modes.
   Implement FrozenCliffsTargets.RegionAvailable(scene,region,story), owner and
   label/entry helpers; integrate FieldNavigationSource and current router guards.
3. [x] RED: native downhill motion and input-faithful manual/automatic cave routes
   from logged/derived poses, including overshot/upstream targets and run settings.
   Implement FrozenCliffsSlideGraph as a bounded directed INavigationGraph,
   retaining actor/terrain/script terminals. Add scoped FrozenCliffsMovement input
   adjustment; preserve other scenes and native locks. Test loss of control,
   focus, context, cancellation and read failures. Record any architecture ruling.
4. [x] RED: all12 live unopened chest targets have stable labels and native Confirm
   reach from their relevant entrances; opened chests disappear. Preserve the
   current component filter and reject local uphill paths to other ledges.
   Add only scoped labels/reach changes the evidence
   requires. Include cliffs, caves, summit and adjoining Winding Passage checks.
5. [ ] Verify original x86 movement and native heading/arrival constraints in
   private emulation; label derived resources and exclusions honestly. Fresh
   bounded Opus5.5/max whole-branch review, resolve material findings, full tests.
   Version0.3.84, pinned manifest hashes, README and release/retest notes.
6. [ ] Commit immutable payload, package standalone and AMM, verify exact x86 DLLs,
   installer and archive closure. Integrate/deploy with backups; preserve saves,
   settings, narration, other mods and public catalog history.
7. [ ] Publish beta/source/catalog without forced pushes; verify anonymous assets,
   catalog phases, source/tag/payload identity and protected files. Write handoff
   and final closure receipt. No tester-only stop; no crash collector.

Review focus: facing is not a persistent lock; steep slopes cannot be climbed by
walking; moving-floor probes may disagree with cached force; no foreign actor or
scene inherits automation; all12 chests and their separate branches stay covered.
No unperformed live gameplay/controller/audible-speech proof is implied.
