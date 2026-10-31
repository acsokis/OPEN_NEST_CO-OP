# acsokis Open Nest syncfix fork

This is an experimental fork of [Open Nest Co-op](https://github.com/1499501762/OPEN_NEST_CO-OP),
contributed by Gabor Kocsis (@acsokis) with AI-assisted implementation.
The upstream authors and AGPL-3.0 license are preserved. It is not a port of
Jager2251's separate Iron Nest Co-op mod.

Current experimental test version: **0.2.1-syncfix.11.1** (MelonLoader).

Published update: **0.2.1-syncfix.11.1**, reviewed on 2026-10-31. Independent
magazine turn acceptance/queuing, deferred host magazine correction and entity
authority guards are implemented. The build passes, with 148 managed/network
checks and 31 package-safety checks. Package/installer/public download checks
passed; a local Defender ZIP scan found no threats. Native gameplay remains unverified for
these new paths. See [upstream review](UPSTREAM_REVIEW_2026-10-31.md),
[release security](RELEASE_SECURITY.md) and the [changelog](../CHANGELOG.md).

See the [complete fork changelog](../CHANGELOG.md) for the changes since upstream
commit `f49a188`, grouped by committed repair set and release.

Syncfix.11 selects the host's exact native MissionID instead of the first card
sharing its Unity scene. The tester confirmed matching background maps in two
complete local game instances on 2026-10-26. It also implements acknowledged
document replication, stable gun routing, guest door/light intents, native marker
endpoints, exact requisition card/slot routing and experimental scout replication.
These implementations do not establish complete native or two-PC coverage.
See [current validation and limitations](SYNCFIX_11_TEST_HU.md).

Download the new `IRON_NEST_UPDATE.cmd` from release syncfix.11.1. Old updater
copies remain pinned to their old release, so replace the CMD itself on both PCs.
No HDR, Modernization implementation, personal saves or private logs are bundled.

Local development: `Prepare-LocalPair.ps1` and `Start-LocalPair.ps1` create/start
two isolated full game copies. Explicit DevProfile mode enforces 960x540 windowed,
20 FPS and no VSync without writing PlayerPrefs. Normal launches are unaffected.
136 automated checks passed; real local TCP and game handshakes were verified.
The full tablet, scout, door/light, purchase and two-PC paths remain partially
verified. The background fix does not establish that all previous tablet issues
are resolved.

## Previous release history (validation below refers to those older releases)

Syncfix.10.1 corrects stale runtime/network version labels found during startup
validation. MelonInfo now uses NetConfig.Version, and packaging rejects a
runtime/package version mismatch. At that point .10.1 superseded .10; the current
release is .11.

Syncfix.10 adds a version-matched optional Modernization model bridge for intel,
map selection/coordinates/measurements, native notes, radar contacts, fire plans
and pending automatic jobs. Automatic purchases/firing and radar scanning run on
the host; guests propose edits and receive authoritative operational state.
Host-earned FCS access is mirrored for the session without changing preferences
or granting a permanent unlock. Door and lamp settled states receive host
corrections. Chat starts hidden (F8 toggles it).

The installer writes `IRON_NEST_ELLENORZES.cmd` into the actual game directory.
It compares co-op/dependency and installed Modernization hashes with the package
baseline, detects duplicate co-op assemblies and reports another running copy.
An install with missing/mismatched mods is explicitly marked incomplete.
Hashes prove file identity; they do not prove two-PC gameplay correctness.

Validation: 49 standalone checks passed, including round trips using the locally
installed Modernization model types; MelonLoader build passed with zero errors.
The private model bridge requires the same Modernization assembly MVID on both
machines and logs unsupported contracts instead of claiming synchronization.
Physical safe/chip/pairing interactions and every private F1-F3 command are not
covered by this data bridge. Multi-player runtime tests remain mandatory.

Syncfix.9 adds scene-scoped calculator-card deletion and skips identical native
state restores, preventing closed cards/views from being reopened by heartbeats.
See [SYNCFIX_REVIEW.md](SYNCFIX_REVIEW.md) for implemented changes, evidence,
validation, and outstanding work. This branch must not be presented as fully
working multiplayer until the two-PC checks are complete.

## Development

Install .NET SDK and MelonLoader 0.7.3 in your own Iron Nest installation.
Launch the game once so MelonLoader generates its interop assemblies.
The repository does not contain the game, generated game assemblies, personal
saves, credentials, or private session logs.

Build without deploying:

```powershell
dotnet build src/OpenNestCoop.MelonMod/OpenNestCoop.MelonMod.csproj -p:ClientGame="P:\SteamLibrary\steamapps\common\Iron Nest Heavy Turret Simulator" -p:DeployToMods=false
```

Run the map packet codec checks:

```powershell
dotnet run --project tests/MapSnapshotCodec.Checks/MapSnapshotCodec.Checks.csproj
```

Create a local installer package (the game must already have MelonLoader):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/syncfix/Build-Package.ps1 -GamePath "P:\SteamLibrary\steamapps\common\Iron Nest Heavy Turret Simulator"
```

Generated packages go to the ignored `artifacts/` directory. The builder includes
the corresponding committed source with the installer. It requires a clean Git
checkout so the included source matches the build.

The builder does not bundle Modernization, its media, HDR/ReShade, the game, or
generated game assemblies. Both players must install the desired third-party
mods separately and use matching versions. The privately prepared friend update
can be kept in `artifacts/` locally; it is not published as source code.

The installer auto-detects the active/Steam-installed game, validates payload
hashes, backs up replaced files, and writes an installation report. If the game
still displays an older mod version, compare that report with its fresh loader log.
