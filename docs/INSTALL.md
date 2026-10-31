# Install Open Nest Co-op — acsokis syncfix.11.1

This is an experimental **MelonLoader-only fork**, not the original upstream
release and not the separate BepInEx co-op mod. Install the same co-op version on
every player's PC. Vortex game support is not available for this release.

## Requirements

- Iron Nest: Heavy Turret Simulator, Windows x64.
- MelonLoader **0.7.3 x64**, installed for the actual game executable. Launch the
  game once to generate its interop assemblies, then close it completely.
- Modernization is optional and not included. If used, both players must have
  matching versions. Keep other gameplay-affecting mods matched as well.
- Disable/uninstall another co-op implementation before using this fork. Do not
  run the BepInEx and MelonLoader co-op implementations together.

The updater installs this co-op mod and its included libraries. It does **not**
install MelonLoader, Modernization, HDR/ReShade or the game.

## Copy-paste installation / update

After installing MelonLoader and closing the game, open **PowerShell** as your
normal Windows user and paste this single command. It downloads the pinned
release CMD, checks its exact SHA256, then runs it. No administrator mode is
required by the command. Review the command before running it.

```powershell
$d=Join-Path $env:TEMP ('OpenNestInstall_'+[guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Path $d -ErrorAction Stop|Out-Null; $f=Join-Path $d 'IRON_NEST_UPDATE.cmd'; Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/acsokis/OPEN_NEST_CO-OP/releases/download/syncfix.11.1/IRON_NEST_UPDATE.cmd' -OutFile $f -ErrorAction Stop; if((Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash -ne '5290A00B2BF35373D1F3B52B1CDC5DB8DBE283483617EF7555EE36D48B708AC1'){throw 'Installer SHA256 mismatch: nothing executed'}; & $f
```

Alternatively, download `IRON_NEST_UPDATE.cmd` from the
[GitHub release](https://github.com/acsokis/OPEN_NEST_CO-OP/releases/tag/syncfix.11.1)
and double-click it. Old updater files remain pinned to their old versions.

The updater detects the Steam installation or asks for the game EXE, backs up
replaced files to `Before_CoopSyncfix_*`, and installs the file checker. Run
`IRON_NEST_ELLENORZES.cmd` from the game directory afterwards. A file-check pass
does not establish multiplayer gameplay correctness.

## Manual installation — Nexus Mods

Download **OPEN_NEST_SYNCFIX_11_1_MANUAL.zip**. Close the game. Back up your
existing co-op files and disable old/renamed copies of `OpenNestCoop.MelonMod.dll`,
including copies in subfolders of `Mods`.

Extract the ZIP into the folder containing `Iron Nest Heavy Turret Simulator.exe`.
Merge `Mods` and `UserLibs` with the existing folders; do not put the whole ZIP
inside `Mods`. The final layout is:

```text
Iron Nest Heavy Turret Simulator.exe
Mods/
  OpenNestCoop.MelonMod.dll
UserLibs/
  LiteNetLib.dll
  OpenNestUIKit.API.dll
  SharpGLTF.Core.dll
  SharpGLTF.Runtime.dll
OpenNestCoopDocs/
  README.md
  CHANGELOG.md
  LICENSE
  THIRD_PARTY_NOTICES.md
  VALIDATION.md
  RELEASE_SECURITY.md
  SOURCE.txt
  SHA256.json
```

Launch through Steam. Check the fresh `MelonLoader/Latest.log` for
`0.2.1-syncfix.11.1` and confirm exactly one co-op mod loaded. The manual ZIP
does not contain the automatic installer or hash checker; its file hashes are
in `OpenNestCoopDocs/SHA256.json`.

There is no supported Vortex deployment path yet. Download manually from Nexus;
do not describe this package as a Vortex extension or one-click Vortex install.

## Update, removal and personal data

For manager-free updates, use the new pinned updater. If another tool manages the
old mod, disable it there first so it cannot redeploy an older DLL later.

To remove this fork, close the game and remove `Mods/OpenNestCoop.MelonMod.dll`.
Remove included UserLibs only if no other installed mod uses them. Do not delete
the whole `Mods`, `UserLibs`, MelonLoader or save directory. An updater backup
contains the files it replaced; restore specific files while the game is closed.

Neither package includes HDR or personal saves. This release has incomplete
native/two-PC regression coverage; see [validation](SYNCFIX_11_TEST_HU.md),
[release security](RELEASE_SECURITY.md) and [changes](../CHANGELOG.md).
