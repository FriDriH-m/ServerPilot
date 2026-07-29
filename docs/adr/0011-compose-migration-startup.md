# ADR 0011: Compose one-shot migration startup

## Context

Issue #32 makes the API and PostgreSQL reproducible with Docker Compose. The API cannot
serve the MVP safely before its PostgreSQL schema is current. Applying migrations inside
every API replica would make a future scale-out race possible, while an undocumented manual
command is easy to skip on a clean local volume. A readiness endpoint must distinguish a
running API process from a usable database and current schema.

## Decision

- The API Dockerfile has a shared SDK `migrate` target that restores the existing local
  `dotnet-ef` tool and runs `database update` through Infrastructure's design-time
  DbContext factory.
- Compose runs `migrate` once after PostgreSQL is healthy. The API depends on successful
  completion of that service and uses no restart loop for migration failures.
- The runtime API image stays multi-stage and non-root. It adds `curl` solely for Docker's
  in-container readiness health check; no application package is introduced.
- `/health/live` is process-only. `/health/ready` and `/health` require reachable
  PostgreSQL with no pending EF Core migrations. Compose exposes the API on loopback by
  default and marks the service healthy only through `/health/ready`.
- PostgreSQL uses a named volume. Normal shutdown preserves it; the documented
  `docker compose down --volumes --remove-orphans` is the explicit destructive local reset.

## Alternatives considered

- Apply migrations from API startup: rejected because it couples schema changes to every
  API process and becomes unsafe when replicas are introduced.
- Require a manual `dotnet ef database update`: rejected for Compose startup because a
  clean volume can otherwise leave a running but unready API through operator omission.
- Retry the migration container indefinitely: rejected because it hides schema failures and
  produces misleading operational state.
- Use a separate migration project or production deployment tool: deferred because the
  current MVP has one API and one PostgreSQL service; the shared Dockerfile target keeps the
  smallest reproducible local boundary.

## Consequences

- A Compose startup has an observable migration phase. A failed migration prevents API
  startup until an operator diagnoses and explicitly retries it.
- `eng/verify-compose.ps1` starts an isolated project with generated process-only secrets,
  proves readiness and applied migrations, resets its volume, then repeats the clean start.
- The Windows Agent stays outside Compose and may use loopback HTTP only in local
  development; non-loopback deployment still requires HTTPS.
- Future production rollout or multiple API replicas need a deployment-specific migration
  coordinator rather than relying on this local Compose workflow.

## Verification evidence

- `docker compose config --quiet` validates required environment configuration.
- `eng/verify-compose.ps1` verifies migration completion, API liveness/readiness and a
  volume-removing clean reset.
- `eng/verify.ps1` runs the Compose verification alongside build, formatting, unit tests,
  PostgreSQL integration tests and migration-model drift checks.
