using System.Text.Json;
using System.Text.Json.Serialization;

namespace Strider.Infrastructure.Editor;

/// <summary>
/// JSON message format exchanged between C# (EditorBridge) and JS (TipTap bridge).
///
/// Two message types:
/// 1. Request (C# → JS): execute a command or query state.
/// 2. Event   (JS → C#): selection changed, content changed, etc.
///
/// Wire format (JSON, sent via window.chrome.webview.postMessage on WebView2 or
/// CefPostMessage on CEF):
/// <code>
/// { "id": 42, "type": "request", "command": "bold", "parameter": null }
/// { "id": 42, "type": "response", "result": null, "error": null }
/// { "type": "event", "event": "selectionChanged", "data": { "isBold": true, ... } }
/// </code>
/// </summary>
public sealed class EditorMessage
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("parameter")]
    public JsonElement? Parameter { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    public static string Serialize(EditorMessage msg) =>
        JsonSerializer.Serialize(msg, EditorJsonOptions.Default);

    public static EditorMessage? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<EditorMessage>(json, EditorJsonOptions.Default);
        }
        catch (JsonException)
        {
            // Invalid JSON from JS side — log and ignore, don't crash the bridge
            return null;
        }
    }
}

internal static class EditorJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
