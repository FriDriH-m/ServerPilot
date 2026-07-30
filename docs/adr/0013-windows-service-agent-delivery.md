# ADR 0013: Windows Service delivery and service-identity storage

## Context

Issue #37 moves the Windows Agent from a development-only console Worker to a persistent
background installation. The choice changes the Windows identity that decrypts the Agent
credential and accesses server files. It also introduces durable configuration, executable
replacement, service recovery and log-location boundaries. Running as `LocalSystem` or granting
broad disk access would turn the existing narrow process supervisor into a privileged host
automation boundary.

## Decision

- Use the official `Microsoft.Extensions.Hosting.WindowsServices` integration. Its context-aware
  lifetime activates only under SCM, so the same executable keeps the console development mode.
- Perform registration/bootstrap inside the hosted worker. SCM can start the host before a network
  round trip, while its stop signal cancels bootstrap, heartbeat, polling and reconciliation.
- Publish a self-contained, single-file `win-x64` application. Package only binaries, the
  administrative management script and operator documentation; never package deployment secrets.
- Install delayed-auto service `ServerPilot.Agent` under the passwordless virtual account
  `NT SERVICE\ServerPilot.Agent`, not `LocalSystem`, `LocalService` or an interactive user.
- Keep service configuration and credential under `%ProgramData%\ServerPilot\Agent`; keep console
  storage unchanged under `%LOCALAPPDATA%\ServerPilot`. Protect both with DPAPI `CurrentUser` so a
  credential cannot move between the interactive user and the service identity.
- Remove inherited ACLs from service application/data directories. Permit `SYSTEM` and local
  administrators full control, service SID read/execute on binaries and modify on its data.
- Grant the service SID `Modify` only to explicitly named, existing server directories. Reject
  relative paths, drive/share roots and the Windows directory. Never grant a whole machine by
  default and never alter the stored per-command executable boundary.
- Supply the one-time installation token as a PowerShell `SecureString`. Persist it only briefly in
  the restricted service configuration, start the service, wait for its DPAPI credential, then
  atomically remove the token. Never put the permanent credential in configuration.
- Configure SCM delayed automatic startup and bounded recovery restarts after unexpected failure.
  Use the Application Event Log source `ServerPilot.Agent`; the elevated installer creates it.
- Update through a stopped-service staging/backup swap with rollback. Preserve ProgramData,
  credentials and every server directory across update and uninstall.

## Alternatives considered

- Run as `LocalSystem`: rejected because local machine authority is unnecessary for managing
  explicitly configured server directories and magnifies a compromised Agent.
- Reuse the interactive user's console credential: rejected because DPAPI `CurrentUser` correctly
  prevents a different Windows principal from decrypting it. Service migration requires a new
  one-time token and revocation of the old Agent.
- Use DPAPI `LocalMachine`: rejected because any local principal able to read the blob could ask
  machine DPAPI to decrypt it. A stable virtual identity plus `CurrentUser` and ACLs is narrower.
- Store a service password or require a named user: rejected because a virtual account is managed
  by Windows and needs no password lifecycle. Remote shares may instead grant the machine account.
- Stop all managed servers when SCM stops the Agent: rejected because service maintenance must not
  silently perform user server commands. Restart reconciliation already validates persisted process
  identity before adoption or signalling.
- Implement remote automatic updates or code signing: outside issue #37. Package provenance and
  code-signing remain operator responsibilities until certificates and an update trust model exist.

## Consequences

- The service can restart after reboot without an installed .NET Runtime and without repeating
  registration. Deleting/recreating it with the same name preserves the deterministic service SID
  and ProgramData, but changing its identity requires re-registration.
- Administrators can read service configuration and can run code as the service identity; local
  administrator compromise remains outside DPAPI's protection. Non-administrative users have no ACL.
- Every managed server directory requires an explicit permission decision. A directory granted
  `Modify` is inside the local process-execution trust boundary, so operators should dedicate the
  smallest practical root and protect package/configuration write access.
- SCM recovery covers unexpected process failure, not an intentional stop or a fatal authentication
  condition that shuts the host down normally. Operators must fix revoked credentials or invalid
  configuration rather than create an infinite retry loop.
- Uninstall removes service registration and binaries but deliberately leaves configuration,
  credential, logs, server data and service-SID ACLs for recoverability. Credential revocation and
  final data/ACL cleanup are explicit operator actions.

## Verification evidence

- Unit tests distinguish console and service credential paths without invoking DPAPI on Linux CI.
- The canonical verification publishes the self-contained `win-x64` package and parses the bundled
  administrative PowerShell before existing build, format, unit, integration, migration and Compose
  checks complete.
- The operator guide defines clean-install, reboot, update, credential-preservation, console-mode
  and uninstall checks. Actual SCM installation requires an elevated Windows test host and is not
  claimed by cross-platform CI.
