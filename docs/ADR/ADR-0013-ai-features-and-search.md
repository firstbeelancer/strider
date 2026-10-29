# ADR-0013: AI assistant in Infrastructure, structured search in SQL

Date: 2026-10-29 · Status: Accepted

## Context

Two v0.1 "must have" blocks from ТЗ §5.1 existed only on paper:

- **AI features (F5.4–F5.8).** Gateways (`OpenAiCompatibleGateway`, `AnthropicGateway`),
  `PromptTemplates` and the `ai_settings` table existed, but nothing called them: no
  store, no service, no UI. The `AiGatewayFactory` also lived in `Strider.UI`, which
  meant the Infrastructure layer could not use it.
- **Search (F2.8).** The toolbar box was not bound to anything; `SearchAsync` did a
  `LIKE` over subject/sender only and ignored the documented syntax.

At the same time the search box needs to stay useful on a 10 000-message mailbox, and
the AI features must never send mail anywhere unless the user configured a provider.

## Decision

**AI**

1. `IAiSettingsStore` + `SqliteAiSettingsStore` persist provider/model/base URL.
   The API key is **only** referenced (`ai_settings.api_key_ref` → `strider:ai:{id}:api_key`);
   the secret lives in DPAPI / libsecret. Guarded by a test.
2. `IAiGatewayFactory` moved from `Strider.UI` to `Strider.Infrastructure.Ai.AiGatewayFactory`.
3. `IAiAssistant` / `AiAssistant` implement summarise, draft reply, classify and improve
   on top of the gateways, with a hard cap on prompt size (`MaxPromptChars = 12 000`)
   and a thread window (`MaxThreadMessages = 12`).
4. Usage is reported as tokens + estimated cost (`ModelPricing`) so F5.8 is visible in
   the UI without a separate billing screen.
5. Missing configuration is a first-class state: `AiNotConfiguredException` is caught by
   every command and turned into a hint, never into a crash or a spinner.

**Search**

1. `Strider.Core.Search.MessageQuery` parses the whole syntax (`from: to: subject: body:
   is:unread is:starred has:attachment`, quoted values, repeatable fields, bare text).
2. `IMessageStore.SearchMessagesAsync` translates the query into one `LIKE`-based SQL
   statement, joining `message_bodies` **only** when the query needs body text.
3. User input is escaped for `LIKE` (`%`, `_`, `\`) so `50%` and `a_b` behave literally.

## Alternatives

- **FTS5 virtual table for search.** Faster on very large mailboxes, but the schema
  migration, the trigger maintenance and the tokeniser (Cyrillic!) are a separate piece
  of work. `LIKE` on the existing indexes is enough for the MVP volumes (300 messages
  per folder on first sync) and keeps the shape of the query builder for the later FTS
  implementation — only the SQL generation changes.
- **AI calls from the ViewModels.** Rejected: it would duplicate prompt building, keep
  keys closer to the UI and make the behaviour untestable without a window.
- **Streaming responses.** Needs a different gateway contract; not worth it for
  summaries of a few hundred tokens.

## Consequences

- 68 new unit tests (query parser, search SQL against real SQLite, AI assistant with a
  fake provider, AI settings store).
- The AI panel is present in the reader *and* the composer; both degrade to a hint when
  nothing is configured.
- Search covers cached bodies only — a body that was never opened is not searched until
  it is downloaded. Documented in Техдолг.
