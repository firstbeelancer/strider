# Strider Mail — Design System: Additional Sections

> Supplements the main DESIGN_SYSTEM.md with components for: AI Panel, Calendar, Rich Editor, PGP, Signatures.
> Version: **v0.1.0 draft**

---

## 7. AI Panel

### 7.1 Layout

AI panel appears on the right side of the message reader (when enabled) or as a floating panel.

```
┌──────────────────────────────────────────┐
│  ✨ AI Assistant              [pin] [✕]  │
├──────────────────────────────────────────┤
│                                          │
│  [Summarize Thread]  [Draft Reply]       │
│  [Extract TODOs]     [Classify]          │
│                                          │
│  ┌────────────────────────────────────┐  │
│  │ Summary                            │  │
│  │                                    │  │
│  │ • Client approved the Q4 budget    │  │
│  │ • Legal needs to review by Friday  │  │
│  │ • Action: send updated contract    │  │
│  │                                    │  │
│  │ [Copy] [Insert into reply] [Edit]  │  │
│  └────────────────────────────────────┘  │
│                                          │
│  💬 Ask anything about this thread...    │
│  ┌──────────────────────────────────┐    │
│  │                            [Send]│    │
│  └──────────────────────────────────┘    │
│                                          │
│  ⚡ 847 tokens · ~$0.002                │
└──────────────────────────────────────────┘
```

### 7.2 AI Action Buttons

| Component | Style |
|---|---|
| Action button | `AIGradientButton` (gradient background: brand/primary → brand/gradient-to) |
| Icon | `sparkles` (Lucide), 16×16 |
| Hover | Gradient darkens 10% |
| Loading state | Spinner replaces icon, text becomes "Thinking..." |
| Disabled | 50% opacity, no hover |

### 7.3 AI Response Card

```
┌────────────────────────────────────┐
│  Summary                   ✨ AI   │
│                                    │
│  • Point one here                 │
│  • Point two here                 │
│  • Point three here               │
│                                    │
│  [Copy] [Insert] [Edit]           │
└────────────────────────────────────┘
```

- **Background:** `bg/surface`
- **Border:** 1px `border/subtle`
- **Border-left:** 3px `brand/gradient-to` (AI accent)
- **Radius:** `radius/lg`
- **Padding:** 16
- **Actions:** GhostButtons below content

### 7.4 AI Cost Badge

```
⚡ 847 tokens · ~$0.002
```

- **Font:** `text/caption`, `text/tertiary`
- **Icon:** `zap` (Lucide), 12×12
- **Position:** bottom-right of AI panel

### 7.5 AI Classification Badges

| Category | Color | Icon |
|---|---|---|
| Work | `category/work` (#5B6CFF) | `briefcase` |
| Personal | `category/personal` (#10B981) | `user` |
| Newsletter | `category/newsletter` (#8B5CF6) | `mail` |
| Transactional | `category/transactional` (#06B6D4) | `receipt` |
| Action Required | `category/action` (#F59E0B) | `alert-circle` |
| Spam-like | `category/spam-like` (#6B7280) | `shield-alert` |

**Badge style:**
- Background: category color at 15% opacity
- Text: category color
- Radius: `radius/full`
- Padding: 2px 8px
- Font: `text/caption`, weight 500

---

## 8. Rich Text Editor

### 8.1 Editor Toolbar

Toolbar is native Avalonia, positioned above the WebView editor.

```
┌──────────────────────────────────────────────────────────────────┐
│ [▾ Inter] [▾ 14] [▾ Normal] │ B I U S │ [A▾] [🖍▾] │            │
│ [≡] [≡] [≡] [≡] │ [•] [1.] [❝] [─] │ [<>] [🔗] [😀] [▦] [</>]│
└──────────────────────────────────────────────────────────────────┘
```

**Toolbar rows:**

**Row 1:**
| Control | Content | Width |
|---|---|---|
| Font family dropdown | Combobox with system + web fonts | 140px |
| Font size dropdown | 8–72px with presets | 64px |
| Paragraph style dropdown | Normal / H1 / H2 / H3 / Blockquote | 100px |
| Separator | 1px `border/subtle` | — |
| Bold / Italic / Underline / Strikethrough | Toggle icon buttons | 36px each |
| Separator | — | — |
| Text color picker | IconButton with color indicator below | 36px |
| Highlight color picker | IconButton with color indicator below | 36px |

**Row 2:**
| Control | Content | Width |
|---|---|---|
| Alignment buttons | Left / Center / Right / Justify (segmented) | 36px each |
| Separator | — | — |
| Bullet list / Numbered list | Toggle icon buttons | 36px each |
| Blockquote | Toggle icon button | 36px |
| Horizontal rule | Icon button | 36px |
| Separator | — | — |
| Code (inline) | Toggle icon button | 36px |
| Link | Icon button | 36px |
| Emoji picker | Icon button | 36px |
| Table insert | Icon button with dropdown | 36px |
| HTML source toggle | Icon button | 36px |

**Toolbar states:**
- **Default:** `bg/surface-sunken`, buttons `bg/canvas` with `border/subtle`
- **Active toggle:** `bg/surface-selected`, text `brand/primary`
- **Hover:** `bg/surface-hover`
- **Disabled:** `text/disabled`, no hover

**Toolbar height:** 36px per row, 2 rows = 72px total
**Toolbar padding:** 8px horizontal, 4px vertical
**Button gap:** 2px

### 8.2 Color Picker

```
┌─────────────────────────────────┐
│  Theme Colors                   │
│  ■ ■ ■ ■ ■ ■ ■ ■ ■ ■          │
│  ■ ■ ■ ■ ■ ■ ■ ■ ■ ■          │
│                                 │
│  Standard Colors                │
│  ■ ■ ■ ■ ■ ■ ■ ■ ■ ■          │
│                                 │
│  Recent                         │
│  ■ ■ ■ ■ ■                     │
│                                 │
│  ┌───────────────────────┐      │
│  │ #5B6CFF           [OK]│      │
│  └───────────────────────┘      │
│                                 │
│  [Custom color...]              │
└─────────────────────────────────┘
```

- **Popup width:** 240px
- **Color swatch:** 24×24, radius `radius/sm`
- **Swatch gap:** 4px
- **Selected swatch:** 2px `border/focus` outline
- **Hex input:** TextInput, 80px wide

### 8.3 Emoji Picker

```
┌────────────────────────────────────┐
│  🔍 Search emoji...               │
├────────────────────────────────────┤
│  😀 😁 😂 🤣 😃 😄 😅 😆 😉    │
│  😊 😋 😎 😍 🥰 😘 😗 😙 😚    │
│  ...                               │
├────────────────────────────────────┤
│  😀  🐻  🍕  🚀  ❤️  👋  ⚽  🎵│  ← category tabs
└────────────────────────────────────┘
```

- **Popup width:** 320px
- **Emoji size:** 32×32
- **Emoji gap:** 4px
- **Hover:** `bg/surface-hover`, radius `radius/sm`
- **Category tabs:** bottom bar, icon-only, 32px each
- **Search:** TextInput at top, 28px height

### 8.4 Table Controls

When cursor is inside a table, a floating toolbar appears:

```
┌──────────────────────────────────────────┐
│ [+] [−] [↕] [↔] │ [Merge] [Split] [🗑] │
└──────────────────────────────────────────┘
```

- **Position:** above the table, centered
- **Background:** `bg/surface-raised`, shadow `shadow/md`
- **Radius:** `radius/md`
- **Buttons:** IconButton, 28×28

---

## 9. Calendar

### 9.1 Calendar Layout

```
┌──────────────────────────────────────────────────────┐
│  ◀  June 2026  ▼  ▶          [Month] [Week] [Day]   │
├──────────────────────────────────────────────────────┤
│  Mon  Tue  Wed  Thu  Fri  Sat  Sun                   │
│   1    2    3    4    5    6    7                     │
│   8    9   10   11   12   13   14                     │
│  15   16   17   18   19   20   21                     │
│  22   23   24   25   26   27   28                     │
│  29   30                                               │
└──────────────────────────────────────────────────────┘
```

### 9.2 Month View

- **Grid:** 7 columns (Mon–Sun), 5–6 rows
- **Day cell:** min-height 80px, padding 4px
- **Day number:** `text/body`, top-left of cell
- **Today:** circle background `brand/primary` at 15% opacity, text `brand/primary`
- **Other month days:** `text/disabled`
- **Events:** colored bars, 2px height, truncated with count badge
- **Selected day:** `bg/surface-selected`

### 9.3 Week View

```
┌──────┬─────────┬─────────┬─────────┬─────────┬─────────┬─────────┬─────────┐
│      │  Mon    │  Tue    │  Wed    │  Thu    │  Fri    │  Sat    │  Sun    │
│      │  23 Jun │  24 Jun │  25 Jun │  26 Jun │  27 Jun │  28 Jun │  29 Jun │
├──────┼─────────┼─────────┼─────────┼─────────┼─────────┼─────────┼─────────┤
│ 08:00│         │         │         │         │         │         │         │
│ 09:00│ ██████  │         │         │         │         │         │         │
│ 10:00│ Meeting │         │         │         │         │         │         │
│ 11:00│         │ ████████│         │         │         │         │         │
│ 12:00│         │ Lunch   │         │         │         │         │         │
│ ...  │         │         │         │         │         │         │         │
└──────┴─────────┴─────────┴─────────┴─────────┴─────────┴─────────┴─────────┘
```

- **Time column:** 48px wide, `text/caption`, `text/tertiary`
- **Hour row:** 48px height
- **Event block:** colored background (event color at 20% opacity), border-left 3px event color
- **Current time indicator:** red line across current hour

### 9.4 Day View

Same as week view but single column, 30-minute granularity.

### 9.5 Event Card

```
┌────────────────────────────────────┐
│  🔵 Team standup                   │
│  09:00 – 09:30 · Zoom             │
│  Daily sync with engineering       │
│                            [Edit]  │
└────────────────────────────────────┘
```

- **Background:** `bg/surface`
- **Border:** 1px `border/subtle`
- **Border-left:** 4px event color
- **Radius:** `radius/md`
- **Padding:** 12px

### 9.6 Event Dialog

```
┌──────────────────────────────────────────┐
│  New Event                          [✕]  │
├──────────────────────────────────────────┤
│                                          │
│  Title                                   │
│  ┌──────────────────────────────────┐    │
│  │ Team standup                     │    │
│  └──────────────────────────────────┘    │
│                                          │
│  Date & Time                             │
│  [📅 24 Jun 2026] [🕐 09:00] – [🕐 09:30]│
│  ☐ All day                               │
│                                          │
│  Location                                │
│  ┌──────────────────────────────────┐    │
│  │ Zoom                             │    │
│  └──────────────────────────────────┘    │
│                                          │
│  Description                             │
│  ┌──────────────────────────────────┐    │
│  │                                  │    │
│  │                                  │    │
│  └──────────────────────────────────┘    │
│                                          │
│  Reminder: [▾ 15 minutes before]         │
│                                          │
│  Color: [🔵] [🟢] [🟡] [🔴] [🟣]       │
│                                          │
│  [Cancel]                  [Save Event]  │
└──────────────────────────────────────────┘
```

### 9.7 Calendar Sidebar

Mini calendar in the left sidebar:

```
┌──────────────────┐
│  ◀ June 2026 ▶   │
│  Mo Tu We Th Fr  │
│  ..  1  2  3  4  │
│   5  6  7  8  9  │
│  10 11 12 13 14  │
│  15 16 17 18 19  │
│  20 21 22 23 24  │
│  25 26 27 28 29  │
│  30              │
└──────────────────┘
```

- **Width:** sidebar width (200px)
- **Today:** circle `brand/primary` at 15%
- **Selected:** `bg/surface-selected`
- **Has events:** dot below number

---

## 10. PGP / Security Indicators

### 10.1 Message PGP Status

In the message header area:

```
┌──────────────────────────────────────────┐
│  🔒 Encrypted   ✓ Signed (valid)         │
│  From: alice@example.com (verified)      │
└──────────────────────────────────────────┘
```

| Status | Icon | Background | Text Color |
|---|---|---|---|
| Encrypted | `lock` (filled) | `status/success-bg` | `status/success` |
| Signed (valid) | `check-circle` (filled) | `status/success-bg` | `status/success` |
| Signed (invalid) | `x-circle` (filled) | `status/error-bg` | `status/error` |
| Signed (unknown key) | `help-circle` (filled) | `status/warning-bg` | `status/warning` |
| Encrypted + Signed | `lock` + `check-circle` | combined | combined |
| Decryption failed | `lock` + `x-circle` | `status/error-bg` | `status/error` |

### 10.2 PGP Key Manager Dialog

```
┌──────────────────────────────────────────────────────┐
│  PGP Key Manager                                [✕]  │
├──────────────────────────────────────────────────────┤
│                                                      │
│  [Generate New Key]  [Import Key]  [Import from URL] │
│                                                      │
│  ┌────────────────────────────────────────────────┐  │
│  │ My Keys                                        │  │
│  │                                                │  │
│  │  🔑 Alice <alice@example.com>     [Default]    │  │
│  │     RSA 4096 · Created: 2026-06-24             │  │
│  │     Fingerprint: AB12 CD34 EF56 ...            │  │
│  │     [Export Public] [Export Private] [Delete]   │  │
│  │                                                │  │
│  │  🔑 Work <alice@company.com>                   │  │
│  │     Ed25519 · Created: 2026-06-20              │  │
│  │     Fingerprint: 7890 AB12 CD34 ...            │  │
│  │     [Export Public] [Export Private] [Delete]   │  │
│  └────────────────────────────────────────────────┘  │
│                                                      │
│  ┌────────────────────────────────────────────────┐  │
│  │ Other's Keys (12)                              │  │
│  │                                                │  │
│  │  🔑 Bob <bob@example.com>         [Full trust] │  │
│  │     RSA 4096 · Imported: 2026-06-23            │  │
│  │     [Set Trust] [Delete]                       │  │
│  └────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────┘
```

### 10.3 PGP Composer Controls

In the compose window toolbar:

```
┌──────────────────────────────────────────────┐
│  To: [alice@example.com]                     │
│  Subject: Re: Confidential                   │
│                                              │
│  [🔒 Encrypt] [✍️ Sign]    ← toggle buttons  │
│                                              │
│  Status: ✓ Recipient key found               │
│  Status: ⚠ No recipient key — send unencrypted?│
└──────────────────────────────────────────────┘
```

- **Toggle buttons:** IconButton with text label, 36px height
- **Encrypt active:** `status/success` background
- **Sign active:** `status/info` background
- **No key warning:** `status/warning` banner below buttons

### 10.4 Passphrase Dialog

```
┌──────────────────────────────────────────┐
│  Unlock PGP Key                     [✕]  │
├──────────────────────────────────────────┤
│                                          │
│  🔑 alice@example.com                    │
│     RSA 4096                             │
│                                          │
│  Passphrase                              │
│  ┌──────────────────────────────────┐    │
│  │ ••••••••••••                  👁  │    │
│  └──────────────────────────────────┘    │
│                                          │
│  ☐ Remember for this session             │
│                                          │
│  [Cancel]                    [Unlock]    │
└──────────────────────────────────────────┘
```

---

## 11. Signature Editor

### 11.1 Signature Management

In Settings → Signatures:

```
┌──────────────────────────────────────────────────────┐
│  Signatures                                     [✕]  │
├──────────────────────────────────────────────────────┤
│                                                      │
│  Account: [▾ alex@example.com]                       │
│                                                      │
│  [+ New Signature]                                   │
│                                                      │
│  ┌────────────────────────────────────────────────┐  │
│  │  📧 Work Signature                    [Default] │  │
│  │  ────────────────────────────────────────────  │  │
│  │  Best regards,                                  │  │
│  │  Alex Smith                                     │  │
│  │  Senior Developer                               │  │
│  │  alex@company.com                               │  │
│  │                                                 │  │
│  │  [Edit] [Duplicate] [Delete]                    │  │
│  └────────────────────────────────────────────────┘  │
│                                                      │
│  ┌────────────────────────────────────────────────┐  │
│  │  📧 Personal                                  │  │
│  │  ────────────────────────────────────────────  │  │
│  │  Cheers! ✌️                                    │  │
│  │  — Alex                                         │  │
│  │                                                 │  │
│  │  [Edit] [Duplicate] [Delete]                    │  │
│  └────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────┘
```

### 11.2 Signature Editor Dialog

```
┌──────────────────────────────────────────┐
│  Edit Signature                     [✕]  │
├──────────────────────────────────────────┤
│                                          │
│  Name                                    │
│  ┌──────────────────────────────────┐    │
│  │ Work Signature                   │    │
│  └──────────────────────────────────┘    │
│                                          │
│  [Rich Text] [HTML Source]               │
│                                          │
│  ┌──────────────────────────────────┐    │
│  │  Best regards,                   │    │
│  │  <b>Alex Smith</b>              │    │
│  │  Senior Developer               │    │
│  │  <a href="...">company.com</a>  │    │
│  │  ─────────────────────          │    │
│  │  📞 +1 (555) 123-4567          │    │
│  └──────────────────────────────────┘    │
│                                          │
│  ☐ Set as default for this account       │
│                                          │
│  [Cancel]                    [Save]      │
└──────────────────────────────────────────┘
```

### 11.3 Signature Selector in Compose

Dropdown in compose window:

```
┌──────────────────────────────────┐
│  ✍️ Signature                    │
│  ┌──────────────────────────┐    │
│  │ 📧 Work Signature    ▾  │    │
│  └──────────────────────────┘    │
│                                  │
│  When opened:                    │
│  ┌──────────────────────────┐    │
│  │ 📧 Work Signature    ✓  │    │
│  │ 📧 Personal              │    │
│  │ 📧 Legal                 │    │
│  │ ─────────────────────── │    │
│  │ ✏️ Manage signatures...  │    │
│  └──────────────────────────┘    │
└──────────────────────────────────┘
```

---

## 12. Compose Window (Separate)

### 12.1 Window Layout

```
┌──────────────────────────────────────────────────────────┐
│  ✉️ New Message                              [_] [□] [✕] │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  From: [▾ alex@example.com]                              │
│  To:   [alice@example.com ×] [+ Cc] [+ Bcc]             │
│  Subj: [Re: Project update                               │
│                                                          │
│  ┌────────────────────────────────────────────────────┐  │
│  │ Toolbar (same as inline composer)                  │  │
│  ├────────────────────────────────────────────────────┤  │
│  │                                                    │  │
│  │  Rich text editor (WebView + TipTap)               │  │
│  │                                                    │  │
│  │  Best regards,                                     │  │
│  │  Alex Smith                                        │  │
│  │                                                    │  │
│  │  ─────────────                                     │  │
│  │  Work Signature                                    │  │
│  │                                                    │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  ┌──────────────────────────────────────────┐            │
│  │ 📎 document.pdf (2.3 MB)           [✕]  │            │
│  │ 📎 screenshot.png (850 KB)         [✕]  │            │
│  └──────────────────────────────────────────┘            │
│                                                          │
│  [📎 Attach] [📅 Schedule] │  [🔒 PGP] [✍️ Sign]        │
│                                                          │
│                              [Draft saved ✓]             │
│                              [Send]  [Save Draft]        │
└──────────────────────────────────────────────────────────┘
```

### 12.2 Compose Window Features

- **Resizable:** min 480×400, max 1100×unlimited
- **Multi-window:** multiple compose windows can be open simultaneously
- **Draft sync:** changes in window reflect in main window draft list
- **Keyboard shortcuts:** Ctrl+Enter (send), Ctrl+S (save draft), Esc (close)
- **Drop zone:** drag files anywhere on window to attach
- **Status bar:** bottom, shows "Draft saved ✓" or "Sending..." or "Sent ✓"

---

## 13. Empty States

### 13.1 No Messages

```
┌──────────────────────────────────────┐
│                                      │
│           📭                         │
│                                      │
│     No messages yet                  │
│     Add an account to get started    │
│                                      │
│     [+ Add Account]                  │
│                                      │
└──────────────────────────────────────┘
```

### 13.2 No Search Results

```
┌──────────────────────────────────────┐
│                                      │
│           🔍                         │
│                                      │
│     No results for "query"           │
│     Try different keywords           │
│                                      │
└──────────────────────────────────────┘
```

### 13.3 No AI Configured

```
┌──────────────────────────────────────┐
│                                      │
│           ✨                         │
│                                      │
│     AI assistant not configured      │
│     Add an API key to enable AI      │
│                                      │
│     [+ Setup AI]                     │
│                                      │
└──────────────────────────────────────┘
```

### 13.4 No Calendar Events

```
┌──────────────────────────────────────┐
│                                      │
│           📅                         │
│                                      │
│     No events this month             │
│     Click a date to create one       │
│                                      │
└──────────────────────────────────────┘
```
