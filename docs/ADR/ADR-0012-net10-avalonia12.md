# ADR-0012: Move to .NET 10, C# 14 and Avalonia 12

Date: 2026-10-29 · Status: Accepted

## Context
.NET 8 LTS support ends 10 Nov 2026. Avalonia 12 (Apr 2026) recommends .NET 10,
turns compiled bindings on by default and ships HarfBuzz text shaping.

## Decision
- `net10.0`, `LangVersion 14.0`, set once in `Directory.Build.props`.
- Central package management (`Directory.Packages.props`) — one version per package,
  so the Avalonia version split behind the RC1/RC2 crash (F-025) cannot come back.
- Avalonia 12.1.x, Microsoft.Extensions 10.x.
- `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher` instead of
  `Microsoft.Data.Sqlite` (which also pulled the plain `e_sqlite3` bundle — two native
  SQLite builds in one app, ambiguous which one `Batteries_V2.Init()` loads).
- FluentAssertions stays on 6.x (v8 is commercially licensed).

## Consequences
- Compiled bindings caught dead bindings (`PreviousCommand`/`NextCommand` in the
  account wizard and calendar) at build time — fixed.
- `Watermark` → `PlaceholderText`.
- CI/release use the 10.0.x SDK.
