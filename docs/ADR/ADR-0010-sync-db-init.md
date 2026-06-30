# ADR-0010: Synchronous DB initialization in App.OnFrameworkInitializationCompleted

## Status
Accepted

## Date
2026-06-24

## Context
`App.OnFrameworkInitializationCompleted` runs on the UI thread at startup.
Before the main window can be shown, the database must be initialized
(migrations applied) because ViewModels query the DB immediately. The original
code used:

```csharp
dbInit.InitializeAsync().GetAwaiter().GetResult();
```

This is a sync-over-async pattern, generally discouraged because it can
deadlock on UI threads that have a synchronization context.

## Decision
Accept the **sync-over-async `.GetAwaiter().GetResult()`** pattern for the
one-time DB initialization at startup. This is a deliberate trade-off:

- DB init takes ~50-200 ms (creating tables, applying first migration)
- The main window cannot meaningfully render without a DB (all ViewModels
  would fail on first query)
- Showing a "Loading..." window then transitioning to the real window adds
  complexity for ~200 ms of benefit
- Avalonia's classic desktop lifetime does not have a synchronization context
  on the UI thread (unlike WPF), so deadlock risk is minimal

## Consequences

Positive:
- Simple startup flow — main window appears once DB is ready
- No "loading → ready" transition flicker
- Failures (e.g., DB corruption) surface immediately at startup, not after
  the user tries to do something

Negative:
- 50-200 ms of UI unresponsiveness at startup (acceptable per NFR: <1.5 s
  cold start budget)
- If a future migration takes >1 s, this will feel like a hang — should be
  revisited with a splash screen at that point
- `.GetAwaiter().GetResult()` is a code smell that triggers linting warnings

Neutral:
- Same pattern used by most Avalonia apps for one-time startup work

## Alternatives Considered

1. **Splash window + async init** — deferred: worth doing if startup exceeds
   500 ms. Currently ~200 ms, not worth the complexity.
2. **Lazy DB initialization on first query** — rejected: every ViewModel
   would need to handle "DB not ready" state, spreading complexity across
   the codebase.
3. **Background DB init with retry-on-failure** — rejected: if DB init fails,
   the app cannot function; better to fail fast and show an error dialog.
4. **`async void Main`** — rejected: Avalonia's `StartWithClassicDesktopLifetime`
   is synchronous; making `Main` async would require restructuring the entry
   point for marginal benefit.

## References
- `src/Strider.UI/App.axaml.cs` (`OnFrameworkInitializationCompleted`)
- SPEC.md §6.1 (Performance — startup <1.5 s)
