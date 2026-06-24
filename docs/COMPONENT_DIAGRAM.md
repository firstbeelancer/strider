# Strider Mail — Component Architecture Diagram

> Version: 1.0 | Date: 2026-06-24
> Shows all components, their responsibilities, and interactions.

---

## 1. High-Level Architecture

```
+=========================================================================+
|                          Strider.Host (Entry Point)                      |
|  Program.cs, DI Container, Configuration                                |
+===========================+=============================================+
                            |
            +---------------+---------------+
            |                               |
+-----------v-----------+    +--------------v--------------+
|     Strider.UI        |    |   Strider.Infrastructure   |
|  (Presentation Layer) |    |   (Implementation Layer)   |
+-----------+-----------+    +--------------+--------------+
            |                               |
            +---------------+---------------+
                            |
                +-----------v-----------+
                |    Strider.Core       |
                |  (Domain + Contracts) |
                +-----------------------+
```

---

## 2. Strider.Core — Domain Layer (No Dependencies)

```
+-------------------------------------------------------------------+
|                         Strider.Core                               |
|                                                                    |
|  +-------------------+    +-------------------+                   |
|  |     Domain/       |    |   Abstractions/   |                   |
|  |                   |    |                   |                   |
|  |  Account          |    |  IImapGateway     |                   |
|  |  Folder           |    |  ISmtpGateway     |                   |
|  |  Message          |    |  IMessageStore    |                   |
|  |  MessageBody      |    |  IAccountStore    |                   |
|  |  Attachment       |    |  IAiGateway       |                   |
|  |  EmailThread      |    |  IKeychainService |                   |
|  |  Signature        |    |  IPgpService      |                   |
|  |  CalendarEvent    |    |  ICalendarStore   |                   |
|  |  PgpKey           |    |  ISignatureStore  |                   |
|  |  PendingOp        |    |  IEventBus        |                   |
|  |  AiSettings       |    |                   |                   |
|  +-------------------+    +-------------------+                   |
+-------------------------------------------------------------------+
```

---

## 3. Strider.Infrastructure — Implementation Layer

```
+-----------------------------------------------------------------------+
|                    Strider.Infrastructure                              |
|                                                                        |
|  +---------------------------+    +---------------------------+       |
|  |     Mail/                 |    |    Persistence/           |       |
|  |                           |    |                           |       |
|  |  MailKitImapGateway ──────┼───>│  SqliteAccountStore       |       |
|  |  MailKitSmtpGateway ──────┼───>│  SqliteMessageStore       |       |
|  +---------------------------+    |  DatabaseInitializer      |       |
|                                   |  Schema.sql               |       |
|  +---------------------------+    +---------------------------+       |
|  |     Ai/                   |                                       |
|  |                           |    +---------------------------+       |
|  |  OpenAiCompatibleGateway  |    |    Security/              |       |
|  |  AnthropicGateway         |    |                           |       |
|  |  PromptTemplates          |    |  DpapiKeychainService     |       |
|  +---------------------------+    |  LibsecretKeychainService |       |
|                                   |  BouncyCastlePgpService   |       |
|  +---------------------------+    +---------------------------+       |
|  |     Services/             |                                       |
|  |                           |    +---------------------------+       |
|  |  InMemoryEventBus         |    |    Editor/                |       |
|  +---------------------------+    |                           |       |
|                                   |  WebViewEditorHost        |       |
|                                   |  EditorBridge             |       |
|                                   +---------------------------+       |
+-----------------------------------------------------------------------+
```

---

## 4. Strider.UI — Presentation Layer

```
+-----------------------------------------------------------------------+
|                         Strider.UI                                     |
|                                                                        |
|  +---------------------------+    +---------------------------+       |
|  |     Views/                |    |    ViewModels/            |       |
|  |                           |    |                           |       |
|  |  MainWindow ──────────────┼───>│  MainWindowViewModel      |       |
|  |  ComposeWindow ───────────┼───>│  ComposeViewModel         |       |
|  |  SettingsWindow ──────────┼───>│  SettingsViewModel        |       |
|  |  AccountWizardWindow ─────┼───>│  AccountWizardViewModel   |       |
|  |                           |    |                           |       |
|  |  Controls/                |    |  MessageListViewModel     |       |
|  |    FolderTreeView ────────┼───>│  MessageReaderViewModel   |       |
|  |    MessageListView ───────┼───>│  CalendarViewModel        |       |
|  |    MessageReaderView ─────┼───>│  PgpKeyManagerViewModel   |       |
|  |    AiPanelView            |    +---------------------------+       |
|  |    ComposerView           |                                       |
|  |    RichEditorView         |    +---------------------------+       |
|  |    CalendarView           |    |    Resources/             |       |
|  |    SignatureEditorView    |    |                           |       |
|  |    PgpKeyManagerView      |    |  Colors.axaml             |       |
|  |    AttachmentChip         |    |  Typography.axaml         |       |
|  |                           |    |  Spacing.axaml            |       |
|  |  Dialogs/                 |    |  Components.axaml         |       |
|  |    AccountWizardDialog    |    +---------------------------+       |
|  |    FilterDialog           |                                       |
|  |    AiSettingsDialog       |    +---------------------------+       |
|  |    PgpKeyDialog           |    |    Assets/                |       |
|  |    SignatureDialog        |    |                           |       |
|  |    CalendarEventDialog    |    |  Icons/ (Lucide SVG)      |       |
|  +---------------------------+    |  Fonts/ (Inter, JetBrains)|       |
|                                   |  Themes/ (Light/Dark)     |       |
|                                   +---------------------------+       |
+-----------------------------------------------------------------------+
```

---

## 5. Component Interaction: Read Email

```
User clicks folder
        |
        v
+-------------------+     +-------------------+     +-------------------+
|  FolderTreeView   |---->| MainWindowVM      |---->| MessageListView   |
|  (UI Component)   |     | (ViewModel)       |     | (UI Component)    |
+-------------------+     +-------------------+     +-------------------+
                                    |                        |
                                    v                        v
                          +-------------------+     +-------------------+
                          | IMessageStore     |     | IImapGateway      |
                          | (Interface)       |     | (Interface)       |
                          +-------------------+     +-------------------+
                                    |                        |
                                    v                        v
                          +-------------------+     +-------------------+
                          | SqliteMessageStore|     | MailKitImapGateway|
                          | (SQLite + Dapper) |     | (MailKit IMAP)    |
                          +-------------------+     +-------------------+
                                    |                        |
                                    v                        v
                          +-------------------+     +-------------------+
                          | SQLite Database   |     | IMAP Server       |
                          | (strider.db)      |     | (Gmail, etc.)     |
                          +-------------------+     +-------------------+
```

---

## 6. Component Interaction: Send Email

```
User clicks Send
        |
        v
+-------------------+     +-------------------+     +-------------------+
|  ComposerView     |---->| ComposeViewModel  |---->| ISmtpGateway      |
|  (UI Component)   |     | (ViewModel)       |     | (Interface)       |
+-------------------+     +-------------------+     +-------------------+
                                    |                        |
                                    v                        v
                          +-------------------+     +-------------------+
                          | IMessageStore     |     | MailKitSmtpGateway|
                          | (Save draft)      |     | (MailKit SMTP)    |
                          +-------------------+     +-------------------+
                                    |                        |
                                    v                        v
                          +-------------------+     +-------------------+
                          | SQLite Database   |     | SMTP Server       |
                          +-------------------+     +-------------------+
```

---

## 7. Component Interaction: AI Features

```
User clicks "Summarize"
        |
        v
+-------------------+     +-------------------+     +-------------------+
|  AiPanelView      |---->| MessageReaderVM   |---->| IAiGateway        |
|  (UI Component)   |     | (ViewModel)       |     | (Interface)       |
+-------------------+     +-------------------+     +-------------------+
                                    |                        |
                                    v                        v
                          +-------------------+     +-------------------+
                          | IMessageStore     |     | OpenAiGateway     |
                          | (Load thread)     |     | AnthropicGateway  |
                          +-------------------+     +-------------------+
                                    |                        |
                                    v                        v
                          +-------------------+     +-------------------+
                          | SQLite Database   |     | AI API (external) |
                          +-------------------+     +-------------------+
```

---

## 8. Component Interaction: PGP Encryption

```
User clicks "Encrypt"
        |
        v
+-------------------+     +-------------------+     +-------------------+
|  ComposerView     |---->| ComposeViewModel  |---->| IPgpService       |
|  (Encrypt button) |     | (ViewModel)       |     | (Interface)       |
+-------------------+     +-------------------+     +-------------------+
                                    |                        |
                                    v                        v
                          +-------------------+     +-------------------+
                          | IMessageStore     |     | BouncyCastlePgp   |
                          | (Load keys)       |     | Service            |
                          +-------------------+     +-------------------+
                                    |                        |
                                    v                        v
                          +-------------------+     +-------------------+
                          | SQLite Database   |     | BouncyCastle      |
                          | (pgp_keys table)  |     | (Crypto Engine)   |
                          +-------------------+     +-------------------+
```

---

## 9. Data Flow: Email Lifecycle

```
+-----------+     +-----------+     +-----------+     +-----------+
|  IMAP     |     |  SQLite   |     |  AI API   |     |  UI       |
|  Server   |     |  Cache    |     |  (opt.)   |     |  Display  |
+-----------+     +-----------+     +-----------+     +-----------+
     |                 |                 |                 |
     |  Fetch headers  |                 |                 |
     |---------------->|                 |                 |
     |                 |  Store message  |                 |
     |                 |---------------->|                 |
     |                 |                 |                 |
     |  Fetch body     |                 |                 |
     |---------------->|                 |                 |
     |                 |  Store body     |                 |
     |                 |---------------->|                 |
     |                 |                 |                 |
     |                 |  Classify       |                 |
     |                 |---------------->|                 |
     |                 |                 |  Return category|
     |                 |<----------------|                 |
     |                 |                 |                 |
     |                 |  Update UI      |                 |
     |                 |--------------------------------->|
     |                 |                 |                 |
```

---

## 10. Dependency Injection Map

```
Host.CreateDefaultBuilder()
        |
        v
+-------------------------------------------------------------------+
|  IServiceCollection                                               |
|                                                                    |
|  Transient:                                                       |
|    IImapGateway       -> MailKitImapGateway                       |
|    ISmtpGateway       -> MailKitSmtpGateway                       |
|                                                                    |
|  Singleton:                                                       |
|    IMessageStore      -> SqliteMessageStore                       |
|    IAccountStore      -> SqliteAccountStore                       |
|    IAiGateway         -> OpenAiCompatibleGateway                  |
|    IKeychainService   -> DpapiKeychainService (Win)               |
|                      |-> LibsecretKeychainService (Linux)         |
|    IPgpService        -> BouncyCastlePgpService                   |
|    IEventBus          -> InMemoryEventBus                         |
|    ICalendarStore     -> SqliteCalendarStore                      |
|    ISignatureStore    -> SqliteSignatureStore                     |
|                                                                    |
|  Scoped:                                                          |
|    MainWindowViewModel                                            |
|    ComposeViewModel                                               |
|    MessageListViewModel                                           |
|    MessageReaderViewModel                                         |
+-------------------------------------------------------------------+
```

---

## 11. File Structure (Current Implementation Status)

```
strider/
├── Strider.sln
├── src/
│   ├── Strider.Core/              [DONE] Domain + Interfaces
│   │   ├── Domain/                [DONE] 11 models
│   │   └── Abstractions/         [DONE] 10 interfaces
│   │
│   ├── Strider.Infrastructure/    [PARTIAL]
│   │   ├── Mail/
│   │   │   ├── MailKitImapGateway.cs    [DONE]
│   │   │   └── MailKitSmtpGateway.cs    [DONE]
│   │   ├── Persistence/
│   │   │   ├── Schema.sql               [DONE]
│   │   │   ├── DatabaseInitializer.cs   [DONE]
│   │   │   ├── SqliteAccountStore.cs    [DONE]
│   │   │   └── SqliteMessageStore.cs    [DONE]
│   │   ├── Ai/                          [TODO]
│   │   ├── Security/                    [TODO]
│   │   └── Editor/                      [TODO]
│   │
│   ├── Strider.UI/                [PARTIAL]
│   │   ├── App.axaml              [DONE]
│   │   └── Views/
│   │       └── MainWindow.cs      [DONE - empty]
│   │
│   └── Strider.Host/              [PARTIAL]
│       ├── Program.cs             [DONE]
│       └── appsettings.example.json [DONE]
│
├── tests/                         [TODO]
├── docs/
│   ├── SPEC.md                    [DONE]
│   ├── DESIGN_SYSTEM_EXTRAS.md    [DONE]
│   ├── ROADMAP.md                 [DONE]
│   └── ARCHITECTURE.md            [DONE]
└── .github/workflows/             [TODO]
```
