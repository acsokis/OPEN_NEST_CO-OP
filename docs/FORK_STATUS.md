# acsokis Open Nest syncfix fork

This is an experimental fork of [Open Nest Co-op](https://github.com/1499501762/OPEN_NEST_CO-OP),
contributed by Gabor Kocsis (@acsokis) with AI-assisted implementation.
The upstream authors and AGPL-3.0 license are preserved. It is not a port of
Jager2251's separate Iron Nest Co-op mod.

Current local test version: **0.2.1-syncfix.9**.

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
