# Experimental MelonLoader interoperability changes

Contribution prepared for Gabor Kocsis (@acsokis), with AI-assisted implementation.
Based on upstream commit f49a18863adb69b1401689843d08015e2d8eec8a.
The upstream AGPL-3.0 license continues to apply.

## Intended behavior

Every player can manipulate shared map markers and drawings. The host supplies
the initial world state and automatic mission outcomes; it does not exclusively
own player interactions. Grabbing a previously remote-controlled token must
transfer control and publish its final drop.

## Changes

- Build against the current MelonLoader-generated game API, including the
  three-argument reload SetState method and the loader's Harmony assembly.
- Discover hidden map tool prefabs and publish native marker-finalized events
  for measurement notes without retransmitting received drawings.
- Restore map-only state requests after scene readiness without replaying reload
  state; defer early token snapshots and send host token changes outside dragging.
- Keep guest-generated automatic photo placement from overwriting host placement.
- Preserve the native projectile start clock when applying host trajectories.
- Replicate direct gun elevation and include firing pose in guest fire requests.
- Resolve shell prefabs from loaded definitions and support partially empty drums.
- Add FireMissionCard creation and bidirectional dragging/drop surface state
  using native capture/restore APIs, with local drag/release protection.
- Syncfix.9 replicates card closure with scene-scoped deletion tombstones,
  rejects stale upserts after deletion, removes obsolete pending updates, and
  skips identical native surface-state restores that could reopen closed views.
- Syncfix.9 also rejects guest mission scene/seed and all guest lifecycle events;
  local guest navigation disconnects before returning/reloading/ending locally.
  The observed guest `ev=6` exit must not end the host's operation.
  Card lifecycle tracking resets on scene handle/native mission instance changes
  so unloading/reloading the same named mission does not send spurious deletions.
- Add compressed native map initialization for dynamic photos/impact objects and
  saved token surface state; scene readiness and request correlation are checked.
- Include native host drawings restored from a save in marker snapshots.
- Transfer counter-battery time/running/expired/stopped together, prevent guest
  autonomous timer updates and spawns, and reject guest automatic mission outcomes.
- Add a persistent local F8 chat visibility toggle; hidden chat ignores Enter.
- Retain the engine and teleprinter repair changes from the local test builds.

## Evidence and validation

The original guest log showed shots received at 23:25:02 with a 35-second flight,
but an impact only at 23:28:32. It also showed missing MapMarkerYellow prefabs,
counter-battery spawns with no host seed, and a guest-originated failure event.
Private logs and account identifiers are deliberately excluded from this repo.

Local MelonLoader builds succeeded for syncfix.8 (72 warnings, zero errors).
The installed syncfix.6 reached the main menu. Packaging checks verified payload
hashes and installer parsing. These do not establish multiplayer correctness.
Nine tests exercise the actual map snapshot compression codec, including Unicode,
large payload round trips, malformed input, and compressed/decompressed size limits.
The map snapshot and token/card manipulation runtime tests remain outstanding.
The syncfix.8 local-host startup registered the native map, calculator card, and
counter-battery clock modules; timer guards installed without a startup error.
The local-only test was closed from the main menu and normal Steam launch restored.

## Outstanding validation and limitations

Syncfix.10 adds `ModernizationStateSync`, a bounded compressed data-model bridge
using only an installed assembly's expected types and matching module MVIDs.
Host revisions serialize intel/selection/moving-contact, notes, radar and fire
plan/pending-job edits; stale proposals receive the current authoritative value.
Automatic queue execution, auto-port controls and native radar scans are guarded
on guests. Host FCS access is a session-only mirror. UI open/close/focus remains
local. This is interoperability code, not copied Modernization source.

`WorldControlsSync` publishes settled door/hatch and light master/switch/manual
states; clicks remain bidirectional. Periodic corrections are idempotent, scoped
to the mission scene, and do not replay random ambient flicker.
Chat visibility uses a new preference key with a false default.

The syncfix.10 build succeeds with zero errors (78 existing/compiler/network
warnings). 49 checks cover codecs, revision conflicts, lifecycle and authority;
8 checks load real local Modernization types without starting Unity. These are
data tests, not two-player simulation. An isolated installer fixture verifies
replacement/backup of renamed co-op assemblies, missing dependencies, duplicate
assemblies, wrong hashes and preservation of a separate HDR sentinel. Manifest
schema validation rejects incomplete entries rather than comparing empty hashes.
Local deployment/startup and two-PC gameplay are separate validation stages.

The optional reflection contract can change between Modernization releases.
Safe/pairing/chip interactions and every private UI command have not been
implemented or proven by the data bridge. Check F1/F2/F3 projections, selection,
measurement/note edits, undo/redo, both-player simultaneous edits, queue
start/pause/auto-fire and host-only physical effects in a real two-PC session.
Do not infer support for untested functions from matching model files.

- Two-PC end-to-end testing is incomplete; earlier test builds had major failures.
- Confirm each drawing tool, measurement label/note, late join, and reconnect.
- Native map restore, late join, and generated photo restoration need two-PC
  runtime validation. Mission progression/graphs are not restored by map transfer.
- Calculator card creation, movement, and closure are implemented but need
  two-PC runtime testing. Explicit local deactivation of a previously active
  printed card is treated as closure; unrelated parent visibility is ignored.
- Verify counter-battery start/pause/expiry and exactly one host-driven impact;
  the current spawn path requires exactly one active cinematic spawner.
- Verify both guns, loading, trajectory, impact, engine, and power switch.
- BepInEx compilation and all Modernization modules together remain unverified.
- This is a modified Open Nest fork, not a port of Jager2251's separate mod.

Do not describe this branch as stable or fully synchronized until those checks pass.
