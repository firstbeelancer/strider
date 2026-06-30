# ADR-0003: SQLCipher for at-rest database encryption

## Status
Accepted

## Date
2026-06-25

## Context
The local SQLite database stores all cached email content, PGP private keys,
account sync state, calendar events, and signatures. SPEC.md §F7.1 requires
"Local DB with encryption (SQLCipher or native SQLite encryption)". The
original implementation (commit 62f2363) used plain `Microsoft.Data.Sqlite`
without any encryption — the database file was readable by anyone with file
system access.

The architecture review (F-009) flagged this as a HIGH-severity security issue.

## Decision
Use **SQLitePCLRaw.bundle_e_sqlcipher 2.1.10** with Microsoft.Data.Sqlite's
`Password=` connection string parameter. The encryption key is a 32-byte random
value generated on first launch, stored in the OS keychain under
`strider:database:key` (via `KeychainKeys.DatabaseKey()`).

Migration from legacy plaintext databases is handled by
`EncryptedSqliteConnectionFactory.MigrateToEncryptedAsync`, which:
1. Detects plaintext files by the "SQLite format 3" magic header
2. ATTACHes a new encrypted database with the key
3. Copies schema + data via SQL `INSERT INTO encrypted.X SELECT * FROM X`
4. Backs up the plaintext file as `.plaintext.bak`, then deletes it on success

## Consequences

Positive:
- At-rest encryption satisfies F7.1 and closes the F-009 finding
- Even if the database file is exfiltrated (stolen laptop, cloud backup leak),
  content is unreadable without the key
- Key never touches disk — lives only in the OS keychain
- Transparent to application code — same `SqliteConnection` API, just a longer
  connection string

Negative:
- ~5-10% performance overhead on encrypted I/O (acceptable for email workloads)
- Key loss = data loss — if the keychain entry is deleted, the DB is unrecoverable
- One-time migration cost on first launch after upgrade (~1-2 s for 10 MB DB)

Neutral:
- SQLCipher is well-audited and widely deployed (Signal, Bitcoin Core, 1Password)
- Bundle pulls in native binaries for Win/Linux/macOS — adds ~3 MB to publish size

## Alternatives Considered

1. **Microsoft.Data.Sqlite native encryption** — rejected: Microsoft.Data.Sqlite
   has no built-in encryption; the docs explicitly recommend SQLCipher.
2. **Application-layer encryption (encrypt columns manually)** — rejected: would
   require encrypting every column that might contain sensitive data (subject,
   body, from_address, ...), missing index support on encrypted data, and
   requiring explicit key management at every call site.
3. **EFS / FileVault (OS-level file encryption)** — rejected: depends on user
   having full-disk encryption enabled; not all users do. SQLCipher is
   defense-in-depth regardless of OS config.
4. **.NET-only encryption libraries (e.g., SQLiteCrypt)** — rejected: less
   battle-tested than SQLCipher, smaller community, paid licensing for some.

## References
- Architecture review by ZAI (finding F-009)
- `src/Strider.Infrastructure/Persistence/EncryptedSqliteConnectionFactory.cs`
- SQLCipher: https://www.zetetic.net/sqlcipher/
- SPEC.md §8 (Security & Secrets Policy)
