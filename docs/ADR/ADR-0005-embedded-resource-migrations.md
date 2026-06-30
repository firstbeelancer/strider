# ADR-0005: Embedded resource SQL migrations vs FluentMigrator

## Status
Accepted

## Date
2026-06-25

## Context
The original `DatabaseInitializer` (commit 62f2363) had two problems:

1. The schema was duplicated — once in `Schema.sql` (read from disk) and once
   inline in `GetEmbeddedSchema()` as a fallback. Maintaining both was a
   recipe for drift.
2. There was no migration mechanism — `CREATE TABLE IF NOT EXISTS` worked for
   v0.1, but any future schema change (new column, new index) would not be
   applied to existing user databases.

The architecture review (F-013, F-018) flagged both as MEDIUM-severity issues.

## Decision
Use **embedded .sql resources** under `Persistence/Migrations/`, named
`NNNN_description.sql` (e.g., `0001_initial.sql`). The `DatabaseInitializer`
class:

1. Creates a `schema_migrations` table tracking applied versions
2. Discovers migration resources via reflection on the assembly manifest
3. Applies pending migrations in version order, each in its own transaction
4. Records the version + name + applied_at in `schema_migrations`

PRAGMA statements (e.g., `journal_mode=WAL`) are extracted and run outside
the transaction because SQLite doesn't allow PRAGMAs inside transactions.

## Consequences

Positive:
- Zero external dependencies — no FluentMigrator or DbUp NuGet packages
- Migrations are plain SQL — any DBA or developer can read/review them
- Embedded resources ship inside the assembly — no file-system path issues
- Idempotent: re-running `InitializeAsync` is safe (checks `schema_migrations`)
- Easy to add new migrations: drop a new `.sql` file, rebuild

Negative:
- No automatic Down migrations (additive-only by convention; destructive
  changes require manual SQL with backup)
- No schema diffing — migrations are written by hand
- No templating — repeated boilerplate (e.g., per-table audit columns) is
  copied manually

Neutral:
- Migrations are immutable once released — never edit a shipped migration,
  always add a new one

## Alternatives Considered

1. **FluentMigrator** — rejected: powerful but heavyweight (~5 MB of deps),
   adds a DSL we'd need to learn, and embedded resources are simpler for our
   schema complexity.
2. **DbUp** — considered: similar embedded-SQL approach, but adds a dependency
   for what is ~100 lines of code in our `DatabaseInitializer`.
3. **EF Core migrations** — rejected: we use Dapper (not EF Core) for data
   access. Mixing EF migrations with Dapper would create two sources of truth
   for schema.
4. **Raw SQL files on disk (not embedded)** — rejected: file-system paths
   are fragile across publish modes (single-file, framework-dependent, etc.).
   Embedded resources always work.

## References
- Architecture review by ZAI (findings F-013, F-018)
- `src/Strider.Infrastructure/Persistence/DatabaseInitializer.cs`
- `src/Strider.Infrastructure/Persistence/Migrations/0001_initial.sql`
