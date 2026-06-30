# ADR-0007: BouncyCastle for PGP (vs GnuPG subprocess)

## Status
Accepted

## Date
2026-06-24

## Context
Strider Mail needs PGP encryption, signing, and key management. The two
realistic options are:

1. **BouncyCastle.Cryptography** — pure .NET PGP implementation
2. **GnuPG subprocess** — shell out to `gpg` / `gpg-agent`

The decision affects deployment (extra binary?), security auditability,
cross-platform consistency, and contributor experience.

## Decision
Use **BouncyCastle.Cryptography 2.3.0** for all PGP operations.

## Consequences

Positive:
- Single-process architecture — no subprocess management, no IPC with gpg-agent
- Cross-platform identical behavior (no "gpg on Linux, gpg4win on Windows" split)
- Keys never leave the process — no temp files, no agent socket to secure
- Pure managed code — auditable, debuggable, no native vulnerabilities
- MIT license, compatible with our project

Negative:
- BouncyCastle PGP API is verbose and easy to misuse (see initial stub bugs:
   `RsaSign` vs `RsaGeneral`, `SetHashedSubpackets` requiring `.Generate()`)
- Larger attack surface within our own code — bugs in our PGP usage are our
   responsibility, not GnuPG's
- No automatic keyring integration with system GPG (users cannot share keys
   with `gpg` CLI without explicit import/export)

Neutral:
- BouncyCastle is the de facto .NET crypto library; same choice as MimeKit,
  MailKit, and most .NET security tooling

## Alternatives Considered

1. **GnuPG subprocess** — rejected:
   - Requires `gpg` installed on user's machine (not default on Windows)
   - Different versions on different platforms (gpg 2.2 vs 2.4 vs 2.5)
   - Subprocess management adds failure modes (gpg-agent not running, locale
     issues, password prompt UX mismatch)
   - Harder to audit — security depends on external binary
2. **OpenPGP.js via WebView** — rejected: would couple PGP to the WebView
   editor host; also slower (JS crypto vs native .NET)
3. **PgpCore wrapper** — considered: it's a thin wrapper over BouncyCastle.
   Rejected because we need fine-grained control (custom subpackets, one-pass
   signatures) that's easier in raw BouncyCastle.

## References
- SPEC.md §10 (PGP Implementation Details)
- `src/Strider.Infrastructure/Security/BouncyCastlePgpService.cs`
- BouncyCastle: https://bouncycastle.org/csharp/
