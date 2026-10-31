# Changelog

Changes in the **acsokis experimental Open Nest Co-op fork**, contributed by
Gabor Kocsis (@acsokis) with AI-assisted implementation. Upstream authorship and
the AGPL-3.0 license are retained. This is not an official upstream release or
a port of the separate BepInEx co-op mod.

The fork starts from upstream
[`f49a188`](https://github.com/1499501762/OPEN_NEST_CO-OP/commit/f49a18863adb69b1401689843d08015e2d8eec8a)
(v0.2.1-Alpha-3 source snapshot). The entries below cover the committed fork
changes through syncfix.11.1. Earlier private syncfix.1-.6 builds are not separate
commits in this history, so their contents and test results are not reconstructed
as releases here. Initial fork changes were committed on **2026-10-26**;
the .11.1 update follows on **2026-10-31**.

“Implemented” describes code changes. It does not mean every native game path or
two-PC session has passed. See [current validation](docs/SYNCFIX_11_TEST_HU.md).

## Distribution documentation — syncfix.11.1

- Add a SHA256-verified copy-paste PowerShell install command and English manual
  install/update/removal instructions; MelonLoader remains a separate prerequisite.
- Add a Nexus main-file repack preserving the released DLL bytes, with Mods and
  UserLibs at the archive root, source links, attribution and MIT library notices.
  Exclude nested archives and executable installer scripts from that ZIP.
- Add ready-to-paste Nexus page text and an upload checklist. Vortex support is
  unavailable; the package is explicitly labelled manual install only.
- Existing gameplay binaries and original release assets remain unchanged.

## 0.2.1-syncfix.11.1 — 2026-10-31

[Release](https://github.com/acsokis/OPEN_NEST_CO-OP/releases/tag/syncfix.11.1).
Binary/source commit: `d410603044326fee17dfa39bb285353fd5506b16`.

- Independently implement selected magazine fixes motivated by the separate
  2.5.1 release notes: publish only native-accepted turns, queue incoming actions
  in per-gun order while busy, bound retries, deduplicate event IDs and scope
  actions to the current mission. Reject non-member or non-host guest senders.
- Retain host magazine corrections received while reloading/rotating and retry
  them after the native operation finishes, instead of discarding the correction.
- Reject guest entity health/lifecycle and magazine-state overwrites; player map
  interactions continue through their dedicated channels.
- 148 managed/network checks now pass, including delayed magazine actions,
  cross-gun progress, native-ignored turns, expiry and queue capacity. Native
  gameplay validation remains outstanding; this is not a full 2.5.1 port.

- Pin updater ZIP hashes at build time; check release identity and download URL.
- Add exact payload/archive allowlists, complete hash manifests, bounded safe
  extraction and junction/reparse-point rejection. Preserve wrapper exit codes.
- Add 31 passing release-safety checks. Build, package archive validation,
  nine-file installer fixture, actual game-folder hash checks and public asset
  digest/updater pin verification pass. Local Microsoft Defender ZIP scan found
  no threats; this is not a malware-free certification. See
  [release security](docs/RELEASE_SECURITY.md).
- [2026-10-31 upstream review](docs/UPSTREAM_REVIEW_2026-10-31.md): Open Nest
  remains at the inherited `f49a188`; the separate BepInEx mod is at 2.5.1.
  No newer Open Nest source changes or code from that separate binary have
  been merged.

## 0.2.1-syncfix.11 — 2026-10-26

Commit: [`441c773`](https://github.com/acsokis/OPEN_NEST_CO-OP/commit/441c773d221b8e0b26731bf2a6cdd546f5e0ad3c).
[Release and updater](https://github.com/acsokis/OPEN_NEST_CO-OP/releases/tag/syncfix.11).

### Fixed

- Follow the host's exact native **MissionID**, using an explicit `@m:` identity,
  instead of activating the first mission card sharing a Unity scene. Different
  cards can use different background maps despite having the same scene name.
  The tester confirmed matching backgrounds after this change in two complete
  game instances on one PC.
- Replace local TCP connection state safely on reconnect. Reader generations
  retain their own streams; retired readers cannot consume a replacement stream
  or deliver stale queued packets. Writes are serialized per connection.
- Route turret/reload requests through stable hierarchy-based gun keys, avoiding
  left/right mismatches caused by different discovery order.
- Register punchcard-list message **138**, retry card creation after console
  readiness, refresh card caches and resend the host catalogue periodically.
- Identify card slots by hierarchy and component ordinal instead of discovery
  indexes. Deduplicate by event ID rather than a time debounce that could drop
  a valid remove/place sequence. Retry early slot events until objects are ready.
- Route requisition requests to the exact card and station. Remove first-slot
  fallback and global purchase debounce; the host reports acceptance/rejection.
- Capture actual native `UpdateLine` endpoints and restore finalized drawings
  with `RestorePlacement`; stop deriving endpoints from rendered tip offsets.
  Include saved native drawings and use collision-resistant marker IDs.
- Replace host-only door/light corrections with guest desired-state commands.
  Preserve pending local intent against older host heartbeats and avoid competing
  hatch/click replication on controls owned by this path.

### Added and changed

- Use a reliable document coordinator for Modernization models: correlated
  initial snapshots, host epochs, client incarnations, revisions, acknowledgements,
  retries, stale proposal rejection and heartbeat repair. Physical commands wait
  for pending edits and are deduplicated before execution. Concurrent conflicting
  edits can be rejected; this is not a semantic merge engine.
- Route supported Modernization map purchase selections and orders to the host
  with explicit left/right lanes. Mirror panel status and session FCS access.
  Automatic scanning, purchases and firing remain host-controlled.
- Add experimental scout lifecycle replication: native prefab instances,
  transforms, animation state and selected FMOD event timing. Avoid duplicate
  guest-generated scout objects and replaying completed historical sounds.
- Add opt-in runtime diagnostics for mission/map identity, scout components,
  gun lane bindings and Modernization readiness/packet counters.
- Add scripts for two complete local game copies, independent development save
  roots and real loopback host/guest testing. Explicit development mode enforces
  960x540 windowed, 20 FPS and VSync off; normal launches do not enable this mode.
- Publish matching committed source, dependency hashes, the game-folder checker
  and a version-pinned download CMD. Public packages exclude game files,
  generated interop assemblies, third-party mod implementations, HDR and saves.
- Clarify that README milestone checkmarks describe upstream implementation,
  not complete runtime verification of this fork.

### Validation and remaining work

- MelonLoader build: zero errors; existing compiler/package warnings remain.
- **136 automated checks passed**, including actual local TCP transport,
  document fault/reordering/reconnect scenarios, model codecs, gun ordering and
  door-intent regressions. These are not 136 native gameplay tests.
- Real local game host/guest handshake, common mission loading and matching
  backgrounds were verified. One initial native map transfer applied 26 tokens.
- Isolated installer fixture: all eight payload hashes matched, renamed duplicate
  co-op DLL was backed up, missing dependencies were reported, HDR/save sentinel
  files were preserved. Public downloads matched GitHub asset SHA256 digests.
- Full F1-F3 edits in both directions, all drawing shapes/labels, requisition
  workflows, both guns, doors/lights, scout audio/photos, counter-battery and
  independent-PC/Steam regression coverage remain incomplete.
- Severe local-pair lag was reported. The test display/FPS cap was added; the
  precise cause and its elimination have not been established by profiling.

## 0.2.1-syncfix.10.1 — 2026-10-26

Commits: [`ac43d50`](https://github.com/acsokis/OPEN_NEST_CO-OP/commit/ac43d50),
[`bb8b837`](https://github.com/acsokis/OPEN_NEST_CO-OP/commit/bb8b837),
[`1f40354`](https://github.com/acsokis/OPEN_NEST_CO-OP/commit/1f403540dc41b2d693e1fb9bc4071a62b63fd01d).

- Unify MelonInfo, network and project version labels. The initial .10 source
  still advertised `.9` in runtime/network metadata; packaging now rejects that
  mismatch. The .10 release was marked superseded.
- Fingerprint the game executable, GameAssembly and MelonLoader as well as mods.
- Validate manifest entries and reject incomplete hashes/unsafe paths.
- Exercise installer backups, renamed duplicates, missing/wrong dependencies and
  HDR preservation using an isolated fixture.

## 0.2.1-syncfix.10 — 2026-10-26

Commit: [`3735131`](https://github.com/acsokis/OPEN_NEST_CO-OP/commit/3735131).
Historical package label; runtime version metadata was corrected in .10.1.

- Add an optional matching-MVID Modernization model bridge for intel, target
  selection, coordinates/measurements, native notes, radar, fire plans and pending
  automatic jobs. Use expected installed model types rather than network-selected
  CLR types or methods. No Modernization implementation is redistributed.
- Keep automatic queue execution, automatic physical controls and radar scans on
  the host. Mirror host-earned FCS access only for the session.
- Add settled door/hatch/light state replication. The later guest snapback report
  motivated the desired-state protocol in .11.
- Start chat hidden using a new preference key; F8 toggles local visibility.
- Add the GitHub download updater and game-folder path/hash/version checker.
- Record 49 automated codec, lifecycle, authority and installed-model checks.
  Full private F1-F3 commands and physical safe/chip/pairing were not covered.

## 0.2.1-syncfix.9 — 2026-10-26

Commit: [`2393370`](https://github.com/acsokis/OPEN_NEST_CO-OP/commit/2393370).

- Replicate calculator-card closure with scene-scoped deletion tombstones.
  Reject stale upserts and discard obsolete pending updates after closure.
- Skip unchanged native card surface restores that could reopen closed views.
- Reject guest mission/seed overwrites and guest automatic lifecycle outcomes.
  Disconnect before guest-local mission navigation so returning/reloading locally
  cannot reset or end the host's mission.
- Reset card lifecycle tracking on scene handle/native mission instance changes.

## Packaging and fork identification — 2026-10-26

Commit: [`64d3f36`](https://github.com/acsokis/OPEN_NEST_CO-OP/commit/64d3f36).

- Identify the experimental fork and contribution without claiming official
  upstream status or authorship of the original mod.
- Add reproducible installer/package generation from a clean committed checkout,
  corresponding source archive, license and SHA256 manifests.
- Detect installed/running game paths, back up replaced files and handle renamed
  duplicate co-op assemblies. Public packaging excludes HDR and personal state.

## 0.2.1-syncfix.8 — 2026-10-26

Commits: [`7c74010`](https://github.com/acsokis/OPEN_NEST_CO-OP/commit/7c74010),
[`7a6b2a5`](https://github.com/acsokis/OPEN_NEST_CO-OP/commit/7a6b2a5).

- Transfer compressed native map-only state: saved token surfaces and dynamic
  photo/impact objects, with scene readiness and request correlation. Do not
  restore progression or replay the full mission graph for map synchronization.
- Include drawings already restored from a host save in marker snapshots.
- Add native calculator-card capture/restore and bidirectional dragging/drop
  surface state, preserving local grab/release control.
- Suppress guest-owned autonomous counter-battery timer/spawn paths.
- Add nine compression-codec tests covering Unicode, large payloads, malformed
  data and compressed/decompressed limits. Local-host startup was verified;
  complete map/card multiplayer behavior was still outstanding.

## 0.2.1-syncfix.7 — initial committed repair set — 2026-10-26

Commit: [`612e16e`](https://github.com/acsokis/OPEN_NEST_CO-OP/commit/612e16e).

- Update MelonLoader API compatibility, including the three-argument reload
  SetState call and the loader's Harmony assembly.
- Find hidden native map tool prefabs; publish native marker-finalized events for
  measurement notes without echoing received drawings.
- Retry map state after readiness, defer early token snapshots and publish host
  token changes outside dragging. Keep guest photo placement from overwriting
  host placement.
- Preserve the native projectile start clock when applying host trajectories;
  add direct elevation replication and firing pose to guest fire requests.
- Resolve shell prefabs from loaded definitions and support partly empty drums.
- Add initial calculator-card creation synchronization.
- Transfer counter-battery time/running/expired/stopped atomically. Guard guest
  autonomous timers/spawns and reject guest automatic mission outcomes.
- Retain engine/power lever and teleprinter repair work in the initial committed
  set, including printer-only heartbeats instead of replaying mission snapshots.
- Add persistent, locally switchable F8 chat visibility; hidden chat ignores Enter.
- Document reported trajectory delays, missing marker prefabs, timer divergence
  and guest-originated failure events. Private logs are excluded from the repo.

## Baseline and attribution

Upstream already supplied Steam/LAN lobbies, transport, avatars, turret and fire
paths, typewriters, cats, card/entity synchronization, configuration, UI libraries
and other systems. Those are upstream features, not newly authored fork features.
The changelog above describes modifications layered on that source snapshot.

For the exact patch history, compare
[`f49a188...441c773`](https://github.com/acsokis/OPEN_NEST_CO-OP/compare/f49a188...441c773).
Documentation-only follow-ups may appear after the release commit without
changing its already published binary assets.
