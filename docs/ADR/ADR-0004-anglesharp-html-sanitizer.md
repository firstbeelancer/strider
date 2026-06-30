# ADR-0004: AngleSharp allowlist for HTML sanitization

## Status
Accepted

## Date
2026-06-25

## Context
HTML email is the primary attack surface for mail clients. A single `<script>`
tag, an `onclick` handler, or a `javascript:` URL can execute arbitrary code
in the user's context. The original `MessageReaderViewModel` (commit 62f2363)
assigned raw `TextHtml` to `DisplayHtml` without sanitization, creating an
XSS risk if the rendering layer ever switched to WebView.

The architecture review (F-006) flagged this as a HIGH-severity security issue.

## Decision
Use **AngleSharp 1.0.7** with a custom `HtmlSanitizer` class implementing an
allowlist approach. The sanitizer:

- Parses HTML with `HtmlParser`
- Walks the DOM tree, removing any element not in `AllowedTags`
- Removes any attribute not in `AllowedAttributes`
- Removes all `on*` event handler attributes (defense in depth)
- Strips `javascript:`, `vbscript:`, and `data:text/html` URLs
- Blocks external images by default (privacy / tracking pixel defense); can
  be configured via `allowExternalImages: true`
- Preserves `cid:` (inline attachments) and `data:image/*` (embedded images)

## Consequences

Positive:
- Defense in depth: even if a future migration to WebView happens, sanitized
  HTML cannot contain executable content
- Allowlist (not blocklist) — unknown future tags/attributes are rejected by
  default
- External image blocking defeats tracking pixels (a major privacy win for
  the target audience)
- Configurable per-call: `allowExternalImages: true` for user-trusted senders
  (future feature)

Negative:
- ~5-15 ms sanitization overhead per message on first read (cached after)
- Some legitimate HTML may be stripped if it uses unusual tags (e.g., `<math>`
  is not in the allowlist by default — could be added later)
- AngleSharp adds ~1.5 MB to binary size

Neutral:
- AngleSharp is the de facto .NET HTML parser; well-maintained, MIT license

## Alternatives Considered

1. **Blocklist approach** (regex out `<script>`, `onclick`, etc.) — rejected:
   blocklists are inherently incomplete. New attack vectors appear faster than
   we can update the list.
2. **HtmlSanitizer NuGet package** — considered: it's a thin wrapper around
   AngleSharp with sensible defaults. Rejected because we need custom logic
   (external image blocking, `cid:` preservation) that's easier to maintain
   in our own class.
3. **Server-side sanitization** — N/A: Strider Mail is a client-only app,
   no server to delegate to.
4. **No sanitization, rely on TextBlock (not WebView) rendering** — rejected:
   current TextBlock rendering is "safe by accident" (no JS engine), but
   the moment we add WebView for HTML email (planned), unsanitized content
   would execute. We must sanitize proactively.

## References
- Architecture review by ZAI (finding F-006)
- `src/Strider.Infrastructure/Security/HtmlSanitizer.cs`
- SPEC.md §8.2 (Runtime Security)
- AngleSharp: https://anglesharp.github.io/
