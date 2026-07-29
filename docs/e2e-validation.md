# Полная проверка MVP end to end

Этот сценарий доказывает вертикальный путь из `docs/mvp.md` на реальном Windows Agent:

```text
User -> API -> PostgreSQL -> HTTP polling -> Windows Agent -> local process -> API
```

В качестве управляемого процесса используется только безопасный
`ServerPilot.ProcessFixture.exe` из тестового проекта. Он ничего не слушает, не читает
файлы и завершается самостоятельно через пять минут, если Agent не остановит его раньше.

## Предварительные условия

- Windows 10/11 с .NET SDK из `global.json`;
- Docker Desktop или совместимый Docker daemon;
- чистый checkout репозитория;
- PowerShell 5.1 или новее;
- отсутствие запущенного ServerPilot Agent в текущем Windows-профиле.

Agent хранит единственный credential текущего пользователя в
`%LOCALAPPDATA%\ServerPilot\agent-credential.dat`. Автоматическая проверка намеренно
останавливается, если файл уже существует: она не заменяет credential установленного Agent.
Для изолированной проверки используйте отдельный Windows-профиль. Не удаляйте рабочий
credential ради теста; вместо этого отзовите его через API либо используйте другой профиль.

## Воспроизводимый прогон

Из корня репозитория выполните:

```powershell
./eng/verify-e2e.ps1
```

Сценарий:

1. собирает solution в `Release`;
2. создаёт уникальный Compose project, чистый PostgreSQL volume, случайные process-only
   PostgreSQL/JWT secrets и свободные loopback-порты;
3. проверяет регистрацию и login пользователя;
4. создаёт одноразовый installation token и запускает настоящий Windows Agent;
5. ждёт registration, heartbeat и `Online`;
6. создаёт ServerInstance для безопасного fixture;
7. выполняет StartServer, проверяет реальный PID и `Running`;
8. повторяет StartServer и доказывает, что PID не изменился;
9. перезапускает Agent без installation token и проверяет rediscovery того же PID;
10. на десять секунд останавливает API, запускает его снова и проверяет восстановление
    heartbeat и сохранённого process state;
11. выполняет StopServer и повторный StopServer;
12. проверяет четыре `Completed` записи с correlation ID в истории;
13. регистрирует второго пользователя и ожидает `404` для чужих Agent,
    ServerInstance, command creation и history;
14. останавливает только созданные процессы, удаляет test credential и уничтожает только
    уникальный Compose project вместе с его volume.

Успешный конец вывода имеет форму:

```text
ServerPilot full MVP E2E verification completed successfully.
AgentId: <guid>
ServerInstanceId: <guid>
Verified process ID: <pid>
Completed commands: <guid>, <guid>, <guid>, <guid>
```

GUID, PID, пароли, JWT signing key и installation token создаются заново на каждом прогоне.
Секреты не записываются в репозиторий и не печатаются. При ошибке несекретные stdout/stderr
Agent сохраняются в указанный `serverpilot-e2e-*` каталог системного temp; cleanup всё равно
пытается удалить созданные credential, процесс, контейнеры, network и volume.

Этот Windows-only прогон не входит в `eng/verify.ps1` и Linux CI: DPAPI credential store и
настоящая проверка Windows process identity требуют Windows. Перед выпуском MVP запускаются
оба сценария:

```powershell
./eng/verify.ps1
./eng/verify-e2e.ps1
```

## Ручной сценарий

Ниже приведены те же шаги без harness. Они полезны для исследования ответов API и логов.
Все пароли и токены в примере живут только в текущем PowerShell process.

### 1. Запуск чистой серверной части

Создайте `.env` по инструкции README, затем:

```powershell
docker compose down --volumes --remove-orphans
docker compose up -d --build
Invoke-WebRequest http://127.0.0.1:8080/health/ready -UseBasicParsing

dotnet build ServerPilot.slnx --configuration Release
$api = "http://127.0.0.1:8080/"
```

Ожидается HTTP `200`; migration container должен завершиться с exit code `0`, API и
PostgreSQL должны быть healthy.

### 2. Пользователь, login и installation token

```powershell
$ownerEmail = "owner-$([Guid]::NewGuid().ToString('N'))@example.test"
$ownerPassword = "Mvp!$([Guid]::NewGuid().ToString('N'))"
$json = @{ email = $ownerEmail; password = $ownerPassword } | ConvertTo-Json

$registered = Invoke-RestMethod `
    -Method Post `
    -Uri "${api}api/auth/register" `
    -ContentType "application/json" `
    -Body $json
$login = Invoke-RestMethod `
    -Method Post `
    -Uri "${api}api/auth/login" `
    -ContentType "application/json" `
    -Body $json
$ownerHeaders = @{ Authorization = "Bearer $($login.accessToken)" }
$installation = Invoke-RestMethod `
    -Method Post `
    -Uri "${api}api/agent-installation-tokens" `
    -Headers $ownerHeaders
```

Registration возвращает `201`, login — `200`, а installation-token creation — `201`.
`$registered.userId` и `$login.userId` должны совпасть. Не печатайте и не сохраняйте
`$login.accessToken` или `$installation.token`.

### 3. Запуск Agent и heartbeat

В отдельном PowerShell окне из корня репозитория:

```powershell
$env:Agent__ApiBaseUrl = "http://127.0.0.1:8080/"
$env:Agent__Name = "mvp-e2e-agent"
$env:Agent__InstallationToken = "<значение $installation.token из первого окна>"
$env:Agent__HeartbeatIntervalSeconds = "1"
$env:Agent__CommandPollingIntervalSeconds = "1"
$env:Agent__ProcessReconciliationIntervalSeconds = "1"
dotnet run --project src/ServerPilot.Agent --configuration Release --no-build
```

В первом окне:

```powershell
Start-Sleep -Seconds 3
$agent = @(Invoke-RestMethod `
    -Uri "${api}api/agents" `
    -Headers $ownerHeaders) |
    Where-Object name -eq "mvp-e2e-agent" |
    Select-Object -First 1
$agent | Select-Object id, name, status, lastSeenAt
```

Ожидаются сохранённый credential, `status = Online` и непустой `lastSeenAt`.

### 4. ServerInstance, StartServer и повторный StartServer

```powershell
$fixture = (Resolve-Path `
    "tests/ServerPilot.ProcessFixture/bin/Release/net10.0/ServerPilot.ProcessFixture.exe").Path
$fixtureDirectory = Split-Path -Parent $fixture
$serverBody = @{
    agentId = $agent.id
    name = "Harmless MVP process"
    executablePath = $fixture
    arguments = ""
    workingDirectory = $fixtureDirectory
    processName = "ServerPilot.ProcessFixture"
} | ConvertTo-Json
$server = Invoke-RestMethod `
    -Method Post `
    -Uri "${api}api/server-instances" `
    -Headers $ownerHeaders `
    -ContentType "application/json" `
    -Body $serverBody

$firstStart = Invoke-RestMethod `
    -Method Post `
    -Uri "${api}api/server-instances/$($server.id)/commands/start" `
    -Headers $ownerHeaders
Start-Sleep -Seconds 3
$running = Invoke-RestMethod `
    -Uri "${api}api/server-instances/$($server.id)" `
    -Headers $ownerHeaders
$firstPid = $running.lastProcessId
Get-Process -Id $firstPid

$secondStart = Invoke-RestMethod `
    -Method Post `
    -Uri "${api}api/server-instances/$($server.id)/commands/start" `
    -Headers $ownerHeaders
Start-Sleep -Seconds 3
$afterRepeatedStart = Invoke-RestMethod `
    -Uri "${api}api/server-instances/$($server.id)" `
    -Headers $ownerHeaders
```

Обе команды должны стать `Completed`, `$running.status` должен быть `Running`, а
`$afterRepeatedStart.lastProcessId` должен быть равен `$firstPid`. Новый процесс не
создаётся.

### 5. Restart Agent и временная недоступность API

Остановите Agent через `Ctrl+C`, но не fixture. В окне Agent удалите installation token и
снова запустите Agent:

```powershell
Remove-Item Env:Agent__InstallationToken -ErrorAction SilentlyContinue
dotnet run --project src/ServerPilot.Agent --configuration Release --no-build
```

Через несколько секунд API снова должен показывать `Running` и тот же PID. Затем:

```powershell
docker compose stop api
Start-Sleep -Seconds 10
Get-Process -Id $firstPid
docker compose start api
Invoke-WebRequest http://127.0.0.1:8080/health/ready -UseBasicParsing
```

Agent должен остаться запущенным, после восстановления API продолжить heartbeat, а
ServerInstance — сохранить `Running` и тот же PID.

### 6. Ownership isolation

Создайте второго пользователя аналогично шагу 2 и используйте его Bearer token. Следующие
операции над ID первого пользователя должны вернуть одинаковый безопасный `404`:

```text
GET  /api/agents/{agentId}
GET  /api/server-instances/{serverInstanceId}
POST /api/server-instances/{serverInstanceId}/commands/start
GET  /api/server-instances/{serverInstanceId}/commands?limit=20
```

Ответ не должен раскрывать, существует ли чужой ресурс.

### 7. StopServer, повторный StopServer и history

```powershell
$firstStop = Invoke-RestMethod `
    -Method Post `
    -Uri "${api}api/server-instances/$($server.id)/commands/stop" `
    -Headers $ownerHeaders
Start-Sleep -Seconds 4
$stopped = Invoke-RestMethod `
    -Uri "${api}api/server-instances/$($server.id)" `
    -Headers $ownerHeaders

$secondStop = Invoke-RestMethod `
    -Method Post `
    -Uri "${api}api/server-instances/$($server.id)/commands/stop" `
    -Headers $ownerHeaders
Start-Sleep -Seconds 3
$history = Invoke-RestMethod `
    -Uri "${api}api/server-instances/$($server.id)/commands?limit=20" `
    -Headers $ownerHeaders
$history.items | Select-Object type, status, attemptCount, correlationId
```

Ожидается `Stopped` без PID, отсутствие fixture в `Get-Process` и четыре команды
`Completed` (`StartServer`, `StartServer`, `StopServer`, `StopServer`) с непустыми
correlation ID.

### 8. Cleanup

Остановите Agent через `Ctrl+C`. Если это был отдельный тестовый профиль и credential
больше не нужен, удалите только созданный тестом файл:

```powershell
Remove-Item -LiteralPath "$env:LOCALAPPDATA\ServerPilot\agent-credential.dat"
docker compose down --volumes --remove-orphans
```

`--volumes` необратимо удаляет локальные тестовые данные выбранного Compose project.

## Диагностика

| Симптом | Проверка |
| --- | --- |
| API не становится ready | `docker compose ps`, затем `docker compose logs migrate api postgres` |
| Compose требует secret | заполнить локальный `.env`; не добавлять его в git |
| Agent отказывается стартовать | проверить loopback/HTTPS URL, имя, интервалы и наличие installation token только при первом запуске |
| Harness сообщает о существующем credential | использовать отдельный Windows-профиль; не перезаписывать установленный Agent |
| Команда остаётся `Pending` | проверить `Online`, Agent stdout и polling interval |
| Команда стала `Failed` | проверить безопасный `errorCode` в history и Agent log; raw local path пользователю не возвращается |
| Restart показывает `Crashed` | убедиться, что исходный PID всё ещё жив и executable не заменён; PID, path, name и start time являются одной identity |
| После API restart нет heartbeat | дождаться bounded retry; `401/403` являются fatal и требуют revoke/re-registration, а не бесконечного retry |
| Порт занят | задать `SERVERPILOT_API_HOST_PORT` или `SERVERPILOT_POSTGRES_HOST_PORT` в локальном `.env` |

## Подтверждённые ограничения

- Agent пока console-hosted и поддерживает Windows/DPAPI только для текущего пользователя.
- Один Windows-профиль имеет один стандартный Agent credential path.
- Loopback HTTP допустим только локально; любой внешний доступ требует HTTPS.
- Compose workflow предназначен для локальной проверки, не для production rollout.
- Fixture — проверочный native `.exe`, а не профиль Project Zomboid.
- Console fixture не имеет окна и поэтому останавливается через bounded forced fallback после
  неуспешной graceful попытки.
- UI, RabbitMQ, Redis, Kubernetes, backups, schedules и notifications не входят в MVP.
