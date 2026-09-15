# ADR 0017: Stopped-server local backup publication and recovery

## Context

Issue #41 extends the Agent filesystem trust boundary from process supervision/log
tailing to recursive reads and archive writes. Command delivery is at least once and
the Agent may crash between producing an archive and acknowledging its result.
Cross-cutting #54 therefore applies to file authorization, concurrency, consistency,
durable artifact identity and PostgreSQL result atomicity.

## Decision

- Support only Project Zomboid's entire dedicated stored cachedir, never a path or
  archive name supplied in a command request. Destination comes only from local Agent
  configuration. Reject traversal, volume roots, UNC paths, overlapping roots and
  reparse points; reject Windows source hard links using the opened file handle.
  Trusted ACLs on roots/ancestors are a precondition: path checks do not protect
  against a privileged local actor changing directory entries between checks.
- Require fresh online/stopped state at enqueue and verify actual process absence
  on the Agent before and after copying. Existing sequential process execution gate
  excludes managed Start/Stop. No implicit stop/restart, online snapshots or quiescing
  protocol. Operator-controlled out-of-band writers must remain stopped.
- Reuse `CreateBackup` in the persisted typed-command pipeline. Create a one-to-one
  backup row keyed by command ID in the same transaction, retaining active-command
  uniqueness. Start/fail transitions update both rows together. Completion locks
  the command and commits its final status plus validated artifact metadata together.
- Use bounded streaming ZIP creation, source rescan, deadline and cancellation.
  Publish only a flushed, read-verified `.partial` through same-directory no-overwrite
  rename. A per-command exclusive lock serializes local duplicate executions. A
  constant-size internal manifest binds command, server, source identifier and an
  ordered content digest; the API receives only size and whole-archive SHA-256.
- On recovery, reuse a valid final archive. Interrupted partials are rebuilt. Exact
  metadata retries succeed; conflicting results fail. Failed/cancelled partial work
  never becomes a successful metadata record. Host cancellation leaves command
  recovery to the existing polling lifecycle.
- Owner-only paginated metadata and a basic manual Web control; no archive transfer.
  No dependencies beyond the framework, no second queue or scheduler.

## Alternatives and consequences

Online ZIP creation without quiescing can produce an inconsistent save. VSS or a
game-specific snapshot protocol would expand this slice substantially and is deferred.
Timestamp-named archives would duplicate work after lost acknowledgements, so command
IDs are durable artifact identities. Database-first success cannot prove a readable
local file, while filesystem publication alone cannot provide owner history: the
chosen order plus recovery reconciles both without a distributed transaction.

The process-name absence check is conservative and may reject an unrelated Java
process. A long backup blocks other managed process operations on that Agent while
heartbeats continue. Storage grows until operator cleanup; retention is deferred.
Size/mtime rescans cannot prove absence of a malicious writer that restores timestamps,
and local administrators can replace archives: least-privilege identity and private
ACLs remain required. This is file-data backup, not preservation of ACLs/empty dirs.

## Verification evidence

`LocalBackupCreatorTests`, `AgentCommandExecutorTests`, `LocalBackupTests` and
`server-backups.test.tsx` cover bounded file creation, readable/corrupt archives,
replay, partial cleanup, cancellation, process gating, ownership, assigned-Agent
completion, exact-result idempotency, concurrent enqueue and bounded history. The
existing full verification script remains the release/commit gate. Operator setup
and limitations are recorded in [local-backups.md](../local-backups.md).
