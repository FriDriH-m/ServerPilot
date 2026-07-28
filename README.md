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
- [`AGENTS.md`](AGENTS.md) — правила работы ИИ-агентов с репозиторием.

## Запуск инфраструктуры

Создайте локальный файл окружения и задайте пароль PostgreSQL:

```powershell
Copy-Item .env.example .env
```

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

### Применение миграций PostgreSQL

Перед локальным применением миграций задайте строку подключения через переменную окружения, не сохраняя пароль в репозитории:

```powershell
$env:ConnectionStrings__PostgreSql = "Host=localhost;Port=5432;Database=serverpilot;Username=serverpilot;Password=<local-password>"
```

Затем примените миграции:

```bash
dotnet ef database update --project src/ServerPilot.Infrastructure --startup-project src/ServerPilot.Infrastructure
```

## Статус проекта

Базовая структура solution подготовлена: созданы проекты Domain, Application, Infrastructure, API, Agent и тестовые проекты.

Текущая цель — реализовать минимальный рабочий вертикальный сценарий без преждевременного добавления сложной инфраструктуры.
