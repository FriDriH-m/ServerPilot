# ServerPilot MVP

## Цель MVP

Создать минимальную рабочую систему, в которой пользователь может удалённо запустить и остановить локальный процесс через Windows Agent.

MVP должен подтвердить работу всей основной цепочки:

```text
Пользователь
→ ASP.NET Core API
→ команда
→ Windows Agent
→ локальный процесс
→ результат выполнения
→ API
```

В MVP не требуется полноценная поддержка Project Zomboid. Сначала Agent должен уметь управлять простым тестовым процессом.

## Основной пользовательский сценарий

1. Пользователь создаёт учётную запись.
2. Пользователь создаёт токен установки Agent.
3. Windows Agent регистрируется в backend.
4. Agent периодически отправляет heartbeat.
5. Пользователь создаёт описание локального сервера.
6. Пользователь нажимает кнопку запуска.
7. Backend создаёт команду `StartServer`.
8. Agent получает команду через polling.
9. Agent запускает локальный процесс.
10. Agent отправляет результат выполнения.
11. Backend обновляет статус команды и сервера.
12. Пользователь видит состояние `Running`.
13. Пользователь создаёт команду `StopServer`.
14. Agent останавливает процесс.
15. Пользователь видит состояние `Stopped`.

## Компоненты MVP

### ASP.NET Core API

API должно поддерживать:

- регистрацию пользователя;
- авторизацию;
- создание installation token;
- регистрацию Agent;
- heartbeat;
- создание ServerInstance;
- получение списка ServerInstance;
- создание команды;
- получение Agent следующей команды;
- начало выполнения команды;
- успешное завершение команды;
- завершение команды с ошибкой;
- получение истории команд.

### Windows Agent

Agent должен:

- запускаться как консольное приложение;
- читать настройки из конфигурации;
- регистрироваться по installation token;
- сохранять выданные credentials;
- периодически отправлять heartbeat;
- периодически запрашивать следующую команду;
- выполнять только известные типы команд;
- запускать локальный процесс;
- сохранять PID;
- проверять состояние процесса;
- останавливать процесс;
- отправлять результат выполнения;
- корректно обрабатывать временную недоступность API;
- писать структурированные логи.

### PostgreSQL

PostgreSQL хранит:

- пользователей;
- installation tokens;
- агентов;
- серверы;
- команды;
- историю изменения состояний.

## Основные сущности

### User

```text
Id
Email
PasswordHash
CreatedAt
```

### Agent

```text
Id
UserId
Name
MachineName
OperatingSystem
AgentVersion
Status
LastSeenAt
CreatedAt
```

### AgentInstallationToken

```text
Id
UserId
TokenHash
ExpiresAt
UsedAt
RevokedAt
CreatedAt
```

В базе не должен храниться installation token в открытом виде.

### ServerInstance

```text
Id
AgentId
Name
ExecutablePath
Arguments
WorkingDirectory
ProcessName
Status
LastProcessId
CreatedAt
UpdatedAt
```

### ServerCommand

```text
Id
AgentId
ServerInstanceId
Type
Status
CreatedAt
ClaimedAt
StartedAt
CompletedAt
ErrorCode
ErrorMessage
AttemptCount
CorrelationId
```

## Поддерживаемые команды

В MVP поддерживаются только:

```text
StartServer
StopServer
```

В MVP не реализуются:

```text
RestartServer
CreateBackup
RestoreBackup
UpdateConfiguration
ExecuteShell
ExecutePowerShell
```

Agent никогда не должен выполнять произвольную командную строку, полученную от backend.

Пути и аргументы запуска должны быть сохранены в заранее созданной конфигурации `ServerInstance`.

## Состояния Agent

```text
Unknown
Online
Offline
```

Agent считается `Offline`, если heartbeat не поступал дольше установленного времени.

Например:

```text
Heartbeat interval: 10 секунд
Offline threshold: 30 секунд
```

Значения должны задаваться через конфигурацию.

## Состояния сервера

```text
Unknown
Starting
Running
Stopping
Stopped
Crashed
Unreachable
```

Статус не должен определяться только по последней команде.

Agent должен проверять фактическое состояние локального процесса.

## Состояния команды

```text
Pending
Claimed
Running
Completed
Failed
Cancelled
TimedOut
```

Основной переход состояний:

```text
Pending
→ Claimed
→ Running
→ Completed
```

При ошибке:

```text
Pending
→ Claimed
→ Running
→ Failed
```

## Получение команд агентом

В MVP используется HTTP polling.

Agent периодически вызывает API:

```http
POST /api/agents/{agentId}/heartbeat
POST /api/agents/{agentId}/commands/claim-next
POST /api/commands/{commandId}/start
POST /api/commands/{commandId}/complete
POST /api/commands/{commandId}/fail
```

Операция `claim-next` должна атомарно назначать команду конкретному агенту.

Одна команда не должна одновременно выдаваться нескольким экземплярам Agent.

## Идемпотентность

Повторная доставка команды не должна приводить к опасному повторению операции.

### StartServer

Если сервер уже запущен, повторный `StartServer` не должен запускать второй процесс.

Допустимый результат:

```text
Completed: server is already running
```

### StopServer

Если процесс уже остановлен, повторный `StopServer` должен завершаться успешно.

Допустимый результат:

```text
Completed: server is already stopped
```

## Владение данными

Пользователь может обращаться только к:

- собственным Agent;
- ServerInstance собственных Agent;
- командам собственных серверов.

Каждый запрос должен проверять принадлежность ресурса пользователю.

Нельзя доверять только идентификатору, пришедшему от клиента.

## Безопасность Agent

Agent имеет доступ к локальным процессам, поэтому действуют следующие ограничения:

- Agent аутентифицируется отдельно от пользователя;
- credentials Agent не хранятся в исходном коде;
- installation token является одноразовым;
- Agent получает только команды, адресованные ему;
- Agent выполняет только поддерживаемые типы команд;
- Agent не выполняет произвольный PowerShell или shell-код;
- абсолютные пути должны проверяться;
- ошибки не должны раскрывать секретные значения;
- все действия должны логироваться.

## API MVP

Предварительный список endpoint:

### Authentication

```http
POST /api/auth/register
POST /api/auth/login
```

### Installation tokens

```http
POST /api/agent-installation-tokens
GET /api/agent-installation-tokens
DELETE /api/agent-installation-tokens/{id}
```

### Agents

```http
POST /api/agents/register
POST /api/agents/{agentId}/heartbeat
GET /api/agents
GET /api/agents/{agentId}
```

### Server instances

```http
POST /api/server-instances
GET /api/server-instances
GET /api/server-instances/{id}
PUT /api/server-instances/{id}
DELETE /api/server-instances/{id}
```

Удаление запущенного сервера должно быть запрещено.

### Commands

```http
POST /api/server-instances/{id}/commands/start
POST /api/server-instances/{id}/commands/stop
GET /api/server-instances/{id}/commands
```

### Agent command processing

```http
POST /api/agents/{agentId}/commands/claim-next
POST /api/commands/{commandId}/start
POST /api/commands/{commandId}/complete
POST /api/commands/{commandId}/fail
```

Точные маршруты могут измениться после обсуждения API, но поведение должно сохраниться.

## Тесты MVP

Минимальные unit-тесты:

- переходы состояний команды;
- невозможность запуска второго процесса;
- повторная остановка уже остановленного процесса;
- валидация ServerInstance;
- проверка истечения installation token.

Минимальные integration-тесты:

- регистрация пользователя;
- регистрация Agent;
- отклонение просроченного token;
- heartbeat;
- создание ServerInstance;
- запрет доступа к чужому Agent;
- создание команды запуска;
- атомарное получение следующей команды;
- успешное завершение команды;
- завершение команды с ошибкой.

Integration-тесты должны использовать настоящий PostgreSQL через Testcontainers или другое изолированное тестовое окружение.

EF Core InMemory не должен использоваться как замена интеграционным тестам PostgreSQL.

## Docker Compose

На этапе MVP через Docker Compose запускаются:

```text
PostgreSQL
ServerPilot.Api
```

Agent запускается непосредственно на Windows, а не в Docker.

Frontend пока необязателен. Основные сценарии можно проверить через HTTP-клиент.

## Не входит в MVP

В MVP намеренно не входят:

- RabbitMQ;
- Redis;
- React;
- Kubernetes;
- Prometheus;
- Grafana;
- Loki;
- OpenTelemetry tracing;
- MinIO;
- резервные копии;
- восстановление резервных копий;
- расписания;
- уведомления;
- несколько ролей пользователей;
- организации и команды;
- мобильное приложение;
- управление произвольными командами ОС;
- автоматическое обновление Agent;
- поддержка Linux Agent;
- поддержка нескольких экземпляров одного сервера.

## Порядок реализации

1. Создать solution и проекты.
2. Настроить зависимости между проектами.
3. Настроить PostgreSQL и EF Core.
4. Реализовать пользователя и авторизацию.
5. Реализовать installation token.
6. Реализовать регистрацию Agent.
7. Реализовать heartbeat.
8. Реализовать ServerInstance.
9. Реализовать ServerCommand.
10. Реализовать атомарное получение команды.
11. Реализовать HTTP-клиент Agent.
12. Реализовать запуск тестового процесса.
13. Реализовать остановку процесса.
14. Реализовать синхронизацию фактического состояния.
15. Добавить integration-тесты.
16. Добавить Docker Compose.
17. Проверить полный сценарий вручную.

## Критерии готовности MVP

MVP считается готовым, когда:

1. API запускается через Docker Compose.
2. Agent запускается на Windows.
3. Agent успешно регистрируется.
4. Heartbeat отображает Agent как `Online`.
5. Пользователь создаёт ServerInstance.
6. Пользователь создаёт команду запуска.
7. Agent получает команду.
8. Локальный процесс действительно запускается.
9. API получает фактический PID и статус `Running`.
10. Пользователь создаёт команду остановки.
11. Процесс действительно завершается.
12. API показывает статус `Stopped`.
13. История команд сохраняется.
14. Пользователь не может управлять чужим Agent.
15. Основные integration-тесты проходят.
16. Повторная доставка команды не создаёт второй процесс.
