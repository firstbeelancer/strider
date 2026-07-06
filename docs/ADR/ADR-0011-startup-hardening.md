# ADR-0011: Startup Hardening

> **Date:** 2026-07-06
> **Status:** Accepted
> **Author:** ZAI architecture review
> **Supersedes:** none
> **Related:** ADR-0007 (BouncyCastle), ADR-0003 (SQLCipher), Wave 1+2+3

## Context

After v0.1.0-rc1 and v0.1.0-rc2, users reported the desktop client
starting for one or two seconds and then disappearing without a crash
dialog. Investigation in the ZAI architecture review (see
`docs/reviews/ZAI_REVIEW_v2_2026-07-06.md`) identified **six independent
failure paths** that all manifested as "silent crash":

1. F-025 — Strider.Host pinned Avalonia 11.0.10 while Strider.UI took
   the floating `11.*`. MSBuild chose 11.0.10 as primary but the
   transitive Avalonia.Themes.Fluent 11.3.18 was still resolved,
   leading to non-deterministic TypeLoadExceptions at runtime.
2. F-026 — `static DpapiKeychainService()` constructor called
   `Directory.CreateDirectory` eagerly. A failure there propagated as
   `TypeInitializationException`, killing the process before any catch
   handler could observe it.
3. F-027 — Emoji glyphs in XAML with Inter font. Inter has no emoji;
   fallback failed on Linux and intermittently on Windows depending
   on installed Segoe UI Emoji variants.
4. F-028 — DB and keychain lived in Roaming `ApplicationData` (can be
   a non-writable junction on Server Core / sandboxed envs) while
   logs lived in `LocalApplicationData`. Inconsistent and fragile.
5. F-029 — No `AppDomain.UnhandledException` handler installed before
   `StartWithClassicDesktopLifetime` ran. Any exception in
   `OnFrameworkInitializationCompleted` or in a background task killed
   the process without surfacing to the user.
6. F-030 — `dbInit.InitializeAsync().GetAwaiter().GetResult()` blocked
   the UI thread forever on a slow disk, no timeout, no fallback.

## Decision

We adopt a **layered startup hardening** approach:

### Layer 1 — Pin and isolate

- All four projects pin Avalonia **11.0.10**. No floating versions.
- All keychain constructors do **no I/O**. Directory creation is
  deferred to first use and wrapped in try/catch.

### Layer 2 — Canonical paths

- New `Strider.Core.Platform.AppPaths` resolver becomes the single
  source of truth for `AppData`, `Logs`, `Keychain`,
  `DefaultDatabasePath`, `CrashLogPath`.
- All services (`DpapiKeychainService`, `LibsecretKeychainService`,
  `App.OnFrameworkInitializationCompleted`, `Program.ConfigureLogging`)
  go through `AppPaths`. No hard-coded `Environment.SpecialFolder.*`
  outside `AppPaths.cs`.

### Layer 3 — Cross-platform crash reporter

- New `Strider.Core.Platform.CrashReporter`. On Windows invokes
  `user32.dll!MessageBox`. On Linux writes to stderr.
- `Program.Main` calls `CrashReporter.Install()` BEFORE
  `StartWithClassicDesktopLifetime`. This installs both
  `AppDomain.UnhandledException` and
  `TaskScheduler.UnobservedTaskException` handlers.

### Layer 4 — Defensive startup

- `OnFrameworkInitializationCompleted` wraps `dbInit.InitializeAsync()`
  in a 30-second `Task.Wait(TimeSpan)` timeout. On failure the app
  still starts, with a non-terminating crash dialog informing the user.
- `MainWindow` construction is also wrapped. DI failures are caught
  and surfaced via `CrashReporter.Show`.
- `LoadAccountsCommand.Execute(null)` replaced with `LoadAccountsSafelyAsync`
  — explicit try/catch around the async operation.

### Layer 5 — Release pipeline

- `release.yml` publishes **multi-file** by default.
  `PublishSingleFile=true` is opt-in via a `*-single` tag.
- The Linux artifact receives a headless `xvfb-run` smoke-test step
  that runs the binary for at least three seconds to catch Avalonia
  init failures.

## Consequences

### Positive

- v0.1.0-rc3 will surface a clear MessageBox on any startup failure.
- Users on Windows Server Core / locked-down environments will get the
  app working because everything lives under LocalApplicationData now.
- Multi-file publish eliminates the single-file self-extract failure
  class entirely for the default install path.
- Smoke-test catches Avalonia init regressions before release.

### Negative

- DB init can take longer on slow disks. We accept the 30s timeout
  because users running Strider Mail on HDD already know it's slow.
- Multi-file release is ~70 MB instead of ~30 MB (single-file).
  We accept this for the safer first-run experience.
- `AppPaths` is a small extra surface area; if it ever breaks, all
  paths break. Mitigation: it has a try/catch fallback at every level.

### Neutral

- Emoji are gone from the UI for now. They come back as real icons in v0.2.
- We switched from `ViewModel.LoadAccountsCommand.Execute(null)`
  (fire-and-forget) to an explicit async helper. No observable
  behaviour change for users.

## Alternatives Considered

1. **Keep single-file publish** with extra diagnostics. Rejected: the
   self-extract problem affects a non-trivial fraction of users and
   adds 30% binary size for no benefit at scale.
2. **Splash window before DI** to give visual feedback. Rejected:
   doubling the surface area to fix a problem that a 30s timeout
   already resolves adequately.
3. **Async-only startup** (Task-based). Rejected: the Avalonia lifecycle
   is fundamentally `StartWithClassicDesktopLifetime`, and rewriting
   it would mean maintaining a fork.
