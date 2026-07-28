# ServerPilot Agent Instructions

## Purpose

ServerPilot remotely manages local game and application server processes through a Windows Agent.

Before making changes, read:

- `README.md`;
- `docs/product.md`;
- `docs/mvp.md`.

`docs/mvp.md` defines the current development scope.

Do not implement functionality outside the current MVP unless the task explicitly requires it.

## Current scope

The current goal is a minimal vertical scenario:

```text
User
→ ASP.NET Core API
→ persisted command
→ HTTP polling
→ Windows Agent
→ local process
→ command result
→ API
```

The first supported operations are:

- Agent registration;
- Agent heartbeat;
- ServerInstance creation;
- `StartServer`;
- `StopServer`;
- command history.

The MVP uses HTTP polling and PostgreSQL.

Do not add the following during MVP unless explicitly requested:

- RabbitMQ;
- Redis;
- Kubernetes;
- React;
- Grafana;
- Prometheus;
- Loki;
- MinIO;
- microservices;
- backups;
- schedules;
- notifications.

## Technology

- .NET 10;
- ASP.NET Core;
- Entity Framework Core;
- PostgreSQL;
- .NET Worker Service;
- xUnit;
- Testcontainers;
- Docker Compose.

Do not add a new package without explaining why it is necessary.

Prefer framework functionality over an additional dependency when the framework solution is sufficient.

## Solution structure

Expected projects:

```text
src/
├── ServerPilot.Domain
├── ServerPilot.Application
├── ServerPilot.Infrastructure
├── ServerPilot.Api
└── ServerPilot.Agent

tests/
├── ServerPilot.UnitTests
└── ServerPilot.IntegrationTests
```

## Dependency rules

Allowed dependencies:

```text
Application → Domain
Infrastructure → Application
Infrastructure → Domain
Api → Application
Api → Infrastructure
```

`Domain` must not reference:

- ASP.NET Core;
- Entity Framework Core;
- PostgreSQL;
- RabbitMQ;
- file system implementations;
- Windows-specific APIs;
- Infrastructure;
- Api.

`Application` must not depend on concrete Infrastructure implementations.

`Api` is the composition root for the backend.

`Infrastructure` contains:

- EF Core;
- PostgreSQL;
- authentication implementations;
- persistence;
- external integrations.

`Agent` is a separate executable and must not reference backend Infrastructure.

Shared contracts should only be extracted when both sides genuinely need the same stable contract. Do not create a large shared project prematurely.

## General development rules

- Prefer simple code over speculative abstractions.
- Do not introduce microservices.
- Do not create abstractions for possible future requirements.
- Do not add technologies that are not required by the current task.
- Do not refactor unrelated code.
- Keep changes focused on one task.
- Preserve existing public behavior unless the task explicitly changes it.
- Use clear names instead of excessive comments.
- Avoid large methods and classes with multiple responsibilities.
- Use nullable reference types.
- Treat compiler warnings as issues to investigate.
- Use UTC for stored timestamps.
- Use `DateTimeOffset` for externally meaningful timestamps.
- Pass `CancellationToken` through asynchronous operations.
- Do not use `.Result`, `.Wait()` or other sync-over-async patterns.
- Do not swallow exceptions.
- Do not catch `Exception` unless there is a clear boundary and the error is logged or converted appropriately.
- Do not use exceptions for normal business control flow.
- Validate all external input.
- Never store secrets in source code.
- Configuration must come from environment variables, user secrets or configuration files with safe defaults.

## Domain rules

Domain entities must protect their important invariants.

Do not expose unrestricted public setters when invalid state can be created.

Use value objects only when they provide real validation or behavior. Do not wrap every primitive automatically.

Business rules must not be placed in controllers or EF Core configurations.

Avoid anemic entities when the entity owns meaningful state transitions.

Do not create a generic repository abstraction over EF Core.

Use application-specific abstractions only when they simplify testing or separate Infrastructure.

## API rules

- Controllers or endpoints must remain thin.
- Authentication is not authorization.
- Every operation must verify resource ownership.
- Never trust a resource identifier supplied by the client without checking ownership.
- Use appropriate HTTP status codes.
- Return a consistent error response.
- Do not expose internal exceptions or stack traces.
- Support cancellation through `HttpContext.RequestAborted`.
- Validate request models before performing changes.
- Do not return EF Core entities directly from endpoints.
- Use explicit request and response contracts.

## Persistence rules

- Use PostgreSQL as the source of truth.
- Use explicit EF Core entity configurations.
- Add database constraints for important invariants where possible.
- Add indexes based on actual queries and uniqueness requirements.
- Do not rely only on application validation for uniqueness.
- Use migrations for schema changes.
- Do not modify an existing applied migration unless explicitly requested.
- Avoid loading full collections when a projection is sufficient.
- Use `AsNoTracking` for read-only queries.
- Be explicit about transaction boundaries.
- Consider concurrent requests when changing command state.

## Command processing rules

Supported MVP commands:

```text
StartServer
StopServer
```

The Agent must never execute arbitrary:

- shell commands;
- PowerShell;
- scripts;
- command strings supplied directly by the backend.

The Agent may only execute a predefined command type against a previously stored `ServerInstance` configuration.

Every command must include:

- command ID;
- target Agent ID;
- target ServerInstance ID;
- command type;
- creation time;
- correlation ID.

Command processing must be idempotent.

### StartServer

If the target process is already running, repeated processing must not start another process.

### StopServer

If the target process is already stopped, repeated processing should complete successfully.

Command state transitions must be validated.

Invalid state transitions must not silently succeed.

Obtaining the next pending command must be atomic. The same command must not be claimed by multiple Agent instances.

Do not assume that network delivery occurs exactly once.

## Agent rules

The Agent initially runs as a console-hosted .NET Worker Service.

Windows Service installation will be added later.

The Agent must:

- use typed configuration;
- validate required configuration at startup;
- log startup and shutdown;
- register using a one-time installation token;
- persist issued credentials securely;
- send heartbeat periodically;
- poll for commands;
- use bounded retry with delay for temporary network errors;
- avoid tight retry loops;
- respect cancellation;
- report command success or failure;
- verify actual process state;
- track the process ID when possible.

The Agent must not accept arbitrary executable paths as part of each command.

Executable path, arguments and working directory belong to the stored `ServerInstance`.

The process execution component should be isolated behind a small, focused abstraction so it can be unit tested.

Do not create a generic operating-system automation framework.

## Security rules

- Installation tokens must be one-time and time-limited.
- Store token hashes rather than raw installation tokens.
- Agent credentials must be revocable.
- User credentials and Agent credentials are different concepts.
- An Agent must only receive commands assigned to it.
- A user must only manage owned Agents and servers.
- Do not log passwords, tokens, authorization headers or secrets.
- Use HTTPS outside local development.
- Validate file paths.
- Be careful with path traversal.
- Avoid returning full local paths to users unless required.
- Record security-relevant operations in logs or audit records.

## Logging rules

Use structured logging.

Good:

```csharp
logger.LogInformation(
    "Agent {AgentId} claimed command {CommandId}",
    agentId,
    commandId);
```

Avoid:

```csharp
logger.LogInformation(
    $"Agent {agentId} claimed command {commandId}");
```

Include relevant identifiers when available:

- `UserId`;
- `AgentId`;
- `ServerInstanceId`;
- `CommandId`;
- `CorrelationId`.

Do not log entire request objects when they may contain secrets.

## Testing rules

Every bug fix should add a regression test when practical.

Use unit tests for:

- domain rules;
- state transitions;
- validation;
- process execution decisions;
- idempotency behavior.

Use integration tests for:

- HTTP endpoints;
- authorization;
- EF Core mappings;
- PostgreSQL constraints;
- transactions;
- concurrent command claiming.

Integration tests must use a real PostgreSQL instance through Testcontainers or another isolated PostgreSQL environment.

Do not use EF Core InMemory as proof that PostgreSQL persistence works.

Tests must verify behavior, not implementation details.

Avoid tests that only check that a mock method was called without validating the result.

## Workflow before changing code

Before implementation:

1. Read the relevant documentation.
2. Inspect the existing implementation.
3. Identify the smallest required change.
4. Explain the current behavior.
5. Propose a short implementation plan.
6. List the files expected to change.
7. Identify unclear requirements, risks and concurrency concerns.
8. Do not modify code until the task requests implementation.

For a simple and explicit task, a brief plan is sufficient.

## Issue execution gate

Before starting every issue:

1. Read the active issue and GitHub issue #54.
2. Run `git fetch --prune` and verify the previous PR state.
3. Switch to `main`, run `git pull --ff-only` and require a clean worktree.
4. Create the task branch from the current `origin/main`.
5. Verify issue dependencies and reconcile the roadmap's current-state section.
6. State the Cross-cutting #54 result before implementation.

Before committing or opening a pull request:

1. Run `./eng/verify.ps1`.
2. Inspect `git status`, `git diff --stat origin/main...HEAD` and
   `git diff origin/main...HEAD`.
3. Recheck ownership, authorization, concurrency, idempotency and secret handling.
4. Update tests, documentation, ADR/threat model and roadmap when applicable.
5. Verify that no unrelated files are included.

After a pull request is merged:

1. Confirm the implementation issue is closed.
2. Update the roadmap's current-state and recommended-next-step sections.
3. Fetch and fast-forward local `main`.
4. Do not start the next issue from the merged feature branch.

After every three to five completed implementation issues, perform a read-only
milestone audit covering documentation drift, security boundaries, ownership,
concurrency, migrations, CI and roadmap state. Record actionable findings as focused
issues instead of silently expanding the next implementation task.

## Workflow during implementation

- Implement only the requested scope.
- Keep the solution buildable after each logical step.
- Avoid broad formatting or namespace changes.
- Do not rewrite working code without a concrete reason.
- Add or update tests with the implementation.
- Check existing patterns before introducing new ones.
- Do not duplicate an existing abstraction or helper.
- Do not disable analyzers or warnings merely to make the build pass.

## Workflow after implementation

After making changes:

1. Run:

```bash
dotnet build
```

2. Run:

```bash
dotnet test
```

3. When formatting is configured, run:

```bash
dotnet format --verify-no-changes
```

4. Inspect:

```bash
git status
git diff --stat
git diff
```

5. Report:

- what was changed;
- why it was changed;
- tests that were added;
- commands that were executed;
- whether they passed;
- files changed;
- remaining risks;
- anything that could not be verified.

Do not claim that the implementation works if build or tests were not executed.

If a command cannot be executed, state that directly.

## Review rules

When asked to review code:

- do not modify files unless explicitly requested;
- prioritize correctness over style;
- report findings by severity;
- reference concrete files and code;
- look for missing tests;
- look for authorization mistakes;
- look for race conditions;
- look for broken state transitions;
- look for insecure process execution;
- look for accidental complexity;
- distinguish confirmed defects from possible risks.

Review severity:

```text
Critical
High
Medium
Low
```

Do not invent findings merely to produce a longer review.

## Git rules

- One task should produce one focused logical change.
- Do not mix refactoring and new behavior without necessity.
- Do not commit generated secrets, local settings or build output.
- Do not modify `.gitignore` without a reason.
- Do not force-push.
- Do not rewrite existing commits unless explicitly requested.
- Do not create a commit unless explicitly requested.
- Before completing work, check for unrelated modified files.

## Commands

Expected repository commands:

```bash
dotnet restore
dotnet build
dotnet test
dotnet format --verify-no-changes
docker compose up -d
docker compose down
```

If these commands become inaccurate, update this file together with the repository configuration.

## Definition of done

A task is complete only when:

- requested behavior is implemented;
- relevant tests exist;
- the solution builds;
- tests pass;
- no unrelated files were changed;
- error cases were considered;
- authorization was checked where applicable;
- documentation was updated when behavior or setup changed;
- remaining limitations were reported.

## Graphify

This repository may use Graphify to maintain a local knowledge graph of the codebase.

When Graphify data is available:

- use it to understand project structure, dependencies and affected components;
- verify important conclusions against the actual source files;
- do not treat the graph as the source of truth;
- regenerate or update the graph after significant structural changes;
- do not modify application architecture merely to improve the graph;
- do not commit generated Graphify data unless the repository explicitly requires it.

## Cross-cutting architecture check

Before implementing or reviewing an issue, check GitHub issue #54.

Determine whether the change:

- introduces or changes a trust boundary;
- changes authentication, credentials, authorization or resource ownership;
- introduces a concurrency, atomicity or idempotency guarantee;
- changes the local process-execution security boundary;
- selects a persistence, messaging, caching or deployment strategy;
- chooses between meaningful alternatives with long-term consequences.

If none apply, state:

`Cross-cutting #54: no update required.`

If any apply, include the smallest relevant ADR, threat-model update or
architecture fitness check in the active issue. Document the context,
decision, alternatives, consequences and verification evidence.

Do not create ADRs for routine implementation details and do not design
post-MVP technologies before their implementation issue becomes active.
