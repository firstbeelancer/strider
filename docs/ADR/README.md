# Architecture Decision Records (ADR)

This directory contains Architecture Decision Records for Strider Mail, following
the Michael Nygard template (http://thinkrelevance.com/blog/2011/11/15/documenting-architecture-decisions).

## What is an ADR?

An ADR captures a single architectural decision: what was decided, why, what
alternatives were considered, and what consequences follow. ADRs are immutable
once Accepted — if a decision is reversed, create a new ADR that supersedes the
old one and update the old ADR's status to "Superseded by ADR-XXXX".

## When to write an ADR

Write an ADR whenever you make a decision that:

- Affects multiple components or layers
- Is hard to reverse (would require significant refactoring)
- Has non-obvious trade-offs (multiple reasonable options)
- Future contributors need to understand "why this way, not the other"

Examples: choosing a framework, deciding a lifecycle strategy, picking a
cryptographic library, settling a naming convention.

## Template

```markdown
# ADR-NNNN: Title

## Status
Proposed | Accepted | Deprecated | Superseded by ADR-XXXX

## Date
YYYY-MM-DD

## Context
What is the problem? What constraints exist? What options are being considered?

## Decision
What did we decide? Be specific and unambiguous.

## Consequences
Positive: ...
Negative: ...
Neutral: ...

## Alternatives Considered
1. Alternative A — why not
2. Alternative B — why not

## References
- Links to discussions, issues, PRs, external articles
```

## Index

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [ADR-0001](ADR-0001-avalonia-over-electron.md) | Choose Avalonia 11 over Electron/MAUI | Accepted | 2026-06-24 |
| [ADR-0002](ADR-0002-per-account-imap-gateway.md) | Per-account IMAP gateway lifecycle (factory pattern) | Accepted | 2026-06-25 |
| [ADR-0003](ADR-0003-sqlcipher-database-encryption.md) | SQLCipher for at-rest database encryption | Accepted | 2026-06-25 |
| [ADR-0004](ADR-0004-anglesharp-html-sanitizer.md) | AngleSharp allowlist for HTML sanitization | Accepted | 2026-06-25 |
| [ADR-0005](ADR-0005-embedded-resource-migrations.md) | Embedded resource SQL migrations vs FluentMigrator | Accepted | 2026-06-25 |
| [ADR-0006](ADR-0006-tiptap-webview-editor.md) | TipTap + WebView for rich text editor | Accepted | 2026-06-24 |
| [ADR-0007](ADR-0007-bouncycastle-pgp.md) | BouncyCastle for PGP (vs GnuPG subprocess) | Accepted | 2026-06-24 |
| [ADR-0008](ADR-0008-ihttpclientfactory-for-ai.md) | IHttpClientFactory for AI gateways | Accepted | 2026-06-25 |
| [ADR-0009](ADR-0009-in-memory-event-bus.md) | ConcurrentDictionary-based in-process event bus | Accepted | 2026-06-24 |
| [ADR-0010](ADR-0010-sync-db-init.md) | Synchronous DB initialization in App.OnFrameworkInitializationCompleted | Accepted | 2026-06-24 |
