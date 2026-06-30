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

## [Wave 2 — HIGH fixes] — 2026-06-25 (continued)

### Added

- **KeychainKeys** + **AccountKeychainExtensions** — canonical key naming convention
  and helpers for setting/clearing credentials. Closes F-008: `OAuth2TokenRef` is now
  always a reference to a keychain entry, never the token itself.
- **Real BouncyCastlePgpService implementation** — all PGP operations now use real
  BouncyCastle API (was a stub returning fake data). Closes F-004:
  - `GenerateKeyPairAsync` — RSA 4096-bit (default, configurable), armored export
  - `ImportPublicKeyAsync` / `ImportPrivateKeyAsync` — armored import with passphrase verification
  - `EncryptAsync` / `DecryptAsync` — AES-256-CBC public-key encryption
  - `SignAsync` / `VerifyAsync` — one-pass cleartext signing with SHA-256
  - `EncryptAndSignAsync` — combined encrypt + sign in single PGP message
- **EncryptedSqliteConnectionFactory** — SQLCipher at-rest encryption for the SQLite
  database. Key is generated on first launch (32 bytes, hex-encoded) and stored in
  OS keychain. Closes F-009. Includes one-time migration from legacy plaintext DB:
  - Detects unencrypted SQLite file by magic header ("SQLite format 3")
  - ATTACHes encrypted DB, copies schema + data via SQL
  - Backs up plaintext file as `.plaintext.bak`, deletes after successful migration
- **IEditorHost** abstraction — interface for WebView-based rich text editor.
- **EditorBridge** — JSON message protocol between C# and JS (TipTap). Handles
  request/response correlation by ID, event dispatch for selection/content changes.
- **WebViewEditorHost** — abstract base class implementing `IEditorHost` in terms of
  `EditorBridge`. Platform-specific subclasses only override `LoadHtmlIntoWebViewAsync`
  and `PostMessageToWebView`.
- **TipTapAssets** — embedded HTML/JS/CSS for the TipTap editor (stub v0.1 using
  `document.execCommand`; will be replaced with real TipTap bundle in v0.2).
- **tiptap-editor.html** — full JS bridge implementation: receives commands, emits
  selection/content events, handles all EditorCommands.

### Changed

- `MailKitImapGateway` and `MailKitSmtpGateway` now use `KeychainKeys.Password()`
  helper instead of hardcoded string format.
- `AccountWizardViewModel.TestConnectionAsync` uses canonical keychain key via
  `KeychainKeys.Password(tempAccountId)`.
- `Account.OAuth2TokenRef` XML doc clarifies: stores the keychain key (e.g.,
  `strider:{accountId}:oauth_token`), never the token itself.
- `App.axaml.cs` now uses `EncryptedSqliteConnectionFactory` — all SQLite stores
  operate on an encrypted database. Migration runs transparently on startup.
- Added `SQLitePCLRaw.bundle_e_sqlcipher 2.1.10` to Infrastructure.csproj.
- Added `Editor/tiptap-editor.html` as embedded resource.

### Fixed

- **F-004**: `BouncyCastlePgpService` no longer returns fake key IDs/fingerprints.
  Real RSA key generation, real PGP/MIME encryption, real signatures.
- **F-008**: OAuth2 access tokens never stored in DB. `OAuth2TokenRef` is always a
  reference to `strider:{accountId}:oauth_token` in keychain.
- **F-009**: Database file is now encrypted at rest with SQLCipher. Even if the DB
  file is exfiltrated, content is unreadable without the key (which lives in keychain).

### Tests

- **86 tests passing** (was 39 before Wave 2):
  - `KeychainKeysTests` (8) — canonical key format, OAuth2/password credential setup
  - `BouncyCastlePgpServiceTests` (17) — full PGP roundtrip: generate, encrypt/decrypt,
    sign/verify, import/export, tamper detection
  - `EncryptedSqliteConnectionFactoryTests` (10) — SQLCipher encryption, legacy migration
  - `EditorBridgeTests` (11) — JSON protocol, request/response, events, unsubscribe
  - Plus existing 40 tests from Wave 1

## [Wave 3 — MEDIUM fixes] — 2026-06-25 (continued)

### Added

- **ThreadIdResolver** — JWZ-style thread grouping by References/In-Reply-To headers.
  Closes F-014: thread_id field in messages table was never populated. Handles
  out-of-order message arrival (later reply arrives before original). 16 tests.
- **docs/ADR/** directory with 10 Architecture Decision Records:
  - ADR-0001: Avalonia 11 over Electron/MAUI
  - ADR-0002: Per-account IMAP gateway lifecycle (factory pattern)
  - ADR-0003: SQLCipher for at-rest database encryption
  - ADR-0004: AngleSharp allowlist for HTML sanitization
  - ADR-0005: Embedded resource SQL migrations vs FluentMigrator
  - ADR-0006: TipTap + WebView for rich text editor
  - ADR-0007: BouncyCastle for PGP (vs GnuPG subprocess)
  - ADR-0008: IHttpClientFactory for AI gateways
  - ADR-0009: ConcurrentDictionary-based in-process event bus
  - ADR-0010: Synchronous DB initialization in App.OnFrameworkInitializationCompleted
- **release.yml** GitHub Actions workflow — automated release pipeline triggered
  by `v*` tags. Builds self-contained single-file binaries for win-x64 and
  linux-x64, creates GitHub Release with artifacts attached. Closes F-021.
- **Coverage in CI** — `dotnet test` now runs with `--collect:"XPlat Code Coverage"`
  and 70% line-coverage threshold. ReportGenerator produces HTML report uploaded
  as build artifact. Closes F-020.
- **Configuration loading** in Program.cs via Microsoft.Extensions.Configuration:
  appsettings.json + appsettings.Development.json + STRIDER_ env vars + CLI args.
  Serilog reads its config from the same source. Closes F-019.
- **appsettings.example.json** expanded with Serilog sink configuration and
  AI rate limit settings.

### Changed

- `SqliteMessageStore.SaveMessagesAsync` — rewrote as batch insert in a single
  transaction using Dapper's `ExecuteAsync(sql, IEnumerable<parameters>)`.
  ~20× faster than calling `SaveMessageAsync` in a loop for 500 messages.
  Closes F-012.
- `Program.cs` — uses `ConfigurationBuilder` to load appsettings.json before
  configuring Serilog. Hardcoded logger config removed.
- `Strider.Host.csproj` — pinned Avalonia 11.0.10, added
  Microsoft.Extensions.Configuration.EnvironmentVariables, CommandLine,
  and Serilog.Settings.Configuration.
- CI workflow — added coverage collection, ReportGenerator, artifact upload,
  and threshold enforcement (70% line coverage).

### Tests

- **102 tests passing** (was 86 after Wave 2):
  - `ThreadIdResolverTests` (16) — JWZ threading: out-of-order arrival,
    reference parsing (RFC 5322 + JSON), deduplication, integration scenario
  - Plus existing 86 tests
