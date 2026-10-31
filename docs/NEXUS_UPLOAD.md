# Nexus Mods upload pack

Use the existing **syncfix.11.1** binary release. This adds distribution assets
and documentation, not a new gameplay version.

## Page fields to copy

- **Game:** Iron Nest: Heavy Turret Simulator.
- **Name:** Open Nest Co-op - acsokis Syncfix Fork (MelonLoader).
- **Version:** 0.2.1-syncfix.11.1.
- **Summary:** Experimental Open Nest Co-op fork with exact mission/map matching,
  shared-state repairs, magazine action retries and a hardened updater.
  MelonLoader 0.7.3 x64 required. Manual install only.
- **Description:** copy [NEXUS_DESCRIPTION.txt](NEXUS_DESCRIPTION.txt).
- **Installation/changes:** [INSTALL.md](INSTALL.md) and [CHANGELOG.md](../CHANGELOG.md).
- **Required loader:** MelonLoader 0.7.3 x64, installed separately.
- **Optional integration:** Modernization; matching versions on both PCs.
- **Credits:** 1499501762 and upstream contributors; fork contributions by Gabor
  Kocsis (@acsokis), with AI-assisted implementation.
- **Permissions/license:** retain upstream AGPL-3.0 and included MIT library
  notices. Do not select an all-assets-original/no-redistribution permission
  template that contradicts those licenses. Review Nexus's upload terms yourself.
- **Vortex support:** unavailable/unverified; do not advertise it as supported.

## Main download

Upload **OPEN_NEST_SYNCFIX_11_1_MANUAL.zip** as the main manual-install file.
Suggested file description:

> MelonLoader-only mod files and required libraries. Extract beside the game EXE,
> merging Mods and UserLibs. MelonLoader 0.7.3 x64 must already be installed.
> Replace old co-op copies; install the same version on every PC. No Vortex
> support, loader, HDR, Modernization or saves included. Experimental release.

This ZIP has no nested archives, self-extracting installer, CMD or PowerShell
script. It contains the released mod/library bytes plus English documentation,
license, library notices and exact-source download link. The source ZIP is
available separately from the linked GitHub tag; it is not nested in the mod ZIP.

Do not upload `OPEN_NEST_SYNCFIX_11_1.zip` as the Nexus main file: that is the
separate GitHub installer package and includes `source.zip` internally.
Nexus documents rejection of nested archives and review of self-extracting
formats: [archive/security guidance](https://help.nexusmods.com/article/117-why-has-my-mod-been-quarantined).

## Requirements before publishing

Review [Nexus's current file submission guidelines](https://help.nexusmods.com/article/28-file-submission-guidelines),
including attribution, distribution permissions and AI categorization. Preserve
the original project link and exact-source link. This pack prepares the upload;
it does not grant an original author's personal endorsement or guarantee Nexus
acceptance. Do not claim complete multiplayer correctness or a malware-free
certification. Apply the actual Nexus scan result when the upload is scanned.

No Nexus page has been submitted by the assistant. You can paste the supplied
fields into your own new mod page. Vortex game support is a separate extension
project and is not part of this upload.

## Verified upload file

`OPEN_NEST_SYNCFIX_11_1_MANUAL.zip` SHA256:

```text
564648F934233191AAEC9FC83F78375E61D7653A9F2D8A5513C0416874972FB6
```

Verified 13 archive entries, with all 12 content hashes matching the included
manifest (the manifest itself is entry 13). The five DLLs match the pinned
published release. Archive paths use standard forward slashes. Local Microsoft
Defender scan: no threats found. The copy-paste command's download/hash phase was
tested without executing the installer again. Vortex and Nexus moderation/scan
results have not been tested or obtained.
