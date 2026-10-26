# Syncfix.10 joint test plan — historical

This is the archived .10 test plan. Use the
[syncfix.11 validation document](SYNCFIX_11_TEST_HU.md) for the current release
and the [changelog](../CHANGELOG.md) for version history. The legacy `_HU`
filename is retained for existing links; the text is now English.

This is an experimental fork. File validation does not replace joint gameplay.

1. Close Iron Nest completely on both PCs before applying the same updater.
2. Run `IRON_NEST_ELLENORZES.cmd` in each actual game directory; inspect both reports.
3. Check both fresh `MelonLoader/Latest.log` files for the intended version. The
   original .10 metadata mismatch was corrected in .10.1; use .11 today.
4. Have the host continue the save. The guest joins instead of starting a mission.
5. F1/F2: test markers, coordinates, measurements, selections and notes both ways.
6. On the native map, test red/yellow/white lines, compass tools, tokens and the
   corresponding Bearing/Distance text.
7. F3: edit/reorder plans from either player; test start/pause/automatic firing.
8. Confirm one shared automatic purchase/shot, with loading and impacts for both guns.
9. Test doors, stair hatch, power switch, lamps and emergency lights both ways.
10. Close cards with X; confirm they stay closed. Rejoin must not reset the host.
11. Counter-battery countdown starts/expires once according to host timing.
12. Chat starts hidden and can be toggled locally with F8.

For a failure, retain both players' fresh `MelonLoader/Latest.log`,
`OpenNestLogs/sync.log` and `IRON_NEST_ELLENORZES.txt`, with host/guest roles and
the event time. Keep private logs out of the public repository.
Physical safe/chip/pairing operations are not fully covered by the Modernization
data bridge. A checklist entry is a test requirement, not a recorded pass.
