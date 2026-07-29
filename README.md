# ServerPilot

ServerPilot — система удалённого управления локальными игровыми и прикладными серверами.

На Windows-компьютере устанавливается фоновый Agent, который запускает и останавливает локальные серверные процессы, собирает информацию об их состоянии и выполняет ограниченный набор команд от центрального backend.

Управление выполняется через веб-интерфейс.

Первоначально проект ориентирован на сервер Project Zomboid, но архитектура должна позволять управлять другими серверными приложениями, запускаемыми через `.exe` или `.bat`.

## Цель проекта

Проект создаётся для практического изучения production-подходов:

- ASP.NET Core;
- React и TypeScript;
- Windows Service;
- PostgreSQL и EF Core;
- RabbitMQ;
- Redis;
- Docker и Docker Compose;
- структурированное логирование;
- OpenTelemetry;
- Prometheus;
- Grafana;
- Loki;
- CI/CD;
- деплой на VPS;
- Kubernetes;
- интеграционные тесты;
- мониторинг;
- безопасное взаимодействие backend с удалённым агентом.

Функциональность проекта должна оставаться ограниченной. Основной упор делается на качество реализации, инфраструктуру, тестируемость и наблюдаемость.

## Основной сценарий

1. Пользователь устанавливает ServerPilot Agent на Windows-компьютер.
2. Agent регистрируется в backend и периодически отправляет heartbeat.
3. Пользователь добавляет локальный сервер через веб-интерфейс.
4. Пользователь создаёт команду запуска или остановки.
5. Agent получает команду от backend.
6. Agent выполняет действие над локальным процессом.
7. Результат выполнения отправляется обратно в backend.
8. Веб-интерфейс показывает актуальное состояние сервера.

## Компоненты

### ServerPilot.Api

ASP.NET Core API.

Отвечает за:

- авторизацию;
- регистрацию агентов;
- управление серверами;
- создание команд;
- получение результатов;
- хранение истории;
- предоставление данных frontend.

### ServerPilot.Agent

.NET Worker Service, работающий на Windows.

Отвечает за:

- регистрацию в backend;
- отправку heartbeat;
- получение команд;
- запуск процессов;
- остановку процессов;
- отслеживание состояния;
- сбор CPU и RAM;
- чтение логов;
- создание резервных копий.

На этапе разработки Agent должен поддерживать запуск как обычное консольное приложение. Позже он будет устанавливаться как Windows Service.

### ServerPilot.Worker

Фоновый серверный сервис.

В будущем будет отвечать за:

- расписания;
- автоматические команды;
- очистку старых данных;
- обработку повторных попыток;
- проверку недоступных агентов;
- уведомления.

Worker не входит в первый минимальный сценарий.

### ServerPilot.Web

Frontend на React и TypeScript.

Планируемые функции:

- список агентов;
- список серверов;
- запуск и остановка;
- отображение статуса;
- просмотр метрик;
- просмотр логов;
- управление резервными копиями;
- история команд.

Frontend будет добавлен после реализации основной связи между API и Agent.

## Структура решения

```text
ServerPilot/
├── src/
│   ├── ServerPilot.Domain/
│   ├── ServerPilot.Application/
│   ├── ServerPilot.Infrastructure/
│   ├── ServerPilot.Api/
│   └── ServerPilot.Agent/
├── tests/
│   ├── ServerPilot.UnitTests/
│   └── ServerPilot.IntegrationTests/
├── docs/
│   ├── product.md
│   └── mvp.md
├── AGENTS.md
├── README.md
├── docker-compose.yml
└── ServerPilot.slnx
```

## Архитектурные зависимости

```text
ServerPilot.Domain
        ↑
ServerPilot.Application
        ↑
ServerPilot.Infrastructure
        ↑
ServerPilot.Api
```

Допустимые зависимости:

```text
Application → Domain
Infrastructure → Application
Infrastructure → Domain
Api → Application
Api → Infrastructure
Agent → собственные abstractions и HTTP-контракты
```

`Domain` не должен зависеть от:

- ASP.NET Core;
- EF Core;
- PostgreSQL;
- RabbitMQ;
- файловой системы;
- Windows API;
- других инфраструктурных компонентов.

## Технологии первой версии

- .NET 10;
- ASP.NET Core;
- EF Core;
- PostgreSQL;
- .NET Worker Service;
- xUnit;
- Docker Compose.

На первом этапе не используются:

- RabbitMQ;
- Redis;
- Kubernetes;
- Grafana;
- Loki;
- MinIO;
- React.

Они будут добавляться после появления рабочего основного сценария.

## Текущий этап

Первый этап проекта:

```text
API
→ регистрация Agent
→ heartbeat
→ создание ServerInstance
→ создание команды
→ polling команды агентом
→ запуск тестового процесса
→ отправка результата
```

В качестве тестового процесса можно использовать простую программу или долгоживущую команду, не связанную с реальным игровым сервером.

## Документация

- [`docs/product.md`](docs/product.md) — полное описание проекта и целевой архитектуры.
- [`docs/mvp.md`](docs/mvp.md) — границы первой версии.
- [`docs/e2e-validation.md`](docs/e2e-validation.md) — воспроизводимая Windows end-to-end проверка полного MVP и ручной сценарий.
- [`docs/api-conventions.md`](docs/api-conventions.md) — контракты API, валидация, Problem Details и correlation ID.
- [`docs/adr/0001-user-password-and-jwt-authentication.md`](docs/adr/0001-user-password-and-jwt-authentication.md) — решение по password hashing и JWT.
- [`docs/adr/0002-one-time-agent-installation-tokens.md`](docs/adr/0002-one-time-agent-installation-tokens.md) — решение по одноразовым installation tokens.
- [`docs/adr/0003-agent-registration-and-opaque-credentials.md`](docs/adr/0003-agent-registration-and-opaque-credentials.md) — решение по атомарной регистрации и отдельным Agent credentials.
- [`docs/adr/0004-server-instance-process-configuration.md`](docs/adr/0004-server-instance-process-configuration.md) — решение по конфигурации локального процесса и её границе доверия.
- [`docs/adr/0005-active-server-command-uniqueness.md`](docs/adr/0005-active-server-command-uniqueness.md) — решение по единственной активной команде для ServerInstance.
- [`docs/adr/0006-postgresql-command-claiming.md`](docs/adr/0006-postgresql-command-claiming.md) — решение по атомарной выдаче команд Agent через PostgreSQL.
- [`docs/adr/0007-agent-heartbeat-and-command-polling.md`](docs/adr/0007-agent-heartbeat-and-command-polling.md) — решение по Agent heartbeat, polling и ограниченным retry.
- [`docs/adr/0008-safe-local-process-supervision.md`](docs/adr/0008-safe-local-process-supervision.md) — решение по безопасной границе запуска и остановки локального процесса.
- [`docs/adr/0009-idempotent-agent-command-execution.md`](docs/adr/0009-idempotent-agent-command-execution.md) — решение по staged-выполнению команд и retry без повторения process action.
- [`docs/adr/0010-agent-process-state-reconciliation.md`](docs/adr/0010-agent-process-state-reconciliation.md) — решение по Agent-authoritative process state, safe restart rediscovery и offline semantics.
- [`docs/adr/0011-compose-migration-startup.md`](docs/adr/0011-compose-migration-startup.md) — решение по one-shot Compose migrations, readiness и clean reset.
- [`docs/threat-model.md`](docs/threat-model.md) — актуальные trust boundaries, угрозы и меры защиты MVP.
- [`AGENTS.md`](AGENTS.md) — правила работы ИИ-агентов с репозиторием.

## Запуск инфраструктуры

Создайте локальный файл окружения. Значения в `.env.example` намеренно пустые:
Compose не должен запускаться с известными публичными credentials.

```powershell
Copy-Item .env.example .env
```

Сгенерируйте независимые случайные значения и запишите их в `.env`:

```powershell
$postgresBytes = [byte[]]::new(24)
$jwtBytes = [byte[]]::new(48)
$generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$generator.GetBytes($postgresBytes)
$generator.GetBytes($jwtBytes)
[Convert]::ToBase64String($postgresBytes)
[Convert]::ToBase64String($jwtBytes)
$generator.Dispose()
```

Первый результат используйте как `POSTGRES_PASSWORD`, второй — как
`JWT_SIGNING_KEY`. API дополнительно отклоняет публичное placeholder-значение, даже
если оно формально длиннее 32 байт.

Запустите API, PostgreSQL и одноразовый migration service:

```bash
docker compose up -d --build
```

`migrate` ждёт healthy PostgreSQL, применяет EF Core migrations один раз и должен
завершиться успешно до запуска API. При ошибке миграции API намеренно не запускается;
исправьте причину, изучите `docker compose logs migrate` и повторите команду. API считается
готовым только после `GET /health/ready`; `GET /health/live` проверяет лишь работающий
процесс. Для проверки используйте:

```powershell
Invoke-WebRequest http://127.0.0.1:8080/health/live
Invoke-WebRequest http://127.0.0.1:8080/health/ready
```

Порты опубликованы только на loopback: API по умолчанию использует `8080`, PostgreSQL —
`5432`; при конфликте задайте `SERVERPILOT_API_HOST_PORT` или
`SERVERPILOT_POSTGRES_HOST_PORT`. Windows Agent в локальном Compose-сценарии подключается к
`http://127.0.0.1:8080/`. Для не-loopback deployment требуется HTTPS.

Остановить окружение без удаления данных:

```bash
docker compose down
```

Полностью удалить локальные данные и на следующем запуске применить все миграции к чистой
базе:

```bash
docker compose down --volumes --remove-orphans
docker compose up -d --build
```

Сборка проекта:

```bash
dotnet build
```

Запуск тестов:

```bash
dotnet test
```

Проверка форматирования:

```bash
dotnet format --verify-no-changes
```

### Аутентификация пользователя

API предоставляет `POST /api/auth/register`, `POST /api/auth/login` и защищённый
`GET /api/auth/me`. Регистрация и login возвращают JWT access token сроком на 30 минут.
Передавайте его в заголовке `Authorization: Bearer <token>`.

Signing key не хранится в `appsettings.json`. Для локального запуска без Compose
задайте сгенерированное случайное значение через environment variable:

```powershell
$env:Authentication__Jwt__SigningKey = "<random-value-with-at-least-32-utf8-bytes>"
```

Email хранится в исходном trimmed-виде и отдельно нормализуется для уникального
сравнения. Пароли сохраняются только как ASP.NET Core Identity hash. Refresh tokens,
password reset, email confirmation и роли не входят в текущий MVP.

### Installation tokens для Agent

Аутентифицированный пользователь может создать одноразовый токен через
`POST /api/agent-installation-tokens`, получить собственные метаданные через `GET` по
тому же адресу и отозвать неиспользованный токен через
`DELETE /api/agent-installation-tokens/{id}`.

Исходное значение возвращается только при создании. В PostgreSQL сохраняется только
SHA-256 hash; список не содержит ни исходного значения, ни hash. По умолчанию токен
действует 15 минут. Для локального запуска срок можно изменить через конфигурацию:

```powershell
$env:AgentInstallationTokens__LifetimeMinutes = "15"
```

Допустимый диапазон — от 1 до 1 440 минут. Один пользователь может иметь не более
10 активных токенов одновременно; значение настраивается через
`AgentInstallationTokens__MaximumActiveTokensPerUser`. `GET` возвращает не более
50 последних записей по умолчанию и принимает `limit` от 1 до 100 и `page` от 1 до
1 000. Использованные, отозванные или просроченные метаданные старше 90 дней
удаляются при следующем создании токена этого пользователя; срок настраивается через
`AgentInstallationTokens__MetadataRetentionDays`.

### Регистрация и аутентификация Agent

Неаутентифицированный Agent регистрируется через `POST /api/agents/register`, передавая
одноразовый installation token, имя, machine name, ОС и версию. API атомарно помечает
token использованным и создаёт Agent: параллельные запросы с одним token не могут
создать два Agent.

Ответ возвращает отдельный credential только один раз. В PostgreSQL хранится только
SHA-256 hash. Agent передаёт credential так:

```http
Authorization: Agent spac_<64 uppercase hex characters>
```

`GET /api/agents/me` проверяет Agent authentication и возвращает точный Agent ID.
Пользователь может идемпотентно отозвать credentials собственного Agent через
`DELETE /api/agents/{id}/credentials`; чужой ID возвращает `404`. После commit отзыва
следующий Agent-запрос получает `401`. Credential не имеет автоматического срока
действия в MVP, поэтому HTTPS и безопасное локальное хранение в issue #26 обязательны.

### Bootstrap и локальное хранение Agent credential

`ServerPilot.Agent` запускается как консольный Worker. До старта фоновых циклов он
валидирует конфигурацию, затем при первом запуске регистрируется через
`POST /api/agents/register`. Одноразовый installation token задаётся только извне и
нужен только пока нет сохранённого credential:

```powershell
$env:Agent__ApiBaseUrl = "https://localhost:5001/"
$env:Agent__Name = "my-windows-agent"
$env:Agent__InstallationToken = "spit_<installation-token>"
$env:Agent__HeartbeatIntervalSeconds = "10"
$env:Agent__CommandPollingIntervalSeconds = "5"
$env:Agent__ProcessReconciliationIntervalSeconds = "10"
dotnet run --project src/ServerPilot.Agent
```

Для локальной разработки допустим `http://localhost` или `http://127.0.0.1`; любой
не-loopback URL должен использовать HTTPS. Token и выданный credential не записываются
в `appsettings.json` и не логируются. После успешной регистрации credential вместе с
Agent ID сохраняется атомарно в
`%LOCALAPPDATA%\ServerPilot\agent-credential.dat`, зашифрованный Windows DPAPI для
текущего пользователя. Последующие запуски используют это хранилище и не требуют token.

Файл нельзя переносить на другую машину или запускать Agent под другим Windows
пользователем: потребуется новый installation token. Если credential считается
скомпрометированным, сначала отзовите его через API, затем удалите локальный файл и
зарегистрируйте Agent заново.

### Heartbeat и polling Agent

После bootstrap Agent использует сохранённый credential в заголовке `Authorization`
каждого запроса и запускает независимые последовательные циклы для
`POST /api/agents/{id}/heartbeat` и
`POST /api/agents/{id}/commands/claim-next`, а также периодическую сверку назначенных
ServerInstance через Agent-only list/status endpoints. Следующая итерация начинается только
после завершения предыдущего запроса и задержки, поэтому медленный API не создаёт
перекрывающихся heartbeat или claim.

Ошибки сети, `408`, `429` и `5xx` повторяются не более трёх раз после первого запроса
с экспоненциальной задержкой 1/2/4 секунды и bounded jitter; затем цикл возвращается к
настроенному интервалу. `401`/`403`, другие неожиданные `4xx` и некорректный ответ API
считаются неисправимой credential/configuration ошибкой: Agent пишет структурированное
событие без секрета и останавливается, не создавая бесконечный retry loop.

После `claim-next` Agent получает вместе с командой сохранённую process-конфигурацию
целевого `ServerInstance`, переводит команду в `Running`, выполняет `StartServer` или
`StopServer` через supervisor и проверяет фактическое состояние перед `Completed`.
Ошибка конфигурации или процесса отправляется как `Failed` с безопасным стабильным кодом
и общим сообщением без локальных путей, аргументов или stack trace.

В памяти одновременно находится только один staged work item. Если response на
`/complete` или `/fail` потерян, Agent повторяет только terminal report с тем же
Correlation ID, но не process action. Следующая команда не запрашивается, пока текущий
result не принят API. Успешный Start/Stop сначала отправляет проверенное состояние процесса,
а затем terminal command result; потерянный state response повторяет только cached report.

Сохранённый UUID `CorrelationId` команды добавляется в structured scope на API при создании
и claim, в Agent при execution и в state/result reports. Он связывает lifecycle без записи
credentials, request bodies, путей, аргументов запуска или raw failure details в логи.

Command polling не начинается до первой успешной сверки после запуска Agent. Для ранее
зафиксированного `Running` Agent восстанавливает supervisor из persisted PID и UTC-времени
старта и принимает процесс только после совпадения полного identity. Периодическая проверка
переводит исчезнувший ранее `Running` процесс в `Crashed`; ошибка inspection не подменяется
состоянием `Stopped`.

### Безопасный process supervisor Agent

Supervisor принимает только заранее сохранённую конфигурацию нативного `.exe`, повторно
проверяет абсолютные Windows/UNC-пути и существование executable/working directory на
локальной машине, затем запускает процесс с `UseShellExecute = false`. Shell, PowerShell,
`.bat` и executable path из payload команды не используются.

Agent отслеживает PID вместе с UTC-временем запуска, фактическим путём и именем процесса.
Перед каждой остановкой identity проверяется заново: stale/reused PID не получает signal.
Сначала предпринимается graceful stop с ограниченным ожиданием, после чего допустим
явный принудительный fallback с отдельным timeout и структурированным логом без путей и
аргументов. Command execution и reconciliation используют один gate, поэтому intentional
stop не может быть ошибочно классифицирован параллельной проверкой как crash.

### Heartbeat и доступность Agent

Аутентифицированный Agent отправляет heartbeat через
`POST /api/agents/{id}/heartbeat`. Route ID должен совпадать с Agent ID из проверенного
credential; JWT пользователя и credential другого Agent не принимаются. API не доверяет
timestamp клиента: `LastSeenAt` устанавливается по серверному UTC и не может сдвинуться
назад при параллельных или переупорядоченных запросах.

Пользователь получает только собственные Agent через `GET /api/agents` и
`GET /api/agents/{id}`. Ответ содержит безопасные метаданные, `LastSeenAt` и вычисляемый
статус `Online`/`Offline`, но не credential и не его hash. Agent без heartbeat считается
`Offline`; heartbeat точно на границе threshold ещё считается `Online`. Threshold не
хранится в базе и настраивается в диапазоне от 1 секунды до 24 часов:

```powershell
$env:AgentAvailability__OfflineThresholdSeconds = "30"
```

Отдельный background job не записывает `Offline`: статус вычисляется при чтении, поэтому
не устаревает из-за пропущенного планового обновления.

Login/register ограничены десятью запросами в минуту на клиентский IP, а операции
аутентифицированного пользователя — тридцатью запросами в минуту на `sub`. Значения
настраиваются в секции `RateLimiting`. Ответы, содержащие JWT, исходный installation
token или Agent credential, помечены `Cache-Control: no-store`.

### Конфигурация ServerInstance

Пользователь с JWT может создавать, просматривать, изменять и удалять только
`ServerInstance` своих Agent через `/api/server-instances`. Конфигурация хранится в
PostgreSQL: имя, абсолютные Windows/UNC-пути без сегментов `.`/`..` к исполняемому файлу и рабочей директории,
аргументы и имя процесса. Это заранее сохранённая конфигурация, а не произвольная
команда, передаваемая будущему Agent при каждом запуске.

`GET /api/server-instances` возвращает безопасный список без локальных путей и
аргументов. Полная конфигурация доступна только владельцу через создание, получение по
ID или изменение. API проверяет базовую форму пути, но не проверяет существование файла
на удалённой машине — это обязанность Agent при выполнении будущей команды. Нельзя
удалить экземпляр в состояниях `Starting`, `Running` или `Stopping`: API вернёт `409`.
Изменение executable path, arguments, working directory или process name также вернёт
`409`, пока процесс активен либо существует `Pending`, `Claimed` или `Running` команда;
обычное переименование экземпляра при этом разрешено. При проверке пути `/` и `\`
сначала приводятся к одной форме, поэтому device namespace нельзя скрыть смешанными
разделителями.

Фактическое состояние сообщает только аутентифицированный целевой Agent через
`POST /api/agents/{agentId}/server-instances/{serverInstanceId}/status`. API сохраняет
reported status, PID, время старта процесса и серверное время получения отчёта. Конфигурация
и последнее identity выдаются этому же Agent постранично через
`GET /api/agents/{agentId}/server-instances`; чужие Agent/ServerInstance возвращают `404`.

Пользовательский `Status` является effective view: если heartbeat owning Agent устарел,
он равен `Unreachable`, а `ReportedStatus`, PID и `LastStatusReportedAt` остаются последним
известным, явно stale снимком. Offline никогда не записывает фиктивный `Stopped`.

### Команды ServerCommand

Пользователь с JWT создаёт команды только для принадлежащего ему `ServerInstance` через
`POST /api/server-instances/{id}/commands/start` и
`POST /api/server-instances/{id}/commands/stop`; `GET` по тому же ресурсу возвращает
историю от новых к старым. В один момент для экземпляра допустима только одна активная
команда в состояниях `Pending`, `Claimed` или `Running`: конкурирующий запрос получает
`409`. История возвращает статусы, временные метки и безопасный код ошибки без
необработанного сообщения Agent. Экземпляр с историей команд не удаляется и также
возвращает `409`, чтобы не потерять историю.

История использует keyset pagination: `limit` принимает значения от 1 до 100, ответ
имеет форму `{ "items": [...], "nextCursor": "..." }`, а следующий запрос передаёт
`nextCursor` как query-параметр `cursor`. Устаревший параметр `page` и некорректный
курсор возвращают `400`.

Agent с отдельным credential забирает старейшую ожидающую команду через
`POST /api/agents/{agentId}/commands/claim-next`. PostgreSQL атомарно переводит её из
`Pending` в `Claimed`. Если предыдущий HTTP-ответ был потерян, тот же Agent сначала
получает свою уже `Claimed` или `Running` команду с `deliveryKind: "Recovery"`; новая
выдача помечается `deliveryKind: "New"`. PostgreSQL допускает не более одной такой
активной команды на Agent. Agent сообщает прогресс и результат через
`POST /api/commands/{commandId}/start`, `/complete` и `/fail`. Все изменения привязаны
к Agent ID из credential; чужие команды возвращают `404`, недопустимые переходы —
`409`, а точные повторы уже применённого перехода безопасно возвращают `204`.
Failure code и message обязательны, обрезаются по краям и ограничены по длине; сырое
сообщение сохраняется для диагностики, но не попадает в пользовательскую историю или
структурированные логи.

## Continuous integration

Workflow [`.github/workflows/ci.yml`](.github/workflows/ci.yml) запускается для каждого pull request в `main` и каждого push в `main`.

CI и локальная разработка используют один канонический сценарий:

```powershell
./eng/verify.ps1
```

Он выполняет NuGet restore/audit, Release build, formatting, unit- и PostgreSQL
integration-тесты, проверку соответствия EF-модели миграциям, проверку Compose и
сборку Docker-образа API. `-SkipDockerBuild` пропускает только последнюю операцию.

Integration-тесты используют Testcontainers и создают временный PostgreSQL-контейнер из образа `postgres:18.4-alpine`. На GitHub-hosted Ubuntu runner Docker уже доступен, поэтому отдельный PostgreSQL service container и credentials в workflow не нужны. Для локального запуска integration-тестов должен работать Docker Desktop или другой совместимый Docker daemon.

Полная проверка настоящего Windows Agent и безопасного локального process fixture запускается
отдельно, потому что DPAPI и Windows process identity недоступны Linux CI:

```powershell
./eng/verify-e2e.ps1
```

Она проверяет clean Compose startup, registration/login, heartbeat, Start/Stop, повторные
команды, Agent/API restart recovery, временную недоступность API, command history и ownership
isolation. Подробные требования, ожидаемые ответы, cleanup и troubleshooting приведены в
[`docs/e2e-validation.md`](docs/e2e-validation.md).

NuGet Audit явно включён для прямых и транзитивных зависимостей. Поскольку warnings считаются errors, найденная уязвимость завершает restore с ошибкой и остаётся видимой в логе job. При падении тестов CI сохраняет TRX-файлы как artifact на семь дней.

Кеш NuGet намеренно не настроен: его следует добавлять только после измерения времени restore.

### Применение миграций PostgreSQL

Перед локальным применением миграций задайте строку подключения через переменную окружения, не сохраняя пароль в репозитории:

```powershell
$env:ConnectionStrings__PostgreSql = "Host=localhost;Port=5432;Database=serverpilot;Username=serverpilot;Password=<local-password>"
```

Затем примените миграции:

```bash
dotnet tool restore
dotnet ef database update --project src/ServerPilot.Infrastructure --startup-project src/ServerPilot.Infrastructure
```

`/health/live` проверяет только работу процесса API. `/health/ready` и совместимый
`/health` дополнительно требуют доступный PostgreSQL без неприменённых миграций.
Compose применяет миграции отдельным one-shot service; readiness не позволяет ошибочно
считать базу с неприменённой схемой готовой. `./eng/verify-compose.ps1` проверяет clean
startup, `/health/ready`, историю миграций и reset с удалением volume.

## Статус проекта

Завершены foundation, PostgreSQL, API conventions, CI, пользовательская JWT-аутентификация,
одноразовые Agent installation tokens, регистрация, отдельная аутентификация, heartbeat,
пользовательские Agent queries, ServerInstance configuration/ownership, пользовательские
Start/Stop endpoints, история ServerCommand и Agent endpoints атомарной выдачи, прогресса
и результата команд. Реализованы функциональные задачи #25–#31. Agent теперь
валидирует typed configuration до запуска фоновых циклов, регистрируется по installation
token только при первом запуске и хранит выданный credential в Windows DPAPI-защищённом
local storage текущего пользователя, отправляет heartbeat и последовательно получает
следующую команду с ограниченными transient retry. После независимого аудита добавлены минимальные
hardening-исправления: безопасная проверка Windows-путей со смешанными разделителями,
защита process-critical конфигурации при активной команде, восстановление потерянного
claim-response, cursor pagination истории, защита переходов при регрессии часов и
PostgreSQL-инвариант одной `Claimed`/`Running` команды на Agent.

Локальный process supervisor дополнительно проверяет сохранённую конфигурацию на Windows,
не использует shell, предотвращает повторный запуск, сверяет полную identity процесса
перед остановкой и применяет ограниченный forced fallback только после graceful attempt.

Polling loop связан с supervisor через staged command executor: API выдаёт сохранённую
конфигурацию только целевому Agent, `StartServer`/`StopServer` выполняются один раз и
проверяются inspection, а потерянный terminal response не повторяет локальное действие.

Agent периодически получает назначенные ServerInstance, безопасно восстанавливает persisted
process identity после рестарта, сохраняет проверенные PID/status и обнаруживает неожиданный
выход как `Crashed`. Для offline Agent пользователь видит `Unreachable` вместе с последним
reported snapshot, а не вымышленный `Stopped`.

Минимальный вертикальный сценарий реализован и воспроизводимо проверяется через
`eng/verify.ps1` и Windows-only `eng/verify-e2e.ps1`. Дальнейшая работа выбирается из
post-MVP roadmap и не расширяет этот сценарий неявно.
