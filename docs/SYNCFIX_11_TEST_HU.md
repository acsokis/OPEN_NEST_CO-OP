# Syncfix.11 validation status — 2026-10-26

This experimental MelonLoader fork is not fully verified multiplayer software.
Upstream milestone checkmarks describe implementation, not complete native or
two-PC validation. The legacy `_HU` filename is retained to preserve links;
this document is maintained in English.

Read the [complete fork changelog](../CHANGELOG.md) for the changes since the
inherited upstream commit.

| Area | Evidence and limits |
|---|---|
| Build | MelonLoader net6 build: zero errors; existing warnings remain. |
| Automated checks | 136 passed: document protocol, faults/reordering/reconnect, actual local TCP, data codecs, door intent and gun ordering. These are not 136 native gameplay tests. |
| Two complete game instances | Real host/guest handshake and common mission loading verified on one PC. |
| Background map | Tester confirmed matching backgrounds after exact MissionID selection replaced scene-only card matching. |
| Initial map state | One trial logged 26 native tokens sent/applied. This does not prove complete visual equality. |
| Tablet | Both replicas ready and host heartbeats received in the latest recorded trial; bidirectional notes/target selection still require native testing. |
| Doors/lights | Guest desired-state commands and old-heartbeat protection implemented; full native regression remains unverified. |
| Drawings | Actual native UpdateLine endpoints and RestorePlacement implemented; all shapes, labels and notes remain unverified. |
| Requisition | Card-list message 138 registered; stable slot keys and exact card lookup implemented. Complete guest purchase workflow remains unverified. |
| Both guns | Stable hierarchy-based routing implemented; every loading/ammunition combination remains unverified. |
| Scout plane | Lifecycle, animation and selected FMOD events implemented; complete native flight/photo/audio trial remains outstanding. |
| Local performance | Severe lag reported. Test copies now enforce 960x540/20 FPS. The precise cause and elimination have not been established by profiling. |
| Two independent PCs / Steam | Full regression coverage for this build remains outstanding. |
| Installer | Isolated fixture verified eight payload hashes, renamed-DLL backup, missing dependency reporting and HDR/save sentinel preservation. Public download digests matched. |

## Next joint session

Close the game and run the **new syncfix.11 updater CMD on both PCs**, then run
`IRON_NEST_ELLENORZES.cmd` from each actual game directory. Both players need
matching Modernization and other desired third-party mods. Old download CMDs
remain pinned to older releases; replace the command itself.

Verify matching backgrounds; guest notes and target selection; every drawing
tool; doors/lights in both directions; requisition card insertion/removal and
purchase; ammunition assignment/loading/fire for both guns; scout sound/flight/
photo; counter-battery timing/outcomes; and rejoin without resetting the host.
File checks do not replace these gameplay tests.

Public packages exclude HDR, personal saves, test logs, the game, generated
interop assemblies and third-party mod implementations. The two local test
copies use independent development save roots. The 960x540/20 FPS limit applies
only to explicit development-profile launches.
