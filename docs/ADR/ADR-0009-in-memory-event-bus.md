# ADR-0009: ConcurrentDictionary-based in-process event bus

## Status
Accepted

## Date
2026-06-24

## Context
Strider Mail needs a pub/sub mechanism for in-process events: when a new
message arrives via IMAP sync, multiple ViewModels need to update (folder
unread count, message list, toast notification). The event bus must be
thread-safe (sync runs on background threads, ViewModels update on UI thread)
and lightweight (no external messaging infrastructure).

## Decision
Use a **simple `ConcurrentDictionary<Type, List<Delegate>>`**-based event bus
implemented in `InMemoryEventBus`. Subscribers register via
`Subscribe<T>(Action<T>)` and receive an `IDisposable` for unsubscription.
Publishers call `Publish<T>(T event)` which synchronously invokes all
registered handlers.

## Consequences

Positive:
- Zero dependencies — no MediatR, no Reactive Extensions
- Simple API — `Subscribe`/`Publish`/`Dispose`
- Thread-safe via `ConcurrentDictionary` + per-type `List<Delegate>` with
  `lock`
- Handlers that throw don't crash the publisher (caught and swallowed in
  `InMemoryEventBus.Publish`)

Negative:
- Synchronous dispatch — long-running handlers block the publisher. UI
  updates must marshal to the UI thread via `Dispatcher.UIThread.Post`.
- No built-in thread marshalling — subscribers are responsible for thread
  safety
- No event ordering guarantees across types
- In-process only — no cross-process or cross-machine events (fine for a
  desktop app)

Neutral:
- Events are reference types; subscribers must not mutate them

## Alternatives Considered

1. **MediatR** — rejected: heavyweight for our needs (5+ MB of deps), designed
   for request/response not pure pub/sub, adds unnecessary abstractions
   (notifications, handlers, pipelines).
2. **Reactive Extensions (Rx.NET)** — considered: powerful for complex event
   streams, but steep learning curve and overkill for our use case
   (10 event types, simple fan-out).
3. **Avalonia's built-in `IObservable` infrastructure** — rejected: tightly
   coupled to UI thread, not suitable for events originating on background
   threads (IMAP sync).
4. **Channel<T> / System.Threading.Channels** — rejected: designed for
   producer-consumer queues, not fan-out pub/sub.

## References
- `src/Strider.Infrastructure/Services/InMemoryEventBus.cs`
- `src/Strider.Core/Abstractions/IEventBus.cs`
- SPEC.md §4.3 (Data Flow: Receiving New Email)
