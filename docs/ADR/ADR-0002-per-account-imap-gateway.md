# ADR-0002: Per-account IMAP gateway lifecycle (factory pattern)

## Status
Accepted

## Date
2026-06-25

## Context
The original `MailKitImapGateway` (commit 62f2363) was registered as a DI singleton
with a single `ImapClient _client` field. When a user adds a second email account
and switches between them, `ConnectAsync` of the second account overwrites `_client`,
severing the first account's connection. This makes parallel IDLE listeners
impossible and forces reconnection on every account switch.

The architecture review (F-007) flagged this as a HIGH-severity issue blocking
parallel multi-account sync.

## Decision
Introduce `IImapGatewayFactory.ForAccount(Guid accountId) → IImapGateway`.
The factory holds a `ConcurrentDictionary<Guid, MailKitImapGateway>` and returns
the same gateway instance for repeated calls with the same accountId. Each
gateway owns its own `ImapClient`. When an account is deleted, call
`factory.Release(accountId)` to dispose its gateway.

## Consequences

Positive:
- Multiple accounts can hold open IMAP connections simultaneously
- IDLE listeners can run in parallel for all accounts
- Account switch is instant — no reconnect
- Clean disposal semantics via `Release` and `IDisposable` on the factory

Negative:
- Each open IMAP connection consumes ~1-2 MB of memory (10 accounts ≈ 20 MB)
- More complex DI story — ViewModels need `IImapGatewayFactory`, not `IImapGateway`
- Gateway disposal must be wired into account deletion flow

Neutral:
- SMTP remains transient (no factory) because SMTP connections are short-lived
  (connect → send → disconnect per RFC 5321)

## Alternatives Considered

1. **Singleton with connection pool** — rejected: MailKit's `ImapClient` is not
   designed for pool semantics; IDLE state is per-connection and cannot be
   shared across "virtual" connections.
2. **Transient per operation** — rejected: `ImapClient.Connect` takes 200-500 ms;
   reconnecting on every fetch/IDLE check would destroy UX.
3. **Background service holding all connections** — deferred: would require
   a `MailSyncService` that owns the factory (planned for v0.2).

## References
- Architecture review by ZAI (finding F-007)
- `src/Strider.Infrastructure/Mail/ImapGatewayFactory.cs`
- SPEC.md §4.3 (Data Flow: Receiving New Email)
