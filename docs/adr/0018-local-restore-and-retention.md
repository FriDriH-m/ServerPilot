# ADR 0018: Recoverable local restore and acknowledged retention

## Context

Issue #42 introduces replacement of server data and deletion of archives. Cross-cutting
#54 applies to the filesystem boundary, at-least-once commands and database/filesystem
consistency. A lost API response must not restore an old save twice or conceal a partly
finished deletion. Local administrators and operators remain trusted; private ACLs on
the data directory, its parent, backup root and their ancestors are a prerequisite.

## Decision

- Owner-only `RestoreBackup` and `PruneBackups` commands share the existing server row
  lock and active-command uniqueness constraint. Enqueue requires a fresh stopped
  Project Zomboid instance and online Agent. The persisted command contains an immutable
  JSON snapshot of backup IDs, sizes and SHA-256 values selected from that server's
  completed backup rows. Browser requests contain no paths or expected hashes.
- Restore checks actual process absence, size, whole-archive SHA-256, identity manifest,
  ordered content checksum, entry names/count/size/depth and directory/file links.
  The verified archive stays open without write sharing through extraction. Extraction
  uses bounded streaming into a new sibling directory on the same volume as the data.
  Windows device names, alternate streams, case-insensitive duplicates and traversal
  are rejected. Original tree links and hard links are rejected before publication.
- A flushed journal and per-server recovery marker precede extraction. States are
  `Prepared`, `Completed`, `RolledBack`; a remaining recovery marker means operator
  attention is required and prevents managed StartServer/CreateBackup and retention.
  After staging and another process absence check, rename current data to `.original`,
  then rename staging into place. Preserve the old tree; never recursively delete it
  as part of restore or archive retention. A completed receipt prevents reapplication.
- On recovery between renames, move the original back when the destination is absent
  and report `RestoreRolledBack`. If both current and original exist and staging is
  gone, publication already happened: record completion without applying it again.
  Other incomplete layouts fail closed for the documented operator recovery procedure.
  Cancellation propagates before publication. The short rename/receipt sequence does
  not observe cancellation, to avoid unnecessary ambiguity after replacing data.
- Retention is a manual policy application by count (1–1000, Web default 10), not a
  scheduler. Keep newest completed backups by creation time and ID; snapshot at most
  1000 excess/interrupted targets per command. Mark targets `Deleting` in the enqueue
  transaction. Only the assigned running command may acknowledge a listed target as
  `Deleted`; retain checksum/size for audit. Completed metadata never claims a deleted
  archive is restorable. A failed command leaves unresolved targets `Deleting`; the
  next application resumes them even if the keep count has since increased.
- Before deleting an existing file, verify its immutable archive identity and checksum.
  Delete only the exact `<server ID>/<backup ID>.zip`, never enumerate/glob-delete user
  files. A missing target is an idempotent deletion and can be acknowledged on retry.
  Generic completion rejects pruning while any deletion for that server is unresolved.

## Alternatives and consequences

In-place extraction makes rollback uncertain after the first overwritten file. A
staging plus rename design costs additional disk space but preserves the previous data.
An automatic recursive cleanup of recovery copies would add another destructive boundary;
operators instead inspect and explicitly remove exact recovery artifacts after rehearsal.
Two directory renames are not a filesystem transaction, so journal recovery handles the
intermediate state. Flushes and same-volume rename support process-crash recovery; this
is not a guarantee against hardware failure or filesystems that ignore flush semantics.

A distributed transaction with the Agent filesystem is unavailable. `Deleting` plus
per-file acknowledgements exposes uncertainty explicitly; retries reconcile it. Count
retention meets this issue's count-and/or-age scope without introducing clocks or a
background scheduler. More than 1000 deletions requires another confirmed application.
Configuration/root changes and out-of-band launches/writers require operator coordination.
The service account needs Modify access to the data parent for sibling staging/renames;
restored files inherit that parent's ACLs rather than preserving source ACLs.

## Verification

`LocalBackupMaintenanceTests` exercises real temporary-directory restores, retained
originals, exact replay, checksum/traversal rejection, process-state checks, cancellation,
both rename crash windows, selective deletion and lost acknowledgement replay.
`LocalBackupTests` covers PostgreSQL ownership, persisted target snapshots, concurrent
enqueue, Agent-only deletion acknowledgements, incomplete completion and retention retry.
Web tests cover explicit confirmation and count validation. `eng/verify.ps1` remains the
commit/PR gate. See [the operator workflow](../local-backups.md) for manual recovery.
