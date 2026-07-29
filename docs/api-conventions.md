# API conventions

Этот документ фиксирует минимальные HTTP-конвенции ServerPilot MVP. Они применяются ко всем controller-based API endpoint.

## Контракты

- Внешние request и response models объявляются отдельными типами в API-проекте.
- EF Core entities и domain entities не возвращаются напрямую.
- Для простых правил полей используются Data Annotations (`Required`, `StringLength`, `Range` и другие встроенные атрибуты).
- Контроллеры помечаются `[ApiController]`, поэтому некорректная модель автоматически возвращает `400 Bad Request`.
- Правила, зависящие от нескольких ресурсов или текущего состояния системы, проверяются в Application/Domain, а не в атрибутах или контроллерах.

## Ошибки

Ответы с ошибкой используют `application/problem+json` и стандартную модель Problem Details:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "instance": "/api/example",
  "correlationId": "request-7f88a03a",
  "errors": {
    "Name": ["The Name field is required."]
  }
}
```

`errors` присутствует только для ошибок валидации. `detail` разрешён для безопасного пользовательского описания ожидаемой ошибки, но не для текста неожиданного исключения.

| Случай | HTTP status | Источник |
|---|---:|---|
| Некорректный request contract | 400 | автоматическая валидация `[ApiController]` |
| Не выполнена аутентификация | 401 | authentication middleware или `Unauthorized()` |
| Недостаточно прав | 403 | authorization middleware или `Forbid()` |
| Ресурс отсутствует или его существование нельзя раскрывать чужому владельцу | 404 | owner-scoped query и `NotFound()` |
| Ресурс не найден | 404 | `NotFound()` |
| Конфликт с текущим состоянием | 409 | `Conflict()` |
| Неожиданная ошибка | 500 | exception handling middleware |

Необработанные исключения логируются сервером, но их сообщения и stack traces не возвращаются клиенту. Контроллер не должен ловить исключение только ради формирования HTTP-ответа.

## Correlation ID

- Клиент может передать один заголовок `X-Correlation-ID` длиной до 64 символов.
- Допустимы ASCII-буквы, цифры, `-`, `_` и `.`.
- Если заголовок отсутствует или некорректен, API создаёт новый идентификатор.
- API всегда возвращает итоговый `X-Correlation-ID` в response header.
- Problem Details содержит тот же идентификатор в поле `correlationId`.
- Логи, созданные во время обработки запроса, получают structured scope `CorrelationId`.

Correlation ID предназначен для поиска связанных логов и не заменяет идентификаторы пользователя, Agent, ServerInstance или команды.

### Command correlation

При создании ServerCommand API генерирует и сохраняет отдельный UUID `CorrelationId`.
Он добавляется в structured scope при создании и выдаче команды, передаётся Agent в
claim response и возвращается в заголовке при state, start и terminal-result reports.
Agent открывает command scope с `AgentId`, `ServerInstanceId`, `CommandId` и
`CorrelationId`, поэтому логи supervisor и command lifecycle можно связать с одной
сохранённой командой. Этот идентификатор не заменяет request-level
`X-Correlation-ID` исходного пользовательского HTTP-запроса.

Логи содержат только идентификаторы и безопасные коды. В них нельзя писать credentials,
токены, authorization headers, пароли, пути, аргументы запуска, request bodies или raw
failure details.

## Authentication schemes

- Пользовательские endpoint принимают `Authorization: Bearer <jwt>` и получают user ID
  только из проверенного `sub` claim.
- Agent endpoint явно используют Agent policy и принимают
  `Authorization: Agent <credential>`.
- Схемы не взаимозаменяемы: Agent credential не даёт пользовательских прав, а JWT
  пользователя не идентифицирует Agent.
- Raw credentials возвращаются только один раз и помечаются `Cache-Control: no-store`.

## Cursor pagination

- Изменяемые списки, для которых смещение может дать пропуски или дубликаты, используют
  непрозрачный cursor вместо номера страницы.
- Ответ имеет поля `items` и `nextCursor`; `nextCursor` равен `null`, когда продолжения
  нет.
- Клиент передаёт полученное значение без изменения в query-параметре `cursor`.
- Некорректный, неподдерживаемый или устаревший cursor возвращает `400 Problem Details`.
- История ServerCommand упорядочена по `(createdAt DESC, id DESC)`, поэтому одинаковые
  временные метки не нарушают границу страницы.
