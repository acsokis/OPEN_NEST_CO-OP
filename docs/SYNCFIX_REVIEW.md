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
- Add experimental FireMissionCard creation using native capture/restore APIs.
- Transfer counter-battery time/running/expired/stopped together, prevent guest
  autonomous timer updates and spawns, and reject guest automatic mission outcomes.
- Add a persistent local F8 chat visibility toggle; hidden chat ignores Enter.
- Retain the engine and teleprinter repair changes from the local test builds.

## Evidence and validation

The original guest log showed shots received at 23:25:02 with a 35-second flight,
but an impact only at 23:28:32. It also showed missing MapMarkerYellow prefabs,
counter-battery spawns with no host seed, and a guest-originated failure event.
Private logs and account identifiers are deliberately excluded from this repo.

Local MelonLoader builds succeeded for syncfix.7 (71 warnings, zero errors).
The installed syncfix.6 reached the main menu. Packaging checks verified payload
hashes and installer parsing. These do not establish multiplayer correctness.
The subsequent token ownership adjustment also builds successfully (71 warnings,
zero errors); its two-PC runtime test remains outstanding.

## Outstanding validation and limitations

- Two-PC end-to-end testing is incomplete; earlier test builds had major failures.
- Confirm each drawing tool, measurement label/note, late join, and reconnect.
- Token messages still skip objects absent from the receiver; a map-only snapshot
  is not a complete transfer of the host's saved mission or generated photo data.
- Calculator card creation is experimental; subsequent card movements/deletion
  are not implemented by the new module, and native JSON restore needs runtime validation.
- Verify counter-battery start/pause/expiry and exactly one host-driven impact;
  the current spawn path requires exactly one active cinematic spawner.
- Verify both guns, loading, trajectory, impact, engine, and power switch.
- BepInEx compilation and all Modernization modules together remain unverified.
- This is a modified Open Nest fork, not a port of Jager2251's separate mod.

Do not describe this branch as stable or fully synchronized until those checks pass.
