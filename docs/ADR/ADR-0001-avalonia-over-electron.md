# ADR-0001: Choose Avalonia 11 over Electron/MAUI

## Status
Accepted

## Date
2026-06-24

## Context
Strider Mail needs a cross-platform desktop UI framework that works on Windows and Linux. The decision affects the entire UI layer, packaging story, performance characteristics, and contributor onboarding for the lifetime of the project.

Options considered:

1. **Electron** (Slack, VS Code, Discord) — Chromium + Node.js, ubiquitous, huge ecosystem
2. **.NET MAUI** (Microsoft) — official .NET 8 desktop+mobile story, XAML, native rendering
3. **Avalonia 11** — third-party .NET, XAML, native Skia rendering, Win/Linux/macOS
4. **Flutter Desktop** — Google, Dart, Skia, stable desktop since 2023
5. **Qt/.NET** — mature C++ framework with .NET bindings

## Decision
Use **Avalonia 11** with the Fluent theme.

## Consequences

Positive:
- Native Skia rendering — no browser engine, ~80 MB binary (vs Electron ~150 MB+)
- Cold start < 1.5 s (Electron: 3-5 s typical)
- Idle RAM < 250 MB (Electron: 300-500 MB typical)
- Full C# 12 / .NET 8 ecosystem — same language as backend (MailKit, BouncyCastle)
- MVVM via CommunityToolkit.Mvvm with source-generated `[ObservableProperty]`
- XAML familiar to WPF/WinUI developers
- Active maintainer community, 11.x is the stable LTS line

Negative:
- Smaller talent pool than Electron (fewer "Avalonia developers" on the market)
- No first-party WebView control (need WebView2/CEF wrappers for the rich text editor)
- Some platform-specific bugs require upstream fixes (cannot patch locally)
- Documentation density lower than Microsoft's MAUI docs

Neutral:
- XAML has a learning curve for developers from web backgrounds
- Skia is the same renderer as Flutter — comparable visual quality

## Alternatives Considered

1. **Electron** — rejected: 150+ MB binaries, 300+ MB RAM idle violates the
   "<250 MB idle" non-functional requirement. Also: JavaScript runtime in a
   "native email client" contradicts the product positioning ("no Electron,
   no browser engine").
2. **.NET MAUI** — rejected: as of .NET 8, Linux support is community-only
   (GTK# backer), not officially supported by Microsoft. Strider Mail targets
   Ubuntu 22.04+ as a first-class platform.
3. **Flutter Desktop** — rejected: would require Dart (different language from
   the .NET backend), splitting the codebase across two ecosystems. Also:
   Flutter's text editing story is weaker than Avalonia's for rich text.
4. **Qt/.NET** — rejected: Qt bindings for .NET are immature; licensing (LGPL
   vs commercial) is more complex than Avalonia's MIT.

## References
- Avalonia UI: https://avaloniaui.net/
- SPEC.md §3.1 (Technology Stack)
- Architecture review by ZAI (F-005 discussion)
