# Our play-session mod setup

This list records the modules loaded and used in our sessions. It is not a
blanket compatibility certification: those sessions exposed synchronization
problems, and the new fork still has incomplete native/two-PC regression coverage.
Versions below come from loader startup metadata, checked against the local DLL
inventory. They are not the generic Windows DLL product versions.

## Required

- **MelonLoader 0.7.3 x64**, installed separately:
  https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3
- **Open Nest Co-op — acsokis syncfix.11.1**, same version on all PCs.

## Recommended companion: Modernization

[Iron Nest Modernization Mod by N2774](https://www.nexusmods.com/ironnest/mods/82),
local package **2026.10.13.1**, supplied our handheld/map/fire-control workspace.
It is optional for base co-op, but both PCs need matching Modernization builds
for this fork's Modernization-specific sharing. Obtain it from its original
author; it is not included in the co-op ZIP. Follow the author's setup guidance.

| Module | Loader version | Used for / evidence boundary |
|---|---|---|
| Iron Nest Handheld | 0.7.46 | Handheld intel, notes, targets and fire-plan workspace; installed model bridge binds this build. |
| Iron Nest Bearing | 0.8.6 | Aiming/bearing tools used alongside the handheld. |
| Iron Nest Map Display | 0.6.18 | Map display and supported purchase-panel integration. |
| Iron Nest Report Screens | 0.4.19 | Report and briefing screens. |
| Iron Nest Automatic Coffee | 0.1.9 | Optional coffee convenience module. |

These were loaded in our setup. This does not establish that every F1-F3 or
physical interaction is shared correctly. Keep the package/modules matched
instead of mixing independently updated DLLs.

## Optional gameplay modules used in the setup

| Module | Loader version | Notes |
|---|---|---|
| Iron Nest Cat Collision | 0.1.0 | Cat collision change. Cat visibility did not establish collision-effect sync; a mismatch was reported earlier. |
| Iron Nest High Tide 40 | 0.1.2 | Startup reports 40 transports in the original 900-second assault window; a new High Tide mission is needed for that schedule. |
| Iron Nest Record Rider | 0.1.0 | Optional record-player-related gameplay module; loaded, but full co-op interaction coverage is unverified. |

These are not mandatory for basic co-op. Use matching gameplay modules on all
PCs. Their implementations are not redistributed here. Local DLL names are
`IronNestCatCollision.dll`, `IronNestHighTide.dll` and `IronNestRecordRider.dll`.

## Personal visual setup

A local HDR/ReShade configuration (`IronNest_HDR1050.ini`, `AutoHDR.addon64`) was
used for an LG G2 with a 1050-nit target. It is optional, display-specific and
was not required on the other player's PC. It is not included as a public preset
or a co-op dependency.

Open Nest Mod Menu/F6 was tried earlier, but is not in the current recommended
set: an F6 freeze was reported. It is not a must-have dependency.

Do not combine this MelonLoader co-op fork with the separate BepInEx co-op
implementation. See [installation](INSTALL.md) and [current limits](SYNCFIX_11_TEST_HU.md).
