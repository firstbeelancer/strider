# ADR-0006: TipTap + WebView for rich text editor

## Status
Accepted

## Date
2026-06-24

## Context
The composer requires a full-featured WYSIWYG editor: tables, inline images,
emoji, code blocks, font/color selection, paste-from-Word cleanup. Avalonia
has no built-in rich text editor of this caliber. Building one from scratch
was estimated at 6+ months of dedicated work.

The decision affects binary size, startup time, security surface, and the
contributor experience for anyone touching the composer.

## Decision
Use an **embedded WebView** (WebView2 on Windows, CEF on Linux) hosting the
**TipTap** editor (MIT-licensed, headless, JSON document model). The native
Avalonia layer draws the toolbar; the WebView renders the editing surface.
Communication is via a JS↔C# JSON bridge (`EditorBridge`).

For v0.1 we ship a stub implementation using `document.execCommand` (deprecated
but functional) inside `tiptap-editor.html`. v0.2 will replace the stub with a
real TipTap bundle produced by an npm build step.

## Consequences

Positive:
- Production-quality editing immediately (TipTap is mature, used by Atlassian,
  GitLab, Linear)
- Tables, code blocks, emoji, paste cleanup all "just work"
- JSON document model enables clean serialization and future collaborative
  editing
- Native toolbar keeps the rest of the app consistent (no visual jarring)

Negative:
- WebView adds ~50 MB to Windows install (WebView2 runtime) and ~150 MB to
  Linux (CEF bundled)
- Two rendering engines in one app — potential for visual inconsistency
  between native controls and WebView content
- JS↔C# bridge adds latency (~1-5 ms per round-trip); acceptable for editor
  commands but not for high-frequency updates
- Security surface: WebView must be sandboxed (no arbitrary navigation,
  no file access) — enforced by `WebViewEditorHost` base class

Neutral:
- WebView2 ships with Windows 10/11; Linux users need CEF (bundled in AppImage)

## Alternatives Considered

1. **Custom Avalonia rich text editor** — rejected: 6+ month effort for
   feature parity with TipTap. Tables alone (resize, merge, paste-from-Excel)
   would take 2 months.
2. **TinyMCE** — considered: more established than TipTap, but heavier
   (~1 MB JS vs TipTap's ~200 KB), non-MIT license for advanced features.
3. **Quill** — considered: simpler than TipTap, but less extensible (no
   first-class table support without paid module).
4. **AvaloniaEdit + custom extensions** — rejected: AvaloniaEdit is a code
   editor, not a WYSIWYG editor. Adapting it for rich text would be similar
   effort to building from scratch.

## References
- SPEC.md §3.2 (Rich Text Editor)
- `src/Strider.Infrastructure/Editor/WebViewEditorHost.cs`
- `src/Strider.Infrastructure/Editor/EditorBridge.cs`
- TipTap: https://tiptap.dev/
