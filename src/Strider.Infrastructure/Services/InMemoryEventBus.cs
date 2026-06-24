using System.Collections.Concurrent;
using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Services;

/// <summary>
/// Simple in-process event bus implementation.
/// </summary>
public class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

    public void Publish<T>(T @event) where T : class
    {
        if (_handlers.TryGetValue(typeof(T), out var handlers))
        {
            foreach (var handler in handlers)
            {
                try
                {
                    ((Action<T>)handler)(@event);
                }
                catch
                {
                    // Log but don't crash on handler errors
                }
            }
        }
    }

    public IDisposable Subscribe<T>(Action<T> handler) where T : class
    {
        var handlers = _handlers.GetOrAdd(typeof(T), _ => new List<Delegate>());
        lock (handlers)
        {
            handlers.Add(handler);
        }

        return new Unsubscriber(handlers, handler);
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly List<Delegate> _handlers;
        private readonly Delegate _handler;

        public Unsubscriber(List<Delegate> handlers, Delegate handler)
        {
            _handlers = handlers;
            _handler = handler;
        }

        public void Dispose()
        {
            lock (_handlers)
            {
                _handlers.Remove(_handler);
            }
        }
    }
}
