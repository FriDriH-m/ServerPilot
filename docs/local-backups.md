# Local backups through the Windows Agent

Issue #41 adds manual local ZIP creation for the Project Zomboid profile. It does
not add restore, download, remote storage, schedules or automatic retention.

## Consistency and setup

1. Give each server its own dedicated `DataDirectory` (`-cachedir`). The backup
   includes all regular files underneath it: saves, Server configuration and logs.
   Empty directories, ACLs, alternate streams and filesystem metadata are not
   preserved. This is a file-data archive, not a volume image.
2. Stop the server and wait for a fresh `Stopped` report from its online Agent.
   Backups never stop or restart the server implicitly. Do not launch it manually
   or let mods, editors or another service write the cachedir during a backup.
3. Create a separate local destination directory, outside the cachedir and its
   ancestors. Grant the Agent's service identity read access to the cachedir and
   Modify access to the destination. Keep both roots and their ancestors writable
   only by trusted local operators/the service. Do not run the Agent as LocalSystem.
   Do not use a volume root, UNC share, symlink, junction or a shared data directory.
4. Configure the Agent (JSON section or equivalent environment variables):

```json
{
  "Backups": {
    "RootDirectory": "D:\\ServerPilotBackups",
    "MaximumSourceBytes": 8589934592,
    "MaximumEntries": 100000,
    "TimeoutSeconds": 1800
  }
}
```

`RootDirectory` is unset by default, so backup commands fail closed until explicitly
configured. For environment configuration use `Backups__RootDirectory`, etc. Limits
are validated at startup: source size 1 byte–100 GiB, entries 1–100,000, deadline
1–3,600 seconds. Default source cap is 8 GiB. Directories count toward the entry
limit; maximum depth is 64 and each relative entry name is at most 1,024 characters.
Archive size is also capped at 100 GiB. Leave free space for the archive: compression
does not guarantee a smaller file. Disk/permission/locked-file errors fail the command.

## Owner workflow and API

Open a Project Zomboid server in Web, choose **Create local backup** and confirm.
Use **Refresh backups** to load the latest metadata; **Older backups** replaces the
displayed page with the next bounded page. Backup history is not automatically polled.
Command history continues to show progress through its existing refresh loop.

- `POST /api/server-instances/{id}/commands/backup` — queue `CreateBackup` (201).
  No source/destination/command arguments are accepted. Missing/foreign server: 404;
  active command or unsupported/stale/non-stopped server: 409.
- `GET /api/server-instances/{id}/backups?limit=20&cursor=...` — owner-only metadata,
  `items` and `nextCursor`; limit 1–100. Metadata includes backup/command ID, status,
  UTC creation/start/completion timestamps, final size, SHA-256 and safe error code.
  Paths, archive contents and raw Agent exception messages are never returned.
- The assigned Agent uses the existing claim/start/fail routes, then
  `POST /api/commands/{id}/complete-backup` with `sizeBytes` and a 64-character
  hexadecimal `checksum`. Generic `/complete` cannot complete a running backup.

Command and backup creation share one PostgreSQL transaction and the existing
per-server active-command constraint. Backup statuses are `Pending`, `Running`,
`Completed` and `Failed`; command timestamps supply the matching metadata. Terminal
result and artifact metadata are committed atomically. An exact duplicate result is
accepted; different metadata after completion is rejected with 409.

## Files, failures and recovery

The destination is `<RootDirectory>/<ServerInstanceId:N>/<CommandId:N>.zip`.
The Agent verifies the stopped process before and after copying. Because process
rediscovery alone is not a complete proof of absence, it also conservatively rejects
creation if any process with the configured process name exists (normally `java`).
An unrelated Java process can therefore block backup; use an isolated Agent host.
The existing Agent execution gate prevents managed Start/Stop from overlapping the
copy. This is stopped-server consistency, not an online snapshot guarantee.

The writer streams bounded buffers into `<CommandId:N>.partial`, checks file sizes
and modification times again, closes/flushes the ZIP, reads back its contents, verifies
an internal identity/content-checksum manifest and computes the whole-file SHA-256.
Only then does a no-overwrite rename publish `.zip` in the same directory. Source
reparse points and, on Windows, hard links are rejected. An exclusive `.lock` file
prevents concurrent local execution of the same command.

- A crash before publication leaves only a partial artifact. Recovery removes that
  exact partial file and rebuilds; it does not append to it.
- A crash/lost response after publication reopens and validates the same archive,
  then reports the same checksum and size without taking another backup. The current
  source may have changed since publication; the immutable published backup is reused.
- Corrupted/mismatched final archives fail closed and are not silently overwritten.
- Deadline expiry reports `BackupTimedOut`. Host shutdown propagates cancellation,
  cleans partial output where possible and leaves the running command recoverable.
- File errors report `BackupFileOperationFailed`; unverifiable/running processes
  report `BackupRequiresStoppedServer`; failed final state validation reports
  `BackupSourceChanged`. No partial file is reported as a successful backup.
- Cleanup failure is logged without a sensitive path. Operators may remove the exact
  `.partial` file when no execution holds its `.lock`. Empty `.lock` files remain and
  are harmless. Do not remove a published ZIP while its result is awaiting delivery.

There is deliberately no archive retention policy in this issue. Operators must
budget disk space and maintain private destination ACLs. Deleting archives manually
does not change historical database metadata. A database migration is included; do
not roll it back after creating backup commands without an explicit data migration.

## Verification

Regression tests cover ZIP contents/checksums, corrupt replay, process state, resource
limits, cancellation, file locks, directory links, interrupted writes, publication
failure and lost-result retry. PostgreSQL/API tests exercise ownership, assigned-Agent
authorization, state transitions, metadata validation, transaction uniqueness and
keyset history. Web tests cover manual refresh, paging, metadata and disabled actions.
These automated fixtures do not substitute for an operator's restore rehearsal on
real game data; restore execution is a separate issue.
