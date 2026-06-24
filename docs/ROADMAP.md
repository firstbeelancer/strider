# Strider Mail — Roadmap

> Hobby project. No dates, just direction. Things move when they move.

---

## Phase 1 — Foundation 🏗️

The skeleton. Everything else depends on this.

- [ ] Solution structure setup (4 projects + tests)
- [ ] Domain models (Account, Folder, Message, Attachment, Thread)
- [ ] SQLite schema + migrations
- [ ] Account wizard with auto-discover (IMAP/SMTP)
- [ ] Keychain integration (DPAPI on Windows, libsecret on Linux)
- [ ] MailKit IMAP gateway (connect, fetch, sync)
- [ ] MailKit SMTP gateway (send)
- [ ] Folder tree with unread badges
- [ ] Message list (virtualized, sorted, thread grouping)
- [ ] Message reader (HTML/plain rendering, sanitized)
- [ ] Basic compose (To/Cc/Bcc/Subject/Body, send)
- [ ] Attachment handling (download, preview images/PDF)
- [ ] Dark/light theme
- [ ] CI pipeline (GitHub Actions: build + test on Win/Linux)

## Phase 2 — Rich Composer ✍️

The editor that doesn't suck.

- [ ] Embedded WebView setup (WebView2 on Windows, CEF on Linux)
- [ ] TipTap editor integration
- [ ] JS↔C# bridge for editor communication
- [ ] Native toolbar (Avalonia) above editor
- [ ] Font family selection
- [ ] Font size presets
- [ ] Font color and highlight color
- [ ] Text formatting (B/I/U/Strike/Sub/Super)
- [ ] Paragraph alignment and line spacing
- [ ] Lists (ordered, unordered, nested)
- [ ] Tables (insert, resize, paste from Excel)
- [ ] Code blocks with syntax highlighting
- [ ] Emoji picker
- [ ] Link insert/edit
- [ ] WYSIWYG ↔ HTML source toggle
- [ ] Paste from Word/Google Docs (cleanup)
- [ ] Undo/Redo
- [ ] Inline images (paste, drag-drop)
- [ ] Multiple signatures CRUD
- [ ] Signature selector in compose
- [ ] Compose in separate window (multiple simultaneous)
- [ ] mailto: handling

## Phase 3 — Sync & Offline 🔄

Reliable mail, even without internet.

- [ ] IMAP IDLE for real-time new mail
- [ ] Background sync with polling fallback
- [ ] Offline mode: read from cache
- [ ] Operation queue (send/delete/move/flag when offline)
- [ ] Conflict resolution by UIDs
- [ ] Draft auto-save (every 30s)
- [ ] Undo Send (5s window)
- [ ] Full re-sync on demand
- [ ] Cache cleanup policy (7/30/90 days/never)
- [ ] Export/import settings (JSON)

## Phase 4 — AI Integration 🤖

Smart email, not dumb email.

- [ ] AI provider abstraction (IAiGateway)
- [ ] OpenAI-compatible gateway
- [ ] Anthropic gateway
- [ ] API key storage in keychain
- [ ] Model selection UI
- [ ] Thread summarization
- [ ] AI draft replies (with style learning from past emails)
- [ ] Email classification (Work/Personal/Newsletter/Spam/Action)
- [ ] Smart folders (auto-create based on classification)
- [ ] Semantic search (embeddings-based)
- [ ] Cost tracking (tokens × price per request)
- [ ] Customizable prompt templates
- [ ] Graceful degradation when AI is unavailable

## Phase 5 — Calendar 📅

Because email and calendar belong together.

- [ ] Calendar domain model (Event, Reminder, Recurrence)
- [ ] SQLite calendar store
- [ ] Month view
- [ ] Week view
- [ ] Day view
- [ ] Mini calendar in sidebar
- [ ] Create/edit/delete events
- [ ] Drag-to-reschedule
- [ ] Color-coded categories
- [ ] Reminder notifications
- [ ] Create event from email (date/time extraction)
- [ ] ICS attachment parsing (meeting invitations)
- [ ] CalDAV sync (v0.2)

## Phase 6 — PGP Security 🔐

Encrypted email that actually works.

- [ ] BouncyCastle integration
- [ ] PGP key generation (RSA 4096 / Ed25519)
- [ ] Key import/export (armored `.asc`)
- [ ] Key management UI (list, delete, trust levels)
- [ ] Encrypt outgoing messages
- [ ] Decrypt incoming messages
- [ ] Sign outgoing messages
- [ ] Verify signatures on incoming
- [ ] PGP status indicators in message view
- [ ] Passphrase prompt for private key operations
- [ ] Keyserver lookup (optional)
- [ ] S/MIME (v0.2)

## Phase 7 — Polish ✨

Make it feel finished.

- [ ] System tray integration (minimize to tray, quick actions)
- [ ] Full keyboard shortcuts
- [ ] Toast notifications with action buttons
- [ ] Search syntax highlighting (`from:alice subject:invoice`)
- [ ] Localization framework (en, ru)
- [ ] Performance profiling and optimization
- [ ] Accessibility audit (keyboard nav, screen reader labels, contrast)
- [ ] Release packaging: MSI (Windows), AppImage + DEB (Linux)
- [ ] Auto-update mechanism (check GitHub releases)
- [ ] CHANGELOG.md maintenance
- [ ] Screenshot/GIF generation for README

---

## Later (v0.2+)

- S/MIME support
- CalDAV sync (Google Calendar, Nextcloud)
- CardDAV contacts sync
- Plugin API (custom rules, AI prompts)
- Scheduled send
- Snooze
- Reply templates
- AI auto-replies by rules
- Flatpak packaging
- Exchange Web Services (EWS) support

## Maybe Someday (v1.0+)

- Mobile version (Avalonia Mobile)
- E2E sync between devices
- IDE plugins
- Custom theme engine
