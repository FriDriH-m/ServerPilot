# ADR 0005: One active command per ServerInstance

## Context

Issue #24 lets a user persist `StartServer` and `StopServer` commands for an owned
`ServerInstance`. Opposing commands created concurrently would leave the future Agent
with an ambiguous local-process instruction. Checking for an active command in
application code before writing is insufficient: concurrent requests can both observe
that no active command exists.

## Decision

- Treat `Pending`, `Claimed` and `Running` as active command states.
- Enforce one active command per `ServerInstance` with a PostgreSQL partial unique
  index on `server_commands(server_instance_id)` for those states.
- Create a command only after an owner-scoped ServerInstance query through
  `ServerInstance -> Agent -> User`. Missing and foreign resources both yield `404`.
- Translate only a unique-constraint violation from that named index into the expected
  `409 Conflict`; other database failures continue through normal error handling.
- Order command history by `created_at DESC, id DESC`, matching the supporting index.

## Alternatives considered

- Application-only read then insert: rejected because it does not prevent a race.
- A serializable transaction for every command request: rejected because the partial
  unique index expresses the invariant directly and needs no retry protocol here.
- Queue both commands and let the Agent resolve conflicts: rejected because it makes
  process intent ambiguous before an Agent claims either command.

## Consequences

- A terminal command immediately permits the next Start or Stop request.
- The API exposes a predictable `409` for concurrent or conflicting active requests,
  while the database remains authoritative if several API instances are running.
- A ServerInstance with command history cannot be deleted. This preserves history and
  turns the existing restrictive foreign key into an explicit `409` rather than a
  database error response.
- The first version intentionally does not define cancellation or automatic retries;
  those are future command-processing work.

## Verification evidence

- PostgreSQL integration tests issue concurrent Start and Stop requests and verify
  exactly one persisted pending command.
- API integration tests cover owner scoping, foreign-resource `404`, conflict handling,
  newest-first pagination and omission of raw failure messages from history responses.
