# Project Zomboid server profile

Issue #38 adds a deliberately narrow Windows profile for the Project Zomboid dedicated
server. It is a post-MVP extension; the existing `Generic` native-executable profile is
unchanged.

## Supported layout

Install **Project Zomboid Dedicated Server** with Steam or SteamCMD and keep the vendor
layout intact. ServerPilot requires these files relative to the selected launcher:

```text
<install>\
├── StartServer64.bat
└── jre64\bin\java.exe
```

The launcher must be named exactly `StartServer64.bat`, be at most 64 KiB and contain a
non-comment command that launches `zombie.network.GameServer` while forwarding `%1` or
`%*`. This check prevents an unrelated batch file from being selected as the profile
launcher. Steam may replace the launcher during an update, so revalidate the profile after
updating the dedicated server.

Choose a dedicated absolute Windows data directory. The first profile version supports the
canonical server name `servertest` only and requires this file before start:

```text
<data>\Server\servertest.ini
```

Create the `servertest` settings with Project Zomboid's Host UI or a controlled interactive
first run, stop it cleanly, then copy the resulting `Server` files and any existing save into
the chosen data directory. Do not leave an interactive admin-password prompt for the Windows
Service Agent.

When the Agent runs as a Windows Service, grant its virtual service account `Modify` only on
the dedicated install and data directories. The install script can grant the install root:

```powershell
.\eng\windows-agent\Install-ServerPilotAgent.ps1 `
  -PackageDirectory C:\Packages\ServerPilot.Agent-win-x64 `
  -ApiBaseUrl https://serverpilot.example.com `
  -InstallationToken (Read-Host -AsSecureString) `
  -ManagedServerDirectory C:\Servers\ProjectZomboid
```

Grant the same service identity access to a separate data root when it is outside that
managed directory. See [Windows Service Agent](windows-agent-service.md) for the service
identity and ACL rules. Never grant a drive root, share root or Windows directory.

## Create the ServerInstance

Select these values in the Web dashboard:

| Field | Value |
| --- | --- |
| Profile | `ProjectZomboid` |
| Executable path | `<install>\StartServer64.bat` |
| Data directory | the explicit `<data>` directory |
| Arguments | empty and managed by the profile |
| Working directory | derived from the launcher directory |
| Process name | fixed to `java` |

ServerPilot passes exactly one quoted game parameter to the vendor launcher:
`-cachedir=<data>`. Paths crossing the `cmd.exe` boundary reject shell metacharacters. The
profile does not accept a command string, custom batch name, custom JVM arguments or a
custom server name.

The API details view exposes the derived operational paths:

```text
<data>\Server\servertest.ini
<data>\Server\servertest_SandboxVars.lua
<data>\Server\servertest_spawnpoints.lua
<data>\Server\servertest_spawnregions.lua
<data>\Logs
<data>\console.txt
<data>\Saves\Multiplayer\servertest
```

List and command-history responses still omit local paths. Only the owner details response
and the authenticated target Agent receive the full configuration.

## Start, discovery and stop behavior

The Agent validates the launcher, bundled Java executable, data directory and
`Server\servertest.ini` immediately before launch. Failures use stable actionable codes such
as `ManagedExecutableNotFound`, `DataDirectoryNotFound`,
`ProfileConfigurationNotFound` and `InvalidLauncher`.

The Agent starts the restricted vendor batch through Windows `cmd.exe`, then searches only
its descendant process tree for the exact bundled `jre64\bin\java.exe`. The tracked identity
is the real Java PID, start time, executable path and process name—not the short-lived batch
wrapper. Periodic reconciliation can rediscover and verify that identity after an Agent
restart.

For a server started in the current Agent session, `StopServer` writes `save`, waits five
seconds, writes `quit`, and waits up to 60 seconds for Java to exit. If graceful shutdown is
unavailable or times out, the Agent uses the existing identity-checked forced fallback and
waits at most another 10 seconds. Standard input cannot be reattached after an Agent restart;
therefore a reconciled process can be stopped only by the bounded identity-checked fallback.

## Manual validation with a real server

Automated tests use a harmless fixture that reproduces the batch-to-child-process and
standard-input behavior. Before promoting the PR from draft, validate a real current
Project Zomboid installation on a disposable world:

1. Confirm the dedicated server starts manually with the chosen `-cachedir` and exits after
   `save`, then `quit`.
2. Create the profile and queue `StartServer`; verify the reported PID belongs to the bundled
   `jre64\bin\java.exe`, the server becomes joinable and `console.txt` is written under the
   selected data directory.
3. Restart the Agent without stopping the server; verify it reports the same Java PID as
   `Running` rather than starting a duplicate.
4. Queue `StopServer`; verify shutdown completes and the save files advance. Repeat after an
   Agent restart and confirm the documented forced-fallback behavior.
5. Temporarily select a missing Java executable, data directory, configuration file and a
   modified launcher that no longer forwards arguments; verify the corresponding actionable
   failure code and restore the files.

## Current limitations

- Windows dedicated server only; Linux launch scripts are not supported.
- Only the canonical `servertest` configuration is supported.
- No custom JVM/game arguments, RCON, mod management, update automation or live log streaming.
- Graceful console shutdown is available only while the Agent owns the launcher's standard
  input stream; an Agent restart loses that stream.
- Vendor launcher/layout changes can require a profile update. The current assumptions were
  checked against Build 41 guidance and current Build 42 support reports, but a real server
  validation remains required for each deployment.

## Upstream references

- Project Zomboid support confirms `StartServer64.bat` and the default `servertest` launch
  convention: <https://steamcommunity.com/app/108600/discussions/1/3816291265157429548/>.
- Project Zomboid support documents `-cachedir` for moving server data:
  <https://steamcommunity.com/app/108600/discussions/1/5401527630813605168/>.
- The Indie Stone administrator guidance recommends `save`, wait for completion, then `quit`:
  <https://theindiestone.com/forums/topic/70111-safest-way-to-save-and-quit-a-server/>.
- A current Build 42.13.2 shutdown log shows `quit` entering shutdown handling and saving:
  <https://theindiestone.com/forums/topic/91067-42132an-error-occurred-when-using-the-quit-command/>.
