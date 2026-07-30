# Установка ServerPilot Agent как Windows Service

Issue #37 добавляет post-MVP способ доставки Agent на Windows без установленного .NET Runtime.
Один и тот же executable поддерживает два режима:

- Windows Service для постоянной работы и автоматического старта;
- обычное консольное приложение для разработки и `eng/verify-e2e.ps1`.

## Что входит в пакет

Соберите self-contained пакет `win-x64` из корня репозитория:

```powershell
./eng/agent/Publish-AgentPackage.ps1
```

Результат:

```text
artifacts/agent/win-x64/
├── app/       self-contained Agent
├── scripts/   административный сценарий управления службой
└── README.md  эта инструкция

artifacts/agent/win-x64.zip
```

`Publish-AgentPackage.ps1` всегда создаёт пакет в `artifacts`, проверяет PowerShell-синтаксис
сценария управления и не включает credential или deployment-конфигурацию.

## Требования

- Windows 10/11 x64 или поддерживаемый Windows Server x64;
- Windows PowerShell 5.1 или новее, запущенный **от имени администратора**;
- доступ к API по HTTPS; `http://localhost` и `http://127.0.0.1` разрешены только для
  локальной разработки;
- новый одноразовый Agent installation token;
- отдельные каталоги серверов, к которым можно явно выдать права службе.

Не переносите credential консольного Agent из `%LOCALAPPDATA%`. Он защищён DPAPI для другой
Windows-учётной записи. Перед переходом остановите консольный Agent, отзовите его credential
через API и создайте новый installation token для службы.

## Установка

Распакуйте архив в временный каталог. В административном PowerShell запросите token как
`SecureString`, чтобы он не попал в историю командной строки:

```powershell
$installationToken = Read-Host "Agent installation token" -AsSecureString
./scripts/Manage-AgentService.ps1 `
  -Action Install `
  -ApiBaseUrl "https://serverpilot.example.com/" `
  -AgentName "game-host-01" `
  -InstallationToken $installationToken `
  -ManagedServerDirectory @("D:\Servers\ProjectZomboid")
```

Установка:

1. копирует self-contained приложение в `%ProgramFiles%\ServerPilot\Agent`;
2. регистрирует delayed-auto службу `ServerPilot.Agent` под виртуальной учётной записью
   `NT SERVICE\ServerPilot.Agent`;
3. задаёт recovery: перезапуск через 5, 15 и 60 секунд после непредвиденного сбоя;
4. закрывает наследование ACL для каталогов приложения и данных;
5. выдаёт службе `Read & Execute` на binaries, `Modify` на каталог данных и только явно
   запрошенные каталоги серверов;
6. запускает службу и ждёт DPAPI-защищённый credential;
7. атомарно удаляет одноразовый installation token из конфигурации после регистрации.

Если регистрация не завершилась за заданное время, служба удаляется, но restricted
конфигурация и данные сохраняются для диагностики. Token не печатается в консоль или лог.

### Стабильные пути

| Назначение | Путь |
|---|---|
| Binaries | `%ProgramFiles%\ServerPilot\Agent` |
| Конфигурация | `%ProgramData%\ServerPilot\Agent\appsettings.json` |
| Credential | `%ProgramData%\ServerPilot\Agent\agent-credential.dat` |
| Логи службы | Event Viewer → Windows Logs → Application, source `ServerPilot.Agent` |

Credential зашифрован DPAPI `CurrentUser` именно для виртуальной учётной записи службы.
Дополнительную границу обеспечивает ACL каталога: доступ имеют только `SYSTEM`, локальные
администраторы и service SID. Смена имени или учётной записи службы требует новой регистрации.

## Права на серверные каталоги

Agent не получает доступ ко всему диску. Для каждого нового корня серверов выполните:

```powershell
./scripts/Manage-AgentService.ps1 `
  -Action GrantPath `
  -ManagedServerDirectory @("D:\Servers\AnotherServer")
```

Сценарий принимает только существующий абсолютный каталог, запрещает корень диска/шары и
Windows directory, затем выдаёт service SID рекурсивный `Modify`. Выбирайте минимальный
отдельный каталог. Для UNC-путей виртуальная учётная запись обращается к сети как учётная
запись компьютера; необходимые share/NTFS permissions на удалённом узле настраиваются отдельно.

## Запуск, остановка и перезагрузка

```powershell
./scripts/Manage-AgentService.ps1 -Action Stop
./scripts/Manage-AgentService.ps1 -Action Start
```

SCM передаёт cancellation token в Agent. Heartbeat, polling, reconciliation и bounded retry
завершаются без нового запроса. Остановка самой службы не отправляет `StopServer` и не удаляет
управляемые файлы; после старта Agent сверяет сохранённую process identity с реальным процессом.

Delayed-auto startup обеспечивает автоматический запуск после перезагрузки. Agent не требует
готовой сети в момент старта: временная недоступность API обрабатывается существующим bounded
retry и обычным интервалом циклов.

## Обновление

Соберите или получите новый пакет, затем из его каталога выполните:

```powershell
./scripts/Manage-AgentService.ps1 -Action Update
```

Сценарий копирует пакет в staging-каталог, запоминает состояние службы, останавливает её,
атомарно меняет каталог приложения, повторно применяет ACL и возвращает прежнее состояние.
`%ProgramData%`, credential и ServerInstance data не изменяются. При ошибке замены восстанавливается
предыдущий каталог приложения и ранее работавшая служба запускается снова.

Ручная проверка upgrade:

1. убедитесь, что служба `Running`, а Agent виден `Online`;
2. выполните `Update` из нового пакета;
3. проверьте тот же Agent ID и новый heartbeat;
4. выполните безопасный `StartServer`/`StopServer` тестового процесса;
5. проверьте Application log на отсутствие bootstrap или DPAPI ошибок.

## Удаление

```powershell
./scripts/Manage-AgentService.ps1 -Action Uninstall
```

Сценарий останавливает и удаляет регистрацию службы, затем удаляет только binaries из
`%ProgramFiles%\ServerPilot\Agent`. Он намеренно сохраняет:

- `%ProgramData%\ServerPilot\Agent` вместе с конфигурацией и credential;
- Event Log и его source;
- все серверные каталоги и их содержимое;
- выданные service SID ACL, чтобы повторная установка с тем же именем могла продолжить работу.

Перед окончательным ручным удалением credential сначала отзовите его через API. Не удаляйте
каталоги серверов или их данные сценарием деинсталляции.

## Диагностика

Состояние и recovery-конфигурация:

```powershell
Get-Service ServerPilot.Agent
sc.exe qc ServerPilot.Agent
sc.exe qfailure ServerPilot.Agent
```

Последние события Agent:

```powershell
Get-WinEvent `
  -FilterHashtable @{ LogName = "Application"; ProviderName = "ServerPilot.Agent" } `
  -MaxEvents 50
```

Права каталогов:

```powershell
icacls.exe "$env:ProgramData\ServerPilot\Agent"
icacls.exe "D:\Servers\ProjectZomboid"
```

Типичные причины ошибки старта:

- API URL не HTTPS и не loopback;
- installation token истёк, отозван или уже использован;
- конфигурация была скопирована без связанного DPAPI credential;
- service SID не имеет `Read & Execute` на executable или `Modify` на working directory;
- executable/working directory ServerInstance не существует либо указывает не на `.exe`;
- Event Log source не был создан административным installer-сценарием.

Для изменения API URL, имени или интервалов остановите службу, отредактируйте restricted
`%ProgramData%\ServerPilot\Agent\appsettings.json` и снова запустите службу. Никогда не помещайте
выданный Agent credential в JSON. Installation token допустим только для новой регистрации и
должен быть удалён после успешного появления `agent-credential.dat`.
