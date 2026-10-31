# Upstream review — 2026-10-31

## Open Nest source

The live `1499501762/OPEN_NEST_CO-OP` main ref still resolves to
`f49a18863adb69b1401689843d08015e2d8eec8a`, our inherited source baseline.
The annotated v0.2.1-Alpha-3 tag peels to that same commit. There is no new source
delta to merge from this upstream. The latest published GitHub release returned
by its releases API is v0.2.1-Alpha-2; source tags and release assets are distinct.

Our latest public release remains syncfix.11. The local syncfix.11.1 hardening
work is not a published release. On this review date its 30 package-safety checks
passed and the local MelonLoader build completed with zero errors; packaging,
installer regression and public-asset verification must still be completed.

## Separate BepInEx implementation

[Jager2251/Iron-Nest-Co-Op-Mod 2.5.1](https://github.com/Jager2251/Iron-Nest-Co-Op-Mod/releases/tag/2.5.1)
was published on 2026-10-30. Its predecessor 2.5.0 targets game update 1705.
These are a separate implementation, not updates to the Open Nest source tree.
The public repository currently contains release documentation and binaries,
but no C# source tree. No code from its binary has been incorporated here.

Reported changes to evaluate independently in this fork:

- Preserve guest mission waits for teleprinter completion.
- Distinguish repeated entities with the same logical identity; defer state
  application until the corresponding local spawn exists.
- Reconcile destroyed/damaged unit state and native unit reactions.
- Align requisition statistics and actual host-paid card costs.
- Preserve engine-start and shell-triggered native behaviour events.
- Avoid full resync while a player is loading; release barriers on disconnect.
- Recover Steam lobby membership after connection loss.
- Serialize shell-magazine turns, reject native-ignored presses, reconcile the
  magazine after turns and before loading, and release levers after contention.
- Give joining players explicit mission-in-progress rejection feedback.
- Evaluate 1705 area effects, unit behaviours, movement and mission statistics.

Release notes identify behaviours to investigate; they do not provide compatible
source patches or establish that this fork supports these features. The local
generated game API is dated 2026-10-23, so newer game mechanics require current
game/API validation before distribution.

## Installation boundary

Do not install the separate BepInEx implementation alongside the current
MelonLoader co-op stack as an automatic update. Changing loader/mod stacks and
updating the existing fork are different operations. Preserve current files,
saves, HDR and matching third-party mod versions when testing either option.
