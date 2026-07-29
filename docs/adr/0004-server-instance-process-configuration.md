# ADR 0004: ServerInstance process configuration and ownership

## Context

Issue #22 introduces the first persisted description of a local process that a future
Windows Agent may manage. The API receives paths, arguments and a process name from a
user, but it cannot inspect the remote Agent file system. The configuration therefore
creates both an ownership boundary and the input boundary for later local process
execution. A user must not read or modify configuration attached to another user's
Agent, and an active configuration must not disappear while future command processing
may still refer to it.

## Decision

- Persist `ServerInstance` with its target `AgentId`, process configuration, status,
  optional last process ID and UTC creation/update timestamps. A foreign key binds each
  instance to one Agent.
- Scope every API read, update and delete through the Agent's `UserId`; missing and
  foreign resources both return `404`.
- Accept only bounded, trimmed values. Executable and working-directory values must be
  absolute Windows drive or UNC paths without `.` or `..` segments, device paths are
  rejected after normalizing forward and backslash separators for validation, and
  `ProcessName` is a bare name rather than a path. The API deliberately does not test
  whether the paths exist on the remote computer.
- Keep the Agent association immutable through the update API. Reassignment is not an
  MVP operation because it could change the machine and ownership context of a process
  configuration unexpectedly.
- Return a safe summary from list queries. Full local paths and arguments are returned
  only to the owner from create, get-by-ID and update operations.
- Reject changes to executable path, arguments, working directory or process name while
  the instance has an active process state or a `Pending`, `Claimed` or `Running`
  command. A name-only update remains allowed. Serialize that check with command creation
  by locking the owned ServerInstance row in the same transaction.
- Reject deletion while status is `Starting`, `Running` or `Stopping`. Perform the
  owner and inactive-state predicates in the `DELETE` statement, then distinguish an
  active item from a missing/foreign item with an owner-scoped read.
- Keep `Unknown`, `Starting`, `Running`, `Stopping`, `Stopped`, `Crashed` and
  `Unreachable` as persisted process-state values. This issue does not add an endpoint
  that lets a user set them; an Agent command/result flow will do so later.

## Alternatives considered

- Verify file paths from the API host: rejected because the path belongs to the remote
  Windows Agent and an API-side check would be inaccurate and could expose network
  resources.
- Send executable paths and arguments with every future command: rejected because it
  would turn command delivery into an arbitrary process-execution channel.
- Return full configuration from the list endpoint: rejected because bulk lists would
  unnecessarily disclose local host layout and launch arguments.
- Let a client choose the target Agent during update: rejected because it weakens the
  stable ownership and machine binding of the configuration.
- Delete first and rely on future command handlers to tolerate a missing instance:
  rejected because it creates a race with active process management.

## Consequences

- The API validates shape rather than remote file existence. A future Agent must still
  validate that the stored configuration is safe and executable in its local context.
- The user who owns an Agent can retrieve the full configuration; this is intentional
  because they supplied it, but API logs and list responses do not contain the paths or
  arguments.
- The conditional delete protects against a status becoming active between an earlier
  read and the deletion attempt. A concurrent inactive-to-active transition after a
  successful delete must fail when later command-state work verifies the instance still
  exists.
- The update guard prevents an already-created command from silently changing meaning;
  configuration revisions and command snapshots remain deferred.
- Cascade deletion from Agent to ServerInstance preserves referential integrity. Agent
  deletion is not exposed in the current MVP API.

## Verification evidence

- Domain tests cover path/configuration validation, immutable ownership and process
  state invariants.
- API integration tests cover owner CRUD, foreign-resource `404`, safe list payloads,
  input validation and active-instance deletion conflicts.
- PostgreSQL integration tests verify the migration, owner-query index and state/check
  constraints.
