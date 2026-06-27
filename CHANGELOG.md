# Changelog

All notable changes to Strider Mail will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Initial project specification (SPEC.md)
- Design system (DESIGN_SYSTEM.md + DESIGN_SYSTEM_EXTRAS.md)
- Architecture documentation
- MIT License
- Contributing guidelines
- CI/CD pipeline (GitHub Actions)
- .gitignore with security rules

### Added (architecture review by ZAI, 2026-06-25)

- **DpapiKeychainService** — Windows DPAPI-based implementation of `IKeychainService`
  via CryptProtectData/CryptUnprotectData P/Invoke (F-005).
- **LibsecretKeychainService** — Linux implementation using `secret-tool` CLI with
  plaintext-file fallback (chmod 600) when gnome-keyring is unavailable (F-005).
- **IImapGatewayFactory** + **MailKitImapGatewayFactory** — per-account IMAP gateway
  lifecycle (F-007). Each account gets its own `MailKitImapGateway` instance with its
  own `ImapClient`, enabling parallel connections to different accounts without blocking.
- **ISmtpGatewayFactory** — factory for short-lived SMTP gateways sharing keychain dep.
- **HtmlSanitizer** — AngleSharp allowlist-based HTML sanitizer for email rendering (F-006).
  Removes `<script>`, `<iframe>`, `<object>`, `<embed>`, `<form>`, inline event handlers,
  `javascript:` URLs. Blocks external images by default (privacy).
- **DatabaseInitializer** rewritten — now applies migrations from embedded `.sql`
  resources, tracks schema version in `schema_migrations` table (F-013, F-018).
- **Migrations/0001_initial.sql** — initial schema as embedded resource (F-013).
- **Unit tests** — 39 tests covering domain models, DatabaseInitializer, and
  HtmlSanitizer (F-015 smoke pass).

### Changed (architecture review by ZAI, 2026-06-25)

- **Pinned dependency versions** — wildcard `8.*`/`11.*`/`4.*` replaced with concrete
  versions (BouncyCastle 2.3.0, MailKit 4.5.0, Microsoft.Data.Sqlite 8.0.7, etc.) for
  reproducible builds. Lock-file generation enabled via `RestorePackagesPath`.
- **MailKitImapGateway** — credentials now read from `IKeychainService` at auth time,
  no longer from `account.SyncState` (F-002). The `SyncState` field is reserved for its
  intended purpose (JSON of last UIDs per folder).
- **MailKitSmtpGateway** — same keychain-based auth fix as IMAP (F-002).
- **MailKitImapGateway constructor** — now accepts `(IKeychainService, IAccountStore,
  Guid accountId)` for per-account lifecycle (F-007).
- **App.axaml.cs DI** — all services now registered (`IKeychainService`, `IPgpService`,
  `IImapGatewayFactory`, `ISmtpGateway`, `IAiGateway` via IHttpClientFactory, `HtmlSanitizer`,
  all ViewModels). Previously commented out as TODO (F-001).
- **Program.cs** — duplicate `ConfigureServices()` removed (F-016). DI configuration is
  now centralized in `App.ConfigureServices()` as the single source of truth.
- **MessageReaderViewModel** — HTML body now passed through `HtmlSanitizer` before
  display (F-006). Was previously assigned raw, creating XSS risk.
- **MainWindowViewModel** — `AddAccount`, `ComposeNew`, `OpenSettings` now resolve
  ViewModels from `IServiceProvider` instead of `new`-ing with null dependencies (F-001).
- **AccountWizardViewModel** — test connection now uses `IImapGatewayFactory.ForAccount`
  with a temporary GUID, stores password in keychain under temp id, cleans up after test.
- **Infrastructure.csproj** — added `AngleSharp 1.0.7`, `Microsoft.Extensions.Http 8.0.0`.
  Embedded resources for `Persistence/Migrations/*.sql`.

### Fixed (architecture review by ZAI, 2026-06-25)

- **F-003** — Removed duplicate `MainWindow.cs` (was conflicting with AXAML-generated
  `MainWindow.axaml.cs`). All window properties now set in AXAML only.
- **F-001** — Application no longer crashes with `NullReferenceException` when user
  clicks "Add Account" — all dependencies are now properly registered in DI.
- **F-002** — IMAP/SMTP authentication no longer uses `SyncState` as password (was a
  hack that broke sync entirely).
- **CA1416 warning** — `File.SetUnixFileMode` call in `LibsecretKeychainService` now
  guarded with `OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()`.

### Security

- All credentials (account passwords, OAuth2 tokens, AI API keys) now flow through
  `IKeychainService` and are stored in OS keychain (DPAPI on Windows, libsecret on
  Linux). Never in plaintext, never in logs.
- HTML email content sanitized through AngleSharp allowlist before rendering. No
  `<script>`, `<iframe>`, inline event handlers, or `javascript:` URLs.
- External images in HTML email blocked by default (privacy / tracking pixel defense).
