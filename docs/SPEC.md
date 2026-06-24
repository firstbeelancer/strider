# Strider Mail — Technical Specification

> Cross-platform (Windows / Linux) desktop email client with integrated AI assistant.
> Version: **v0.1.0 draft** | Date: **24 June 2026**
> License: **MIT**

---

## 1. Product Vision

Strider Mail is a fast, beautiful, and private email client for people who:

- Want a **unified inbox** from multiple accounts (IMAP/SMTP of any provider)
- Value a **native experience** on Windows and Linux (no Electron, no browser engine inside)
- Want a built-in **AI assistant** that summarizes threads, helps write replies, classifies emails, and searches by meaning — not just keywords
- Don't want to send their emails to someone else's cloud for AI (the AI can be cloud-based, user's choice — OpenAI, Anthropic, OpenRouter, any compatible provider)

### 1.1 Non-goals (what Strider Mail does NOT do in MVP)

- No own email server. Client only.
- No Exchange ActiveSync in MVP (IMAP/SMTP only).
- No mobile version (desktop only).
- No own AI provider. User brings their own API key.
- No telemetry or analytics of any kind.

---

## 2. Target Audience

| Segment | Pain | What Strider Mail solves |
|---|---|---|
| Developers | Spam in main inbox, dozens of newsletters, searching old threads | AI classification, semantic search, category folders |
| Product managers | 200+ emails/day, 30+ message threads | Thread summaries, draft replies in user's style |
| Freelancers | Multiple emails (work, personal), need to keep separate | Unified inbox + per-account rules |
| Privacy-conscious | Don't trust Gmail client, want local cache | Local SQLite, no sync to our server |

---

## 3. Technology Stack

### 3.1 Core

| Layer | Technology | Rationale |
|---|---|---|
| Language | **C# 12 / .NET 8** | Modern, cross-platform, excellent ecosystem |
| UI framework | **Avalonia 11** + Fluent Theme | XAML, native Skia rendering, Win/Linux/macOS out of the box |
| MVVM | **CommunityToolkit.Mvvm** | Source-generated observable properties, minimal boilerplate |
| IMAP/SMTP | **MailKit** | Best .NET email library, handles any server quirks |
| Database | **SQLite** (Microsoft.Data.Sqlite) | Local email cache, offline mode |
| ORM | **Dapper** | Speed and full SQL control |
| DI/Config | **Microsoft.Extensions.Hosting** | Standard, no surprises |
| HTTP | **HttpClient** + **System.Text.Json** | For AI API |
| Logging | **Serilog** | Structured logging, file and console sinks |
| Tests | **xUnit** + **FluentAssertions** + **Avalonia.Headless** | Unit tests + UI tests without display |
| CI | **GitHub Actions** | Build/test/release on Windows and Linux |
| PGP | **BouncyCastle** | Battle-tested .NET crypto library |

### 3.2 Rich Text Editor

The composer requires a full-featured rich text editor. Avalonia does not have a built-in one.

**Chosen approach: Embedded WebView with TipTap**

| Platform | Engine | Why |
|---|---|---|
| Windows | WebView2 (Edge/Chromium) | Ships with Windows 10/11, zero install |
| Linux | CEF (Chromium Embedded Framework) | Consistent cross-distro rendering |

**Why TipTap over TinyMCE/Quill:**
- Headless — full control over UI (we draw our own toolbar)
- Extensible — tables, code blocks, emoji are first-class extensions
- JSON document model — clean serialization, easy to convert to HTML
- MIT license — compatible with our project

**Why not custom Avalonia editor:**
A full WYSIWYG editor with tables, inline images, emoji, code blocks, and font selection would take 6+ months to build from scratch. The WebView approach gives us production-quality editing immediately.

**Architecture:**

```
┌────────────────────────────────────────────┐
│  Avalonia UI                               │
│  ┌──────────────────────────────────────┐  │
│  │  Custom Toolbar (native Avalonia)    │  │
│  │  Font | Size | Color | B I U | ...   │  │
│  └──────────────────────────────────────┘  │
│  ┌──────────────────────────────────────┐  │
│  │  WebView (WebView2 / CEF)            │  │
│  │  ┌────────────────────────────────┐  │  │
│  │  │  TipTap Editor                 │  │  │
│  │  │  (HTML content editable)       │  │  │
│  │  └────────────────────────────────┘  │  │
│  └──────────────────────────────────────┘  │
│  Communication: JS ↔ C# bridge             │
└────────────────────────────────────────────┘
```

The toolbar is native Avalonia (consistent with the rest of the app). The editing surface is a WebView with TipTap. Communication happens through a JS↔C# message bridge.

### 3.3 Target Platforms

| Platform | Version | Artifact |
|---|---|---|
| Windows | 10 / 11 (x64) | `.exe` (self-contained, single file) + `.msi` installer |
| Linux | Ubuntu 22.04+, Fedora 39+, Arch | `.AppImage` + `.deb` + Flatpak (v0.2) |

Minimum: **.NET 8 runtime** (for non-self-contained) or self-contained build ~80-120 MB (with WebView2 runtime ~50MB additional on first install for Windows).

---

## 4. Architecture

### 4.1 Layers

```
┌─────────────────────────────────────────────────────────────┐
│  Strider.UI (Avalonia Views, XAML, code-behind)             │
│  - Main window, threads, composer, settings                  │
│  - ViewModels (MVVM, CommunityToolkit.Mvvm)                  │
│  - WebView host for rich text editor                         │
└──────────────────┬──────────────────────────────────────────┘
                   │ (interfaces only)
┌──────────────────▼──────────────────────────────────────────┐
│  Strider.Core (Domain + Application services)               │
│  - Account, Mailbox, Message, Thread — domain models        │
│  - MailSyncService, SmtpService, AiService, SearchService   │
│  - CalendarService, SignatureService, PgpService            │
│  - Command/query handlers                                    │
└──────────────────┬──────────────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────────────┐
│  Strider.Infrastructure                                     │
│  - MailKitImapClient, MailKitSmtpClient                      │
│  - SqliteMessageStore, SqliteAccountStore                   │
│  - SqliteCalendarStore, SqliteSignatureStore                │
│  - OpenAiCompatibleClient (OpenAI/Anthropic/OpenRouter)     │
│  - KeychainService (DPAPI on Win, libsecret on Linux)       │
│  - PgpService (BouncyCastle)                                │
│  - WebViewEditorBridge (JS↔C# for TipTap)                   │
└─────────────────────────────────────────────────────────────┘
```

**Dependency rules:**
- UI → Core (only through interfaces from Core/Abstractions)
- Infrastructure → Core (implements interfaces)
- Core → depends on neither UI nor Infrastructure

### 4.2 Top-level Components

```
┌─────────────────────────────────────────────────────────┐
│                     Presentation Layer                  │
│  ┌─────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐ │
│  │ Main    │  │ Compose  │  │ Settings │  │ Account  │ │
│  │ Window  │  │ Window   │  │ Window   │  │ Wizard   │ │
│  └────┬────┘  └─────┬────┘  └─────┬────┘  └─────┬────┘ │
│       └──────────┬──┴──────────────┴─────────────┘      │
│                  │ ViewModels                           │
└──────────────────┼──────────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────────┐
│              Application Services Layer                │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐  │
│  │ SyncSvc  │ │ AiOrch.  │ │ Search   │ │ Calendar │  │
│  └────┬─────┘ └────┬─────┘ └────┬─────┘ └────┬─────┘  │
│       └─────────┬──┴──────────────┴────────────┘       │
│  ┌──────────────▼────────────────────────────────────┐  │
│  │   Domain Models (Account, Folder, Message, etc)   │  │
│  └──────────────────────┬────────────────────────────┘  │
└─────────────────────────┼───────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────┐
│             Infrastructure Layer                       │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐  │
│  │ ImapGw   │ │ SmtpGw   │ │ AiGw     │ │ PgpSvc   │  │
│  │ (MailKit)│ │ (MailKit)│ │ (Http)   │ │ (Bouncy) │  │
│  ├──────────┤ ├──────────┤ ├──────────┤ ├──────────┤  │
│  │ MsgStore │ │ AcctStore│ │ Keychain │ │ CalStore │  │
│  │ (SQLite) │ │ (SQLite) │ │ (DPAPI/  │ │ (SQLite) │  │
│  │          │ │          │ │ libsecret│ │          │  │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘  │
└────────────────────────────────────────────────────────┘
```

### 4.3 Data Flow: Receiving New Email

```
IMAP IDLE → ImapGateway → SyncService
                              │
                              ▼
                    ParseMime (MIME → MessageBody)
                              │
                              ▼
                    MessageStore.Save (SQLite)
                              │
                              ▼
                    AiService.Enrich (if enabled)
                       - classify
                       - embed (for semantic search)
                       - extract-todos
                              │
                              ▼
                    EventBus → ViewModels update
                              │
                              ▼
                    System.NotifyIcon → toast
```

### 4.4 Data Flow: AI Request (Thread Summary)

```
User clicks "Summarize" → ComposeViewModel.SummarizeThread
        │
        ▼
AiService.SummarizeAsync(threadId, promptTemplate)
        │
        ▼
MessageStore.LoadThread(threadId)
        │
        ▼
AiGateway.ChatAsync(messages[]) → OpenAI-compatible API
        │
        ▼
Result → DiffViewer dialog → User accepts/edits
```

### 4.5 Repository Structure

```
strider/
├── src/
│   ├── Strider.Core/                    # Domain + Application services
│   │   ├── Domain/
│   │   │   ├── Account.cs
│   │   │   ├── Mailbox.cs
│   │   │   ├── Folder.cs
│   │   │   ├── Message.cs
│   │   │   ├── MessageBody.cs
│   │   │   ├── Attachment.cs
│   │   │   ├── Thread.cs
│   │   │   ├── Signature.cs
│   │   │   ├── CalendarEvent.cs
│   │   │   ├── PgpKey.cs
│   │   │   └── AiInsights.cs
│   │   ├── Abstractions/
│   │   │   ├── IImapGateway.cs
│   │   │   ├── ISmtpGateway.cs
│   │   │   ├── IMessageStore.cs
│   │   │   ├── IAiGateway.cs
│   │   │   ├── IKeychainService.cs
│   │   │   ├── ICalendarStore.cs
│   │   │   ├── ISignatureStore.cs
│   │   │   ├── IPgpService.cs
│   │   │   └── IEventBus.cs
│   │   ├── Services/
│   │   │   ├── MailSyncService.cs
│   │   │   ├── AiService.cs
│   │   │   ├── SearchService.cs
│   │   │   ├── AccountService.cs
│   │   │   ├── CalendarService.cs
│   │   │   ├── SignatureService.cs
│   │   │   └── PgpService.cs
│   │   └── Strider.Core.csproj
│   │
│   ├── Strider.Infrastructure/
│   │   ├── Mail/
│   │   │   ├── MailKitImapGateway.cs
│   │   │   ├── MailKitSmtpGateway.cs
│   │   │   └── MimeParser.cs
│   │   ├── Persistence/
│   │   │   ├── SqliteMessageStore.cs
│   │   │   ├── SqliteAccountStore.cs
│   │   │   ├── SqliteCalendarStore.cs
│   │   │   ├── SqliteSignatureStore.cs
│   │   │   └── Migrations/
│   │   ├── Ai/
│   │   │   ├── OpenAiCompatibleGateway.cs
│   │   │   ├── AnthropicGateway.cs
│   │   │   └── PromptTemplates.cs
│   │   ├── Security/
│   │   │   ├── DpapiKeychainService.cs     # Windows
│   │   │   ├── LibsecretKeychainService.cs # Linux
│   │   │   └── BouncyCastlePgpService.cs
│   │   ├── Editor/
│   │   │   ├── WebViewEditorHost.cs        # WebView2/CEF host
│   │   │   └── EditorBridge.cs             # JS↔C# communication
│   │   └── Strider.Infrastructure.csproj
│   │
│   ├── Strider.UI/
│   │   ├── App.axaml / App.axaml.cs
│   │   ├── Program.cs
│   │   ├── Views/
│   │   │   ├── MainWindow.axaml
│   │   │   ├── ComposeWindow.axaml
│   │   │   ├── SettingsWindow.axaml
│   │   │   ├── AccountWizardWindow.axaml
│   │   │   ├── Controls/
│   │   │   │   ├── AvatarView.axaml
│   │   │   │   ├── FolderTreeView.axaml
│   │   │   │   ├── MessageListView.axaml
│   │   │   │   ├── MessageReaderView.axaml
│   │   │   │   ├── AiPanelView.axaml
│   │   │   │   ├── ComposerView.axaml
│   │   │   │   ├── RichEditorView.axaml
│   │   │   │   ├── CalendarView.axaml
│   │   │   │   ├── SignatureEditorView.axaml
│   │   │   │   ├── PgpKeyManagerView.axaml
│   │   │   │   ├── AttachmentChip.axaml
│   │   │   │   └── ...
│   │   │   └── Dialogs/
│   │   │       ├── AccountWizardDialog.axaml
│   │   │       ├── FilterDialog.axaml
│   │   │       ├── AiSettingsDialog.axaml
│   │   │       ├── PgpKeyDialog.axaml
│   │   │       ├── SignatureDialog.axaml
│   │   │       └── CalendarEventDialog.axaml
│   │   ├── ViewModels/
│   │   │   ├── MainWindowViewModel.cs
│   │   │   ├── MessageListViewModel.cs
│   │   │   ├── MessageReaderViewModel.cs
│   │   │   ├── ComposeViewModel.cs
│   │   │   ├── SettingsViewModel.cs
│   │   │   ├── CalendarViewModel.cs
│   │   │   ├── PgpKeyManagerViewModel.cs
│   │   │   └── AccountWizardViewModel.cs
│   │   ├── Converters/
│   │   ├── Assets/
│   │   │   ├── Icons/
│   │   │   ├── Fonts/
│   │   │   └── Themes/
│   │   ├── Resources/
│   │   │   ├── Colors.axaml
│   │   │   ├── Typography.axaml
│   │   │   ├── Spacing.axaml
│   │   │   └── Components.axaml
│   │   └── Strider.UI.csproj
│   │
│   └── Strider.Host/
│       ├── Program.cs
│       ├── appsettings.example.json      # ← example, NOT real config
│       └── Strider.Host.csproj
│
├── tests/
│   ├── Strider.Core.Tests/
│   ├── Strider.Infrastructure.Tests/
│   └── Strider.UI.Tests/
│
├── docs/
│   ├── SPEC.md
│   ├── DESIGN_SYSTEM.md
│   ├── ARCHITECTURE.md
│   ├── ROADMAP.md
│   └── ADR/
│
├── .github/
│   └── workflows/
│       ├── ci.yml
│       └── release.yml
│
├── .gitignore
├── LICENSE
├── README.md
├── CONTRIBUTING.md
├── CHANGELOG.md
└── Strider.sln
```

---

## 5. Functional Requirements

### 5.1 MVP (v0.1) — Must Have

#### Accounts
- **F1.1** Add IMAP account via wizard (email, password, IMAP host/port, SMTP host/port, display name)
- **F1.2** Auto-discover settings for popular providers (Gmail, Outlook, Yahoo, Yandex, Mail.ru, iCloud)
- **F1.3** Store passwords in system keychain (DPAPI on Windows, libsecret on Linux)
- **F1.4** OAuth2 support for Gmail and Outlook (XOAUTH2 via MailKit)
- **F1.5** Multiple accounts, switch via sidebar
- **F1.6** Delete/edit account

#### Folders & Messages
- **F2.1** Folder tree with unread badges
- **F2.2** Message list (sender, subject, snippet, date, badges: attachment/starred/important)
- **F2.3** Threads (group by References/In-Reply-To)
- **F2.4** Read message: HTML/plain rendering, inline images, sanitized
- **F2.5** Attachments: view, download, drag-and-drop, preview for images and PDF
- **F2.6** Actions: reply, reply-all, forward, archive, delete, mark read/unread, star, flag
- **F2.7** Folder operations: rename, delete (with confirmation), empty trash
- **F2.8** Search by from/subject/body/to (syntax like `from:alice subject:invoice`)
- **F2.9** Sort: date (default desc), sender, subject, size

#### Synchronization
- **F3.1** Initial sync: last N messages (default 500 per folder) with progress UI
- **F3.2** Background sync via IMAP IDLE (when supported) or polling (default 5 min)
- **F3.3** Offline mode: read from local cache, queue outgoing operations
- **F3.4** Conflicts: operations applied by UIDs, on failure marked for retry
- **F3.5** Full re-sync on demand

#### Composer
- **F4.1** New email, reply, reply-all, forward
- **F4.2** Fields: To, Cc, Bcc, Subject, Body (HTML + plain)
- **F4.3** Attachments: drag-and-drop + file picker + inline attach
- **F4.4** Inline images in HTML (paste from clipboard, drag-and-drop)
- **F4.5** Auto-save draft every 30 seconds
- **F4.6** Send via SMTP with progress
- **F4.7** Undo Send within 5 seconds (UI feature)

#### Rich Text Editor
- **F4.10** Font family selection: system fonts + web fonts (Inter, Roboto, Georgia, etc.)
- **F4.11** Font size: 8–72px with presets (Small / Normal / Large / Huge)
- **F4.12** Font color: color picker with hex input + recent colors palette
- **F4.13** Background color (highlight)
- **F4.14** Text formatting: Bold, Italic, Underline, Strikethrough, Superscript, Subscript
- **F4.15** Paragraph: alignment (left / center / right / justify), line spacing
- **F4.16** Lists: ordered, unordered, nested (indent / outdent)
- **F4.17** Tables: insert, resize columns/rows, merge cells, paste from Excel (preserve formatting)
- **F4.18** Code blocks: syntax-highlighted inline code and block code (language selector)
- **F4.19** Emoji: picker with search and categories
- **F4.20** Links: insert/edit with preview
- **F4.21** Horizontal rule
- **F4.22** WYSIWYG ↔ HTML source toggle
- **F4.23** Undo/Redo
- **F4.24** Paste from Word/Google Docs (clean up garbage HTML)

#### Signatures
- **F4.30** Multiple signatures per account (CRUD)
- **F4.31** Signature editor: HTML rich text or plain text
- **F4.32** Set default signature per account
- **F4.33** Signature selector in compose (dropdown)
- **F4.34** Signature can include: text, HTML formatting, images, links, horizontal rule
- **F4.35** Signatures stored in local SQLite (not synced to server)

#### Compose Window
- **F4.40** Open compose in main window (inline)
- **F4.41** Open compose in separate window
- **F4.42** Multiple separate compose windows simultaneously
- **F4.43** Draft syncs between inline and windowed views
- **F4.44** mailto: links open in compose window

#### AI Features (MVP)
- **F5.1** AI provider setup: choose from OpenAI, Anthropic, OpenRouter, custom OpenAI-compatible endpoint
- **F5.2** Store API key in keychain
- **F5.3** Default model setting (e.g., gpt-4o-mini, claude-haiku)
- **F5.4** **AI-Summary**: "Summarize thread" button → short summary in side panel
- **F5.5** **AI-Draft**: "Draft reply" → AI generates draft in your tone (optionally: load 5 past replies as few-shot)
- **F5.6** **AI-Classify**: on receive, AI categorizes (Work / Personal / Newsletter / Spam-like / Action-required), user can override
- **F5.7** **AI-Smart folders**: auto-folders based on classification
- **F5.8** Show request cost (tokens × model price) in UI

#### Calendar (Local)
- **F6.10** Local calendar stored in SQLite
- **F6.11** Create/edit/delete events (title, description, start/end time, all-day, location, reminders)
- **F6.12** Calendar views: month, week, day
- **F6.13** Drag events to reschedule
- **F6.14** Color-coded event categories
- **F6.15** Quick event from email (extract date/time from message body)
- **F6.16** Calendar notification reminders (system notifications)

#### PGP
- **F6.20** Generate PGP key pair
- **F6.21** Import/export public and private keys
- **F6.22** Key management UI (list, delete, trust levels)
- **F6.23** Encrypt outgoing message (select recipient's public key)
- **F6.24** Decrypt incoming message (with private key from keychain)
- **F6.25** Sign outgoing message
- **F6.26** Verify signature on incoming message
- **F6.27** PGP indicators in message view (lock icon, signature status)
- **F6.28** Keyserver lookup (optional, for finding recipient public keys)

#### UI/UX
- **F6.1** Dark and light theme, sync with system
- **F6.2** Toast notifications for new messages, with action buttons (Mark as read / Open / Archive)
- **F6.3** Shortcuts: j/k (navigate), e (archive), r (reply), c (compose), / (search), Ctrl+Enter (send), Esc (close dialog)
- **F6.4** System tray icon with quick actions (Compose, Show/Hide window)
- **F6.5** Multiple layouts (3-pane / 2-pane / compact), user-switchable
- **F6.6** Three-panel layout: left sidebar (folders + tools), center message list, right message reader

#### Storage & Privacy
- **F7.1** Local DB with encryption (SQLCipher or native SQLite encryption)
- **F7.2** Cache cleanup on schedule (configurable: 7/30/90 days / never)
- **F7.3** Export/import settings (JSON, without passwords)

### 5.2 v0.2 — Should Have

- S/MIME support
- CalDAV sync for calendar
- CardDAV sync for contacts
- Plugin API for custom rules/AI-prompts
- Scheduled send ("send tomorrow at 9:00")
- Snooze ("hide until tomorrow")
- Templates for frequent replies
- Full AI integration: auto-replies by rules, auto-archive newsletters
- Localization (minimum en, ru)
- Flatpak packaging for Linux

### 5.3 v1.0 — Could Have

- Mobile version (Avalonia Mobile)
- E2E sync between devices (via own relay or E2EE storage)
- IDE plugins (email notifications in IDE)

---

## 6. Non-functional Requirements

### 6.1 Performance
- App startup: **< 1.5 sec** on mid-range laptop
- Open list of 10,000 messages (virtualized): **< 200 ms** to first frame
- Search 10,000 messages: **< 300 ms** (local)
- AI-summary of 20-message thread: **< 5 sec** end-to-end

### 6.2 Memory
- Idle: **< 250 MB RAM**
- 10,000 messages cached: **< 600 MB RAM**

### 6.3 Reliability
- SMTP retry with exponential backoff (1s, 2s, 4s, 8s, 30s, 60s)
- Local DB with WAL mode and regular checkpoint
- Never lose a draft (auto-save)
- Graceful degradation: if AI provider is down, app works without AI

### 6.4 Localization
- All strings through resources (`Resources.Strings`)
- RTL support (for future)
- Date/number formats via `CultureInfo`

### 6.5 Accessibility
- Keyboard navigation for all main actions
- Screen reader labels for critical controls
- Minimum WCAG AA text contrast

### 6.6 Security
- TLS 1.2+ for all connections
- API keys never written to logs
- HTML in emails rendered through sanitizer (AngleSharp + AllowList)
- Email content from DB never executed as script
- **No secrets in repository** — see Security section in README

---

## 7. Data Storage

### 7.1 SQLite Schema (simplified)

```sql
-- Accounts
CREATE TABLE accounts (
    id TEXT PRIMARY KEY,         -- GUID
    email TEXT NOT NULL UNIQUE,
    display_name TEXT,
    imap_host TEXT, imap_port INTEGER, imap_ssl INTEGER,
    smtp_host TEXT, smtp_port INTEGER, smtp_ssl INTEGER,
    oauth2_token_ref TEXT,       -- ref to keychain
    sync_state TEXT,              -- JSON: last_uids per folder
    default_signature_id TEXT,    -- FK to signatures
    created_at INTEGER, updated_at INTEGER
);

-- Folders
CREATE TABLE folders (
    id TEXT PRIMARY KEY,
    account_id TEXT NOT NULL REFERENCES accounts(id),
    remote_name TEXT NOT NULL,
    type TEXT,                    -- inbox/sent/drafts/trash/custom
    parent_id TEXT,
    last_sync_uid INTEGER,
    unread_count INTEGER
);

-- Messages (metadata)
CREATE TABLE messages (
    id TEXT PRIMARY KEY,
    account_id TEXT NOT NULL,
    folder_id TEXT NOT NULL,
    message_uid INTEGER NOT NULL,
    message_id TEXT,
    in_reply_to TEXT,
    references TEXT,              -- JSON array
    from_address TEXT, from_name TEXT,
    to_addresses TEXT,            -- JSON
    cc_addresses TEXT,
    subject TEXT,
    date_utc INTEGER,
    size INTEGER,
    has_attachments INTEGER,
    is_read INTEGER, is_starred INTEGER, is_flagged INTEGER,
    thread_id TEXT,
    ai_category TEXT,
    ai_summary TEXT,
    pgp_status TEXT,              -- none/signed/encrypted/signed+encrypted
    pgp_verified INTEGER,         -- 0/1/-1 (unknown/valid/invalid)
    fetched_at INTEGER,
    UNIQUE(account_id, folder_id, message_uid)
);
CREATE INDEX idx_messages_thread ON messages(thread_id);
CREATE INDEX idx_messages_date ON messages(date_utc DESC);
CREATE INDEX idx_messages_unread ON messages(is_read) WHERE is_read = 0;

-- Message bodies
CREATE TABLE message_bodies (
    message_id TEXT PRIMARY KEY REFERENCES messages(id),
    text_plain TEXT,
    text_html TEXT,               -- sanitized
    raw_mime_path TEXT            -- path to raw binary file on disk
);

-- Attachments
CREATE TABLE attachments (
    id TEXT PRIMARY KEY,
    message_id TEXT NOT NULL REFERENCES messages(id),
    filename TEXT,
    content_type TEXT,
    size INTEGER,
    content_id TEXT,              -- for inline
    local_path TEXT               -- where downloaded
);

-- Signatures
CREATE TABLE signatures (
    id TEXT PRIMARY KEY,
    account_id TEXT NOT NULL REFERENCES accounts(id),
    name TEXT NOT NULL,           -- e.g., "Work", "Personal"
    content_html TEXT,
    content_plain TEXT,
    is_default INTEGER DEFAULT 0,
    sort_order INTEGER DEFAULT 0,
    created_at INTEGER, updated_at INTEGER
);

-- Calendar events
CREATE TABLE calendar_events (
    id TEXT PRIMARY KEY,
    account_id TEXT,              -- nullable (local-only events)
    title TEXT NOT NULL,
    description TEXT,
    location TEXT,
    start_utc INTEGER NOT NULL,
    end_utc INTEGER NOT NULL,
    all_day INTEGER DEFAULT 0,
    color TEXT,                   -- hex color for category
    reminder_minutes INTEGER,     -- minutes before event
    recurrence_rule TEXT,         -- iCal RRULE format
    caldav_uid TEXT,              -- for future CalDAV sync
    created_at INTEGER, updated_at INTEGER
);
CREATE INDEX idx_calendar_start ON calendar_events(start_utc);

-- PGP keys
CREATE TABLE pgp_keys (
    id TEXT PRIMARY KEY,
    account_id TEXT NOT NULL REFERENCES accounts(id),
    key_id TEXT NOT NULL,         -- PGP key ID (last 16 hex chars)
    fingerprint TEXT NOT NULL,
    public_key_armored TEXT NOT NULL,
    private_key_armored TEXT,     -- null for imported public-only keys
    user_id TEXT,                 -- "Name <email>"
    is_default INTEGER DEFAULT 0,
    created_at INTEGER
);

-- Pending operations (offline queue)
CREATE TABLE pending_ops (
    id TEXT PRIMARY KEY,
    account_id TEXT NOT NULL,
    op_type TEXT,                 -- send/delete/move/flag
    payload TEXT,                 -- JSON
    status TEXT,                  -- pending/sent/failed
    retry_count INTEGER DEFAULT 0,
    created_at INTEGER, updated_at INTEGER
);

-- AI settings
CREATE TABLE ai_settings (
    id TEXT PRIMARY KEY,
    provider TEXT NOT NULL,       -- openai/anthropic/openrouter/custom
    model TEXT NOT NULL,
    api_key_ref TEXT,             -- ref to keychain
    base_url TEXT,                -- for custom endpoints
    is_default INTEGER DEFAULT 0,
    created_at INTEGER
);
```

---

## 8. Security & Secrets Policy

### 8.1 Rules for Open Source

Since this project is open source on GitHub, the following rules are **absolute**:

1. **Never commit secrets.** API keys, passwords, tokens, database files — never in git.
2. **Use `appsettings.example.json`** with placeholder values. Real config is in `appsettings.json` which is gitignored.
3. **`.gitignore` must include:**
   ```
   appsettings.json
   *.db
   *.db-shm
   *.db-wal
   *.key
   *.pem
   .env
   ```
4. **CI secrets** go through GitHub Actions secrets, never in workflow files.
5. **AI API keys** are stored only in OS keychain, never on disk in plaintext.
6. **PGP private keys** are stored in SQLite (encrypted DB) or optionally in OS keychain.
7. **Logs must never contain** API keys, email content, passwords, or tokens.

### 8.2 Runtime Security

- TLS 1.2+ for all IMAP/SMTP/HTTP connections
- HTML email rendered through AngleSharp allowlist sanitizer
- No JavaScript execution in email rendering
- WebView in composer is sandboxed (no network access to arbitrary URLs)
- PGP operations happen in-process (keys never sent to external services)

---

## 9. AI Integration Details

### 9.1 Provider Abstraction

```csharp
public interface IAiGateway
{
    Task<AiResponse> ChatAsync(IEnumerable<AiMessage> messages, 
        AiRequestOptions options, CancellationToken ct);
    Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct);
    Task<AiUsage> EstimateCostAsync(IEnumerable<AiMessage> messages, 
        string model, CancellationToken ct);
}
```

Implementations:
- `OpenAiCompatibleGateway` — works with OpenAI, OpenRouter, any OpenAI-compatible API
- `AnthropicGateway` — for Claude models (different API format)

### 9.2 Prompt Templates

Stored in `PromptTemplates.cs`, customizable by user in settings:

| Feature | Template |
|---|---|
| Summarize | "Summarize this email thread in 2-3 bullet points..." |
| Draft Reply | "Draft a reply to this email in a [formal/casual] tone..." |
| Classify | "Classify this email into one of: Work, Personal, Newsletter, Spam-like, Action-required..." |
| Extract TODOs | "Extract action items from this thread..." |

### 9.3 Cost Tracking

Every AI request logs:
- Model used
- Input tokens
- Output tokens
- Estimated cost (based on provider pricing)
- Timestamp

Displayed in UI as a small badge per request, with a total in settings.

---

## 10. PGP Implementation Details

### 10.1 Library

**BouncyCastle** (Portable.BouncyCastle) — the standard for .NET PGP.

### 10.2 Key Management

- Generate RSA 4096-bit or Ed25519 keys
- Import from file (`.asc`, `.gpg`)
- Import from keyserver (optional)
- Export public key (armored)
- Export private key (armored, passphrase-protected)
- Trust levels: Unknown, Marginal, Full, Ultimate

### 10.3 Email Integration

**Outgoing:**
1. User clicks "Encrypt" in composer
2. App looks up recipient's public key (from local keyring or keyserver)
3. Message encrypted with recipient's public key, signed with sender's private key
4. Encrypted content sent as PGP/MIME attachment

**Incoming:**
1. App detects PGP/MIME structure
2. Looks up sender's public key for signature verification
3. Decrypts with user's private key (passphrase prompt if locked)
4. Displays decrypted content with PGP status indicators

### 10.4 UI Indicators

| Status | Icon | Color |
|---|---|---|
| Encrypted | 🔒 Lock | Green |
| Signed (valid) | ✓ Checkmark | Green |
| Signed (invalid) | ✗ Cross | Red |
| Encrypted + Signed | 🔒✓ | Green |
| Unknown key | ❓ Question | Yellow |
| No PGP | (nothing) | — |

---

## 11. Calendar Details

### 11.1 Event Model

```csharp
public class CalendarEvent
{
    public Guid Id { get; set; }
    public Guid? AccountId { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool AllDay { get; set; }
    public string? Color { get; set; }
    public int? ReminderMinutes { get; set; }
    public string? RecurrenceRule { get; set; }
    public string? CaldavUid { get; set; }
}
```

### 11.2 Views

- **Month view** — grid of days, events shown as colored bars
- **Week view** — hourly timeline, events as blocks
- **Day view** — detailed hourly view for a single day
- **Mini calendar** — in sidebar for quick date navigation

### 11.3 Integration with Email

- "Create event from email" — extracts dates/times from message body using regex + AI
- Meeting invitations (ICS attachments) — parse and add to calendar
- Reply to meeting invitation (accept/tentative/decline)

### 11.4 CalDAV (v0.2)

- Sync with Google Calendar, Nextcloud, Radicale
- Two-way sync with conflict resolution
- Multiple CalDAV accounts

---

## 12. Open Questions / TBD

| # | Question | Options | Status |
|---|---|---|---|
| 1 | WebView on Linux: CEF or WebKitGTK? | CEF is heavier but consistent; WebKitGTK is lighter but rendering differences | TBD |
| 2 | SQLCipher vs native SQLite encryption? | SQLCipher requires native bindings; native encryption is .NET-only | TBD |
| 3 | Should calendar be a separate window or tab in main? | Separate window = more flexible; tab = simpler | TBD |
| 4 | Default theme: light or dark? | Follow system default | TBD |
| 5 | Rate limiting for AI requests? | Per-minute and per-day caps to prevent cost surprises | TBD |
