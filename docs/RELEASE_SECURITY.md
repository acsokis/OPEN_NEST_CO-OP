# Release hardening

The syncfix.11.1 release hardens the execution path. It does not
certify this software or any third-party dependency as malware-free.

## Implemented controls

- The published download CMD must contain a build-pinned ZIP SHA256, in addition
  to the GitHub asset digest. An unresolved source template refuses to run.
- Release identity and the GitHub download URL are fixed for the intended build.
- Before extraction, the entire ZIP directory is checked against exact file
  names. Duplicate names, traversal, absolute/UNC paths, alternate data streams,
  device names, symlinks and unexpected entries are rejected.
- Extraction bounds entry count, per-file and total uncompressed size, checks
  actual streamed byte counts and requires an empty destination.
- The installer checks complete required payload membership, hashes, unexpected
  files and destination paths before replacing payload files. Reparse points and
  junctions in source/destination paths are rejected.
- Backup folders use unique names. Renamed co-op duplicates are preserved rather
  than deleted. The installer does not add persistence or modify security tools.
- The self-contained offline wrapper pins its embedded ZIP hash and uses the
  same extraction checks. Both wrapper forms preserve failure exit codes.

## Verification as of 2026-10-31

Thirty-one defensive tests pass, including malformed paths, incomplete/duplicate
manifests, altered hashes, unexpected payloads, ZIP links, archive size limits,
duplicate archive names and actual junction rejection. The local MelonLoader
build succeeds with zero errors. Full package generation and archive validation
passed. An isolated installer fixture verified all nine payload hashes, duplicate
backup and HDR/save sentinel preservation. Installation in the local game passed
the file checker with a backup. Public ZIP/CMD digests, the embedded updater ZIP
pin and updater script syntax were verified after download. A local Microsoft
Defender custom ZIP scan reported no threats. No files were uploaded to an
external malware-scanning service. Older syncfix.11 packages predate these checks.

## Trust boundaries

A compromised updater CMD can change its own code and hash: pinning protects a
trusted updater against later asset replacement, not against an initially
malicious updater. No code-signing certificate or independent signing key has
been configured. Local same-user concurrent tampering is not prevented by
filesystem locks across the entire installation transaction. Backups do not
provide an automatic rollback transaction if installation is interrupted.

This is a targeted package/update audit, not a complete audit of every gameplay
network handler, Unity, MelonLoader or dependency. A game peer should not be
treated as trusted simply because package checks passed. Scanner results and
independent-PC gameplay checks must be reported separately when performed.

`Install.ps1 -VerifyOnly` suppresses interactive dialogs; its historical name does
not mean a dry run. Use an isolated fixture when testing installation behaviour.
