# Architecture

> Detailed architecture documentation for Strider Mail.
> See also: [SPEC.md](SPEC.md) §4 for the high-level overview.

---

## Layers

### UI Layer (Strider.UI)
- Avalonia 11 views (XAML)
- ViewModels using CommunityToolkit.Mvvm
- WebView host for rich text editor (TipTap)
- Custom controls (FolderTree, MessageList, MessageReader, etc.)
- Resources (colors, typography, spacing, component styles)

### Core Layer (Strider.Core)
- Domain models (pure C#, no dependencies)
- Service interfaces (abstractions)
- Application services (business logic)
- No references to UI or Infrastructure

### Infrastructure Layer (Strider.Infrastructure)
- MailKit IMAP/SMTP gateways
- SQLite persistence (Dapper)
- AI provider clients (OpenAI, Anthropic)
- PGP operations (BouncyCastle)
- OS keychain integration (DPAPI/libsecret)
- WebView editor bridge (JS↔C#)

## Dependency Injection

```
Host → configures DI container
  ├── UI services (transient/scoped)
  ├── Core services (singleton/transient)
  └── Infrastructure implementations (singleton)
      ├── IImapGateway → MailKitImapGateway
      ├── ISmtpGateway → MailKitSmtpGateway
      ├── IMessageStore → SqliteMessageStore
      ├── IAiGateway → OpenAiCompatibleGateway / AnthropicGateway
      ├── IKeychainService → DpapiKeychainService / LibsecretKeychainService
      ├── IPgpService → BouncyCastlePgpService
      └── IEventBus → InMemoryEventBus
```

## Data Flows

See SPEC.md §4.3 and §4.4 for detailed data flow diagrams.

## Decisions

Architecture Decision Records (ADRs) are stored in `docs/ADR/`.
