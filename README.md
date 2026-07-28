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
- [`docs/api-conventions.md`](docs/api-conventions.md) — контракты API, валидация, Problem Details и correlation ID.
- [`docs/adr/0001-user-password-and-jwt-authentication.md`](docs/adr/0001-user-password-and-jwt-authentication.md) — решение по password hashing и JWT.
- [`docs/adr/0002-one-time-agent-installation-tokens.md`](docs/adr/0002-one-time-agent-installation-tokens.md) — решение по одноразовым installation tokens.
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

Запустите API и PostgreSQL:

```bash
docker compose up -d
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
`AgentInstallationTokens__MetadataRetentionDays`. Фактическое использование токена
при регистрации Agent относится к следующей задаче MVP.

Login/register ограничены десятью запросами в минуту на клиентский IP, а операции
аутентифицированного пользователя — тридцатью запросами в минуту на `sub`. Значения
настраиваются в секции `RateLimiting`. Ответы, содержащие JWT или исходный installation
token, помечены `Cache-Control: no-store`.

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
До завершения #32 миграции применяются явно приведённой выше командой; readiness не
позволяет ошибочно считать чистую базу готовой.

## Статус проекта

Завершены foundation, PostgreSQL, API conventions, CI, пользовательская JWT-аутентификация
и одноразовые Agent installation tokens. Следующая функциональная задача — #20,
регистрация и отдельная аутентификация Agent.

Текущая цель — реализовать минимальный рабочий вертикальный сценарий без преждевременного добавления сложной инфраструктуры.
