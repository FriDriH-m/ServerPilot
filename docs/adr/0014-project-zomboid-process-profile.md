# ADR 0014: Bounded Project Zomboid process profile

## Context

Issue #38 is the first supported local server whose vendor entry point is a Windows batch
file and whose durable process is a Java child. Passing the generic stored arguments to
`cmd.exe`, tracking the batch wrapper, or killing Java without a full identity check would
expand the process-execution trust boundary and break restart reconciliation.

Project Zomboid also needs console input for its recommended graceful shutdown. The API must
still preserve ownership, avoid exposing local paths in list/history responses and keep the
generic native-executable behavior unchanged.

## Decision

- Persist an explicit `Generic` or `ProjectZomboid` profile. Existing rows migrate to
  `Generic`; a database constraint requires a data directory only for Project Zomboid.
- Accept only the canonical `StartServer64.bat`, its own directory as working directory,
  process name `java`, empty custom arguments and an explicit absolute data directory. Reject
  shell metacharacters in paths crossing the command-interpreter boundary. Version one fixes
  the server name to `servertest`.
- Immediately before start, require the launcher, bundled `jre64\bin\java.exe`, data directory
  and `Server\servertest.ini`. Read at most 64 KiB of launcher content and require a
  non-comment `zombie.network.GameServer` line that forwards `%1` or `%*`.
- Invoke only `%SystemRoot%\System32\cmd.exe /d /s /c` with the canonical batch path and one
  generated, quoted `-cachedir=<data>` argument. No command payload or stored free-form game
  arguments reach the shell.
- Discover only a descendant whose executable is the exact bundled Java path and persist its
  PID, start time, path, name and profile. Use the same complete identity for restart
  reconciliation, inspection and termination.
- While the originating standard-input stream exists, send `save`, wait five seconds, send
  `quit`, wait at most 60 seconds, then use the existing identity-checked forced fallback with
  a 10-second bound. After Agent restart, standard input cannot be recovered, so use the
  bounded fallback after re-verifying the Java identity.
- Expose derived config/log/save paths only in the owner details contract. The authenticated
  target Agent receives the launch configuration; list and history contracts remain path-free.

## Alternatives considered

- Permit arbitrary `.bat` paths and stored arguments: rejected because it would turn
  ServerPilot into remote shell execution.
- Copy or rewrite the vendor launcher: rejected because Steam updates and local operator
  changes would create a second untracked launcher and unclear update ownership.
- Launch `java.exe` directly: rejected because the vendor batch owns classpath, native-library
  and JVM setup that differs across supported game builds.
- Track `cmd.exe` or Java by name only: rejected because the wrapper is not the server and a
  shared process name is insufficient identity evidence.
- Add RCON solely for shutdown: rejected because it adds credentials, networking and a new
  protocol when inherited standard input handles the supported same-session case.
- Support arbitrary `-servername` values now: deferred to keep shell input and derived paths
  fixed and auditable.

## Consequences

- Generic `.exe` supervision and its timeouts remain unchanged.
- The Project Zomboid profile is intentionally coupled to a small verified vendor layout and
  fails with actionable codes if that layout changes.
- A real server is identified correctly even though it is started by a batch wrapper, and
  Agent restart does not create a duplicate process.
- A reconciled server loses graceful console input after Agent restart and may require forced
  termination. This limitation is explicit in the setup guide and real-server checklist.
- Adding another script-based server requires its own bounded profile and architecture review;
  this decision does not create a generic script runner.

## Verification evidence

- Domain, Agent configuration and API integration tests cover profile defaults, prohibited
  arguments/metacharacters, ownership-preserving contracts and path disclosure.
- PostgreSQL integration tests cover persistence and atomic command delivery of profile data.
- A Windows fixture launches a canonical batch, discovers the exact child `java.exe`, tracks
  that identity and exits through `save`/`quit` without forced termination.
- Existing generic supervisor tests remain unchanged and prove the original `.exe` behavior.
- The real-server checklist is in `docs/project-zomboid-server.md`; real Project Zomboid
  validation remains an explicit draft-PR limitation until performed on a disposable world.
