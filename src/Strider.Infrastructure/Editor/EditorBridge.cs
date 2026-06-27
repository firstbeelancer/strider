using System.Text.Json;
using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Editor;

/// <summary>
/// Manages the C# side of the JS↔C# editor bridge.
///
/// - Tracks pending requests with monotonic IDs.
/// - Resolves TaskCompletionSource when matching response arrives.
/// - Dispatches events (selectionChanged, contentChanged) to subscribers.
///
/// Platform-specific code (WebView2 host, CEF host) feeds raw JSON strings in
/// via <see cref="HandleIncomingMessage"/> and sends raw JSON strings out via
/// the <see cref="SendMessage"/> callback.
/// </summary>
public sealed class EditorBridge : IDisposable
{
    private int _nextId = 0;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement?>> _pending = new();
    private readonly object _pendingLock = new();
    private readonly List<Action<EditorSelection>> _selectionHandlers = new();
    private readonly List<Action<string>> _contentHandlers = new();
    private readonly object _handlersLock = new();

    /// <summary>
    /// Set by the platform host. Called when the bridge has a JSON message ready
    /// to send to the JS side (via window.chrome.webview.postMessage on Win,
    /// CefPostMessage on Linux).
    /// </summary>
    public Action<string>? SendMessage { get; set; }

    /// <summary>
    /// Called by the platform host when a JSON message arrives from the JS side.
    /// Dispatches to the appropriate handler: response → pending TaskCompletionSource,
    /// event → subscriber list.
    /// </summary>
    public void HandleIncomingMessage(string json)
    {
        var msg = EditorMessage.Deserialize(json);
        if (msg == null) return;

        switch (msg.Type)
        {
            case "response":
                HandleResponse(msg);
                break;
            case "event":
                HandleEvent(msg);
                break;
            case "request":
                // Future: handle JS-initiated requests (e.g., JS asking C# for a token)
                break;
        }
    }

    /// <summary>
    /// Sends a command to the JS editor and awaits a response.
    /// </summary>
    public Task<JsonElement?> SendRequestAsync(string command, object? parameter = null, CancellationToken ct = default)
    {
        if (SendMessage == null)
            throw new InvalidOperationException("SendMessage callback not set — bridge is not attached to a host");

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_pendingLock)
        {
            _pending[id] = tcs;
        }

        ct.Register(() =>
        {
            lock (_pendingLock)
            {
                _pending.Remove(id);
            }
            tcs.TrySetCanceled();
        });

        var msg = new EditorMessage
        {
            Id = id,
            Type = "request",
            Command = command,
            Parameter = parameter == null ? null : JsonSerializer.SerializeToElement(parameter, EditorJsonOptions.Default),
        };

        SendMessage(EditorMessage.Serialize(msg));

        return tcs.Task;
    }

    /// <summary>
    /// Subscribes to selection-changed events from the editor.
    /// Returns an IDisposable that unsubscribes when disposed.
    /// </summary>
    public IDisposable SubscribeToSelectionChanges(Action<EditorSelection> handler)
    {
        lock (_handlersLock)
        {
            _selectionHandlers.Add(handler);
        }
        return new ActionUnsubscriber<EditorSelection>(_selectionHandlers, handler, _handlersLock);
    }

    /// <summary>
    /// Subscribes to content-changed events (fired on every keystroke).
    /// The string parameter is the current HTML content.
    /// </summary>
    public IDisposable SubscribeToContentChanges(Action<string> handler)
    {
        lock (_handlersLock)
        {
            _contentHandlers.Add(handler);
        }
        return new ActionUnsubscriber<string>(_contentHandlers, handler, _handlersLock);
    }

    private void HandleResponse(EditorMessage msg)
    {
        if (msg.Id == null) return;
        TaskCompletionSource<JsonElement?>? tcs;
        lock (_pendingLock)
        {
            if (!_pending.Remove(msg.Id.Value, out tcs)) return;
        }

        if (!string.IsNullOrEmpty(msg.Error))
        {
            tcs.TrySetException(new InvalidOperationException($"Editor error: {msg.Error}"));
        }
        else
        {
            tcs.TrySetResult(msg.Result);
        }
    }

    private void HandleEvent(EditorMessage msg)
    {
        switch (msg.Event)
        {
            case "selectionChanged":
                if (msg.Data == null) return;
                var selection = msg.Data.Value.Deserialize<EditorSelection>(EditorJsonOptions.Default);
                if (selection == null) return;
                lock (_handlersLock)
                {
                    foreach (var h in _selectionHandlers) h(selection);
                }
                break;

            case "contentChanged":
                var html = msg.Data?.Deserialize<string>(EditorJsonOptions.Default) ?? "";
                lock (_handlersLock)
                {
                    foreach (var h in _contentHandlers) h(html);
                }
                break;
        }
    }

    public void Dispose()
    {
        lock (_pendingLock)
        {
            foreach (var kvp in _pending)
            {
                kvp.Value.TrySetCanceled();
            }
            _pending.Clear();
        }
        lock (_handlersLock)
        {
            _selectionHandlers.Clear();
            _contentHandlers.Clear();
        }
    }

    private sealed class ActionUnsubscriber<T> : IDisposable
    {
        private readonly List<Action<T>> _list;
        private readonly Action<T> _item;
        private readonly object _lock;

        public ActionUnsubscriber(List<Action<T>> list, Action<T> item, object @lock)
        {
            _list = list;
            _item = item;
            _lock = @lock;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _list.Remove(_item);
            }
        }
    }
}
