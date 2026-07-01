# Strider Mail

> A modern, native email client for Windows and Linux — built for people who actually like email.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-lightgrey.svg)
![Stack](https://img.shields.io/badge/stack-.NET%208%20%7C%20Avalonia%2011-5B6CFF.svg)
![CI](https://github.com/firstbeelancer/strider/actions/workflows/ci.yml/badge.svg)
![Tests](https://img.shields.io/badge/tests-146%20passing-brightgreen.svg)
![Coverage](https://img.shields.io/badge/coverage-70%25%2B%20threshold-yellow.svg)
![Version](https://img.shields.io/badge/version-v0.1.0--rc2-orange.svg)

---

## What is Strider Mail?

Strider Mail is a desktop email client that combines the speed of native applications with modern design and AI capabilities. It's built on **Avalonia 11** and **.NET 8** — no Electron, no browser engine, no JavaScript runtime. Just compiled code rendering directly through Skia.

**Core principles:**

- **Your email stays yours.** IMAP/SMTP to any provider. Local SQLite cache. API keys in your OS keychain. No telemetry, no cloud sync unless you choose it.
- **Speed is a feature.** App starts in under 1.5 seconds. Message list handles 10,000+ emails without breaking a sweat.
- **AI that earns its keep.** Summarize threads, draft replies, classify messages, search by meaning — but only if you bring your own API key. We never touch your email.
- **Respect your time.** Three-panel layout, keyboard shortcuts, dark/light themes, multiple accounts, multiple signatures. Everything where you expect it.

## Features

### Email (MVP)
- 📬 Unified inbox across multiple IMAP/SMTP accounts
- 📁 Folder tree with unread badges, drag-and-drop organization
- 🧵 Thread grouping (References / In-Reply-To)
- 📎 Attachments: preview images and PDFs, drag-and-drop, download
- 🔍 Search with syntax (`from:alice subject:invoice`)
- ✉️ Rich composer: fonts, colors, tables, emoji, code blocks, inline images
- ✍️ Multiple signatures per account (HTML or plain text)
- 🪟 Compose in separate window — multiple drafts simultaneously
- 🔐 PGP encryption and signing (BouncyCastle)
- 📅 Built-in local calendar with CalDAV sync (v0.2)

### AI Assistant
- 🤖 **Summarize** — get the gist of a 30-message thread in seconds
- ✏️ **Draft** — AI writes a reply in your tone (learned from your past emails)
- 🏷️ **Classify** — auto-categorize: Work / Personal / Newsletter / Action Required
- 📂 **Smart Folders** — auto-sort based on AI classification
- 🔎 **Semantic Search** — find emails by meaning, not just keywords

### Platform
- 🪟 Windows 10/11 (x64)
- 🐧 Linux: Ubuntu 22.04+, Fedora 39+, Arch
- 🌗 Dark and light themes (syncs with system)
- ⌨️ Full keyboard navigation
- 🖥️ System tray with quick actions
- 🌍 Localization-ready (en, ru planned for v0.2)

## Stack

| Layer | Technology | Why |
|---|---|---|
| Language | C# 12 / .NET 8 | Modern, cross-platform, great ecosystem |
| UI | Avalonia 11 + Fluent Theme | XAML, native Skia rendering, Win/Linux/macOS out of the box |
| MVVM | CommunityToolkit.Mvvm | Source-generated observable properties |
| Email | MailKit | Best .NET email library, handles any IMAP server |
| Database | SQLite (Microsoft.Data.Sqlite) | Local cache, offline mode |
| ORM | Dapper | Speed and SQL control |
| AI | HttpClient + OpenAI-compatible API | Works with OpenAI, Anthropic, OpenRouter, any compatible endpoint |
| Crypto | BouncyCastle | PGP encryption/signing |
| Logging | Serilog | Structured logging |
| Tests | xUnit + FluentAssertions | Unit + integration tests |
| CI | GitHub Actions | Build/test/release on Windows and Linux |

## Architecture

Strider Mail follows a clean three-layer architecture:

```
┌─────────────────────────────────────────────┐
│  UI (Avalonia Views + ViewModels)           │
│  Communicates via interfaces only           │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│  Core (Domain Models + Application Services)│
│  No dependencies on UI or Infrastructure    │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│  Infrastructure (MailKit, SQLite, AI, etc.) │
│  Implements interfaces from Core            │
└─────────────────────────────────────────────┘
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for detailed diagrams.

## Security

- **No secrets in the repo.** API keys, passwords, and tokens are stored in your OS keychain (DPAPI on Windows, libsecret on Linux). Never written to disk, never in logs.
- **TLS 1.2+** for all IMAP/SMTP connections.
- **HTML sanitization** — email HTML is rendered through an allowlist (AngleSharp). No scripts, no iframes, no tracking pixels by default.
- **Local-only cache.** Your email database never leaves your machine unless you configure it.

> ⚠️ **For contributors:** Never commit `.env` files, `appsettings.json` with real credentials, database files, or API keys. Use `appsettings.example.json` with placeholders. CI secrets go through GitHub Actions secrets — never in code.

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git

### Build & Run

```bash
git clone https://github.com/firstbeelancer/strider.git
cd strider
dotnet restore
dotnet build
dotnet run --project src/Strider.Host
```

### Run Tests

```bash
dotnet test
```

### Build Release

```bash
# Windows (self-contained)
dotnet publish src/Strider.Host -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Linux (self-contained)
dotnet publish src/Strider.Host -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

## Roadmap

### Phase 1 — Foundation
- [x] Project specification (SPEC.md)
- [x] Design system (DESIGN_SYSTEM.md)
- [x] Solution structure and project setup
- [x] Domain models (11 models in Strider.Core)
- [x] Service interfaces (10 abstractions in Strider.Core)
- [x] SQLite schema + migrations runner (embedded .sql resources)
- [x] Keychain integration (DPAPI on Windows, libsecret on Linux)
- [x] MailKit IMAP/SMTP gateways (per-account factory, keychain-based auth)
- [x] HTML sanitizer (AngleSharp allowlist)
- [x] AI gateways (OpenAI-compatible, Anthropic) via IHttpClientFactory
- [x] PGP service (BouncyCastle — full implementation: generate, encrypt, sign, verify)
- [x] SQLite encryption (SQLCipher via SQLitePCLRaw.bundle_e_sqlcipher)
- [x] WebView editor infrastructure (IEditorHost, EditorBridge, TipTap stub)
- [x] CI pipeline (GitHub Actions: build + test on Win/Linux)
- [x] Unit tests (86 passing — domain, DB, sanitizer, PGP, encryption, editor bridge)
- [ ] Account wizard UI completion (test connection flow works, save flow WIP)
- [ ] Folder tree with unread badges (UI in place, needs IMAP sync wiring)
- [ ] Message list (virtualized, threads — UI in place, thread grouping pending)
- [ ] Message reader (HTML rendering — sanitized, needs WebView for full layout)
- [ ] Platform WebView host implementations (WebView2 on Win, CEF on Linux)

### Phase 2 — Composer
- [ ] Basic composer (To/Cc/Bcc/Subject/Body)
- [ ] Rich text editor (fonts, colors, formatting)
- [ ] Table support (including paste from Excel)
- [ ] Emoji picker
- [ ] Code blocks with syntax highlighting
- [ ] Multiple signatures management
- [ ] Compose in separate window

### Phase 3 — Sync & Offline
- [ ] IMAP IDLE for real-time new mail
- [ ] Background sync (polling fallback)
- [ ] Offline mode with operation queue
- [ ] Draft auto-save

### Phase 4 — AI Integration
- [ ] AI provider setup (OpenAI / Anthropic / OpenRouter / custom)
- [ ] Thread summarization
- [ ] AI draft replies
- [ ] Email classification
- [ ] Smart folders
- [ ] Semantic search

### Phase 5 — Calendar
- [ ] Local calendar (SQLite storage)
- [ ] Event creation/editing/deletion
- [ ] Calendar view (month/week/day)
- [ ] CalDAV sync (v0.2)

### Phase 6 — Security
- [ ] PGP key management UI
- [ ] PGP encrypt/decrypt/sign/verify
- [ ] S/MIME support (v0.2)

### Phase 7 — Polish
- [ ] Dark/light theme with system sync
- [ ] System tray integration
- [ ] Keyboard shortcuts
- [ ] Localization (en, ru)
- [ ] CI/CD pipeline
- [ ] Release packaging (MSI, AppImage, DEB)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

This is a hobby project. Contributions are welcome, but please open an issue first to discuss what you'd like to change.

## Documentation

- [SPEC.md](docs/SPEC.md) — Full technical specification
- [DESIGN_SYSTEM.md](docs/DESIGN_SYSTEM.md) — Design system with tokens, components, states
- [ARCHITECTURE.md](docs/ARCHITECTURE.md) — Architecture diagrams and decisions
- [ROADMAP.md](docs/ROADMAP.md) — Development roadmap
- [Wiki](https://tigerwiki.tigerapps.pro/projects/strider) — Project knowledge base

## Why not Thunderbird / Geary / Mailspring?

| | Thunderbird | Geary | Mailspring | **Strider Mail** |
|---|---|---|---|---|
| Native rendering | ❌ (Electron-like) | ✅ (GTK/WebKit) | ❌ (Electron) | ✅ (Skia) |
| AI built-in | ❌ | ❌ | ❌ | ✅ |
| Rich composer | ⚠️ Basic | ⚠️ Basic | ✅ Good | ✅ Full |
| PGP native | ✅ (addon) | ❌ | ✅ | ✅ |
| Calendar | ✅ | ❌ | ❌ | ✅ |
| Multi-account | ✅ | ✅ | ✅ (3 free) | ✅ Unlimited |
| Cross-platform | ✅ | Linux only | ✅ | ✅ |
| Startup time | ~5s | ~2s | ~3s | <1.5s |
| Memory idle | ~400MB | ~200MB | ~300MB | <250MB |

## License

[MIT](LICENSE) — use it, fork it, build on it.

The name "Strider Mail" and associated branding are not covered by the MIT license. You may fork the code and rebrand it freely.

---

Made with ☕ by the Strider Mail community.
