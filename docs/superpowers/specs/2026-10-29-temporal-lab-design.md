# Temporal Research Lab progression repair

The user approved repairing the lab passages and progression routes, clear control
labels, and manual guidance and automatic walking to the real security/warp
passages. Public release is already authorized. Preserve native hazards, floor
movement, locks, battles and player Confirm. Retain chest targets and native rules.

Use the installed scripts and captured collision/actor state. Extend the existing
staged terrain-door mechanism to the two omitted touch doors, remove robot-capture
consequences that were extracted as navigable exits, and correct the lab's Story
objective selection from the native flag sequence. Keep the existing scene router:
it selected the correct actual exits. Rate-limit belt-boundary speech while Repeat
continues to describe the current floor. Use appearance/location labels and current
native restrictions; do not reveal passwords, rewards or concealed encounters.

Alternatives considered: increasing the global arrival tolerance would worsen the
premature arrival; changing collision or native belts would change gameplay;
regenerating the whole navigation catalog would broaden risk. The scoped repair
uses the existing graph and current native door copies instead.

Evidence: retained Ghidra for the supported executable, Atel0617/0618/0620/0622/0623,
two 0.3.84 tester logs and sixteen exact snapshots. Private original-x86 contact
replay confirms617 actor12 from the near captured activation state. Derived camera,
addresses, party roster, alternate poses and native-script tile-copy replay must
remain explicitly distinguished from captured state and live gameplay.

Verify both guidance modes, a real exit boundary, closed/open/unavailable door
guards, the optional side-room/chest connection, belt direction/feedback and the
native Story stages. Build, package, deploy with preservation checks, independently
review, then publish standalone and Mod Manager artifacts for0.3.85. Live gameplay,
physical controller, audible speech and recipient-PC testing are not claimed.
