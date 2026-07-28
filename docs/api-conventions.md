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
