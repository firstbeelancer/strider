# ADR-0008: IHttpClientFactory for AI gateways

## Status
Accepted

## Date
2026-06-25

## Context
The original `OpenAiCompatibleGateway` and `AnthropicGateway` (commit 62f2363)
created `HttpClient` instances directly in their constructors:

```csharp
_httpClient = new HttpClient();
```

This is a well-known .NET anti-pattern: `HttpClient` is designed to be
long-lived and shared, but `new HttpClient()` per instance leads to
**socket exhaustion** under load (sockets remain in `TIME_WAIT` for ~4 minutes
after disposal, eventually exhausting the ephemeral port range).

For an AI gateway that might handle 100+ requests per minute (e.g., classifying
a batch of incoming emails), this would cause the app to hang within hours.

The architecture review (F-011) flagged this as a MEDIUM-severity issue.

## Decision
Use **`IHttpClientFactory`** (via `Microsoft.Extensions.Http 8.0.0`) to manage
`HttpClient` lifetimes. Gateways accept `HttpClient` via DI, and DI provides
pooled instances managed by the factory.

## Consequences

Positive:
- No socket exhaustion — factory reuses `HttpMessageHandler` instances across
  `HttpClient` creations
- Centralized configuration — timeout, retry policy, default headers can be
  set in one place (DI registration)
- Future-ready for Polly integration (circuit breaker, retry on 429)
- Standard .NET pattern — well-documented, familiar to contributors

Negative:
- Adds `Microsoft.Extensions.Http` dependency (~500 KB)
- Slightly more complex DI registration (`AddHttpClient<T>` instead of
  `AddSingleton<T>`)
- `HttpClient` from factory should not be cached by the consumer (we don't)

Neutral:
- `IHttpClientFactory` is the Microsoft-recommended pattern since .NET Core 2.1

## Alternatives Considered

1. **Singleton `HttpClient`** — rejected: works for socket exhaustion but
   prevents DNS rotation (if a user's custom endpoint changes IP, the singleton
   keeps the stale connection). Factory rotates handlers every 2 minutes by
   default.
2. **Static `HttpClient` per gateway type** — rejected: same DNS issue as
   singleton; also harder to test (no DI injection).
3. **Manual handler pooling** — rejected: reinventing `IHttpClientFactory`
   for no benefit.

## References
- Architecture review by ZAI (finding F-011)
- `src/Strider.UI/App.axaml.cs` (DI registration)
- Microsoft docs: https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory
