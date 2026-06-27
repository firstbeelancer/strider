using System.Text.Json;
using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Editor;

/// <summary>
/// Base class for platform-specific WebView editor hosts.
///
/// Implements the <see cref="IEditorHost"/> interface in terms of an
/// <see cref="EditorBridge"/>. Platform-specific subclasses (WebView2 on Windows,
/// CEF on Linux) only need to:
/// 1. Override <see cref="LoadHtmlIntoWebViewAsync"/> — feed the HTML to the
///    platform WebView and await its load completion.
/// 2. Override <see cref="PostMessageToWebView"/> — send a JSON string to the
///    platform WebView's JS context.
/// 3. Wire up incoming messages from the WebView to <see cref="EditorBridge.HandleIncomingMessage"/>.
///
/// The HTML/JS/CSS content for the TipTap editor is provided by
/// <see cref="TipTapAssets"/>. The bridge expects the JS side to:
/// - Receive postMessage with {id, type:"request", command, parameter}.
/// - Reply with {id, type:"response", result, error}.
/// - Emit {type:"event", event:"selectionChanged", data:{...}} on selection change.
/// - Emit {type:"event", event:"contentChanged", data:"html"} on content change.
/// </summary>
public abstract class WebViewEditorHost : IEditorHost
{
    protected readonly EditorBridge Bridge = new();
    private bool _loaded;
    private readonly object _loadLock = new();

    protected WebViewEditorHost()
    {
        // Wire up the bridge's outbound messages to the platform-specific post
        Bridge.SendMessage = json => PostMessageToWebView(json);
    }

    public bool IsLoaded
    {
        get { lock (_loadLock) return _loaded; }
    }

    /// <summary>
    /// Platform-specific: load the given HTML into the WebView and await completion.
    /// </summary>
    protected abstract Task LoadHtmlIntoWebViewAsync(string html, CancellationToken ct);

    /// <summary>
    /// Platform-specific: send a JSON string to the WebView's JS context.
    /// Implementations typically call window.chrome.webview.postMessage (Win)
    /// or CefPostMessage (Linux).
    /// </summary>
    protected abstract void PostMessageToWebView(string json);

    public async Task LoadAsync(CancellationToken ct = default)
    {
        lock (_loadLock)
        {
            if (_loaded) return;
        }

        var html = TipTapAssets.GetEditorHtml();
        await LoadHtmlIntoWebViewAsync(html, ct);

        lock (_loadLock)
        {
            _loaded = true;
        }
    }

    public async Task<string> GetHtmlAsync(CancellationToken ct = default)
    {
        EnsureLoaded();
        var result = await Bridge.SendRequestAsync("getHtml", null, ct);
        return result?.Deserialize<string>() ?? "";
    }

    public async Task SetHtmlAsync(string html, CancellationToken ct = default)
    {
        EnsureLoaded();
        await Bridge.SendRequestAsync("setHtml", html, ct);
    }

    public async Task ExecuteCommandAsync(string command, object? parameter = null, CancellationToken ct = default)
    {
        EnsureLoaded();
        await Bridge.SendRequestAsync(command, parameter, ct);
    }

    public IDisposable SubscribeToSelectionChanges(Action<EditorSelection> handler)
    {
        return Bridge.SubscribeToSelectionChanges(handler);
    }

    public IDisposable SubscribeToContentChanges(Action<string> handler)
    {
        return Bridge.SubscribeToContentChanges(handler);
    }

    /// <summary>
    /// Called by platform code when a message arrives from the WebView's JS context.
    /// </summary>
    protected void OnMessageReceived(string json)
    {
        Bridge.HandleIncomingMessage(json);
    }

    private void EnsureLoaded()
    {
        if (!IsLoaded)
            throw new InvalidOperationException("Editor not loaded. Call LoadAsync() first.");
    }

    public virtual void Dispose()
    {
        Bridge.Dispose();
    }
}
