# Open Nest Co-op — acsokis syncfix.11.1

The original mod promised a lot, but in our co-op sessions player-to-player
synchronization did not work reliably for us: maps, cards, controls and mission
timing could diverge. That is why I started fixing the issues we encountered and
building this fork. This describes our experience, not every player's setup;
the changes below do not mean every sync issue has been resolved.

Experimental **MelonLoader 0.7.3 x64** fork of Open Nest Co-op by 1499501762 and
upstream contributors. Fork contributions by Gabor Kocsis (@acsokis), with
AI-assisted implementation. AGPL-3.0 and dependency notices are preserved.

## What is shared

Work as one turret crew with a shared tactical workspace. Sharing is implemented
for supported **notes, target selections, coordinates/measurements, fire-plan
data, tactical map tokens/drawings and printed calculator/aiming cards**.
Supported printed fire-mission cards share creation, placement, dragging and
closure, rather than being independent printouts on each PC.

Both host and guests can submit supported edits. The host maintains the accepted
shared state and drives automatic world outcomes. Modernization data sharing
requires matching Modernization versions on both PCs. Opening F1/F2/F3, cursor
focus, window visibility and chat visibility remain local; another player's
edit does not force your menu open. Simultaneous conflicting edits can be
rejected in favour of the accepted host state.

These are implemented paths with incomplete native two-PC coverage, particularly
notes/target/card workflows and private F1-F3 commands. See the limits below.

## Downloads

- **IRON_NEST_UPDATE.cmd:** automatic install/update for an existing MelonLoader
  installation. Includes backups and a file checker. Both players must update.
- **OPEN_NEST_SYNCFIX_11_1_MANUAL.zip:** Nexus/manual-install package; extract
  beside the game EXE, merging `Mods` and `UserLibs`. No Vortex support yet.
- **NEXUS_DESCRIPTION.txt / NEXUS_UPLOAD.md:** ready-to-paste page text and upload
  notes, with requirements, attribution, known limits and original-versus-fork changes.
- [Copy-paste installer command](https://github.com/acsokis/OPEN_NEST_CO-OP/blob/main/docs/INSTALL.md).

The manual package adds documentation and distribution packaging only. Its five
DLLs are identical to the previously published .11.1 binary release. It excludes
the automatic installer's nested source archive and executable installer scripts.

## Changes from the original source snapshot

- Exact host MissionID selection instead of ambiguous scene-based mission choice.
- Native map initialization, card lifecycle/shared movement and timer authority repairs.
- Stable left/right gun routing and improved trajectory/reload compatibility.
- Matching-model Modernization interoperability and guest door/light intents.
- Actual native drawing endpoints, card catalogue registration and exact requisition routing.
- Magazine action ordering/retries, native-accepted turn filtering and deferred host correction.
- Host-only entity health/lifecycle and magazine-state authority.
- Local reconnect handling, isolated test profiles and default-hidden F8 chat.
- Hardened updater: pinned ZIP hash, exact file allowlists, bounded extraction,
  path/link rejection, backup and explicit installation checks.

This is not the original upstream release or a full port of the separate
BepInEx 2.5.1 co-op implementation. Modernization, its media, HDR, the loader,
game files, generated interop assemblies and saves are not bundled.

## Evidence and limits

Build: zero errors. **148 managed/network checks and 31 package-safety checks**
passed. Local two-instance connection and matching backgrounds were verified.
The manual ZIP's file layout and archived hashes were checked, and its local
Microsoft Defender scan reported no threats. This is not a malware-free
certification or a complete multiplayer verification.

New magazine paths, every private F1-F3 command, all drawings/cards/doors/scout
paths and independent-PC/Steam coverage remain incomplete. Game-update-1705
coverage is not established. Read [validation](SYNCFIX_11_TEST_HU.md),
[security limits](RELEASE_SECURITY.md) and the [full changelog](../CHANGELOG.md).

Exact gameplay binary/source commit: `d410603044326fee17dfa39bb285353fd5506b16`.
[Corresponding source](https://github.com/acsokis/OPEN_NEST_CO-OP/archive/refs/tags/syncfix.11.1.zip).
Later main-branch documentation commits do not change those binary assets.

## Our play-session companion mods

Required: MelonLoader 0.7.3 x64 and this fork. Our recommended companion is
[Modernization by N2774](https://www.nexusmods.com/ironnest/mods/82), local package
2026.10.13.1: Handheld 0.7.46, Bearing 0.8.6, Map Display 0.6.18, Report Screens
0.4.19 and Automatic Coffee 0.1.9. Both PCs need matching builds for that bridge.

Optional modules loaded in our setup: Cat Collision 0.1.0, High Tide 40 0.1.2 and
Record Rider 0.1.0. Their complete co-op effects are not certified. HDR at a
1050-nit target was a personal LG G2 visual setup, not a requirement for other
players. F6 Mod Menu is not recommended as required; a freeze was reported.

Read the [full setup and evidence boundaries](https://github.com/acsokis/OPEN_NEST_CO-OP/blob/main/docs/COMPANION_MODS.md).
Companion mods and HDR are not included in this release.
