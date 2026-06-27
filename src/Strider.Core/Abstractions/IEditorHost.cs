using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// Abstraction over a WebView-based rich text editor (TipTap inside WebView2/CEF).
///
/// The host renders an HTML page containing the TipTap editor and a JS↔C# bridge
/// that exchanges JSON messages. The native Avalonia toolbar (drawn above the
/// WebView) sends formatting commands to the editor via <see cref="ExecuteCommandAsync"/>;
/// the editor reports selection changes back via <see cref="OnSelectionChanged"/>.
///
/// Platform implementations:
/// - Windows: WebView2 (Edge/Chromium runtime, ships with Win10/11)
/// - Linux:   CEF (Chromium Embedded Framework)
///
/// The editor surface is sandboxed: no arbitrary URL navigation, no access to
/// local files. Only the TipTap document model is exposed.
/// </summary>
public interface IEditorHost : IDisposable
{
    /// <summary>True after the underlying WebView has loaded the TipTap HTML.</summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Initializes the WebView and loads the TipTap editor HTML.
    /// Called once after the host control is attached to a window.
    /// </summary>
    Task LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the current HTML content of the editor.
    /// </summary>
    Task<string> GetHtmlAsync(CancellationToken ct = default);

    /// <summary>
    /// Replaces the entire editor content with the given HTML.
    /// Use null or empty string to clear.
    /// </summary>
    Task SetHtmlAsync(string html, CancellationToken ct = default);

    /// <summary>
    /// Executes a formatting command (e.g., "bold", "italic", "insertTable").
    /// See <see cref="EditorCommands"/> for the full list.
    /// </summary>
    Task ExecuteCommandAsync(string command, object? parameter = null, CancellationToken ct = default);

    /// <summary>
    /// Subscribes to selection-change events from the editor (e.g., when the
    /// cursor moves into a bold span, the toolbar should highlight the B button).
    /// </summary>
    IDisposable SubscribeToSelectionChanges(Action<EditorSelection> handler);

    /// <summary>
    /// Subscribes to content-change events (fired on every keystroke).
    /// Used for autosave (every 30s) and "dirty" tracking.
    /// </summary>
    IDisposable SubscribeToContentChanges(Action<string> handler);
}

/// <summary>
/// Current editor selection state — reported from JS to C#.
/// </summary>
public sealed class EditorSelection
{
    /// <summary>True if the cursor is inside a bold span.</summary>
    public bool IsBold { get; init; }

    /// <summary>True if the cursor is inside an italic span.</summary>
    public bool IsItalic { get; init; }

    /// <summary>True if the cursor is inside an underlined span.</summary>
    public bool IsUnderline { get; init; }

    /// <summary>True if the cursor is inside a strikethrough span.</summary>
    public bool IsStrikethrough { get; init; }

    /// <summary>Current block type: "paragraph", "h1", "h2", "h3", "blockquote", "codeBlock".</summary>
    public string BlockType { get; init; } = "paragraph";

    /// <summary>Current alignment: "left", "center", "right", "justify".</summary>
    public string Alignment { get; init; } = "left";

    /// <summary>Current list type: null, "bulletList", "orderedList".</summary>
    public string? ListType { get; init; }

    /// <summary>True if the cursor is inside a link.</summary>
    public bool IsLink { get; init; }

    /// <summary>True if the cursor is inside a code block.</summary>
    public bool IsCodeBlock { get; init; }

    /// <summary>Font size at the current selection, or null if default.</summary>
    public int? FontSize { get; init; }

    /// <summary>Font family at the current selection, or null if default.</summary>
    public string? FontFamily { get; init; }
}

/// <summary>
/// All editor formatting commands supported by the TipTap setup.
/// Keep in sync with the JS side (see TipTapAssets.html).
/// </summary>
public static class EditorCommands
{
    // Inline formatting
    public const string Bold = "bold";
    public const string Italic = "italic";
    public const string Underline = "underline";
    public const string Strike = "strike";
    public const string Subscript = "subscript";
    public const string Superscript = "superscript";

    // Inline styles
    public const string SetFontFamily = "setFontFamily";
    public const string SetFontSize = "setFontSize";
    public const string SetTextColor = "setTextColor";
    public const string SetHighlightColor = "setHighlightColor";

    // Block formatting
    public const string SetParagraph = "setParagraph";
    public const string SetHeading = "setHeading";
    public const string SetBlockquote = "setBlockquote";
    public const string SetCodeBlock = "setCodeBlock";

    // Alignment
    public const string AlignLeft = "alignLeft";
    public const string AlignCenter = "alignCenter";
    public const string AlignRight = "alignRight";
    public const string AlignJustify = "alignJustify";

    // Lists
    public const string ToggleBulletList = "toggleBulletList";
    public const string ToggleOrderedList = "toggleOrderedList";
    public const string SinkListItem = "sinkListItem";
    public const string LiftListItem = "liftListItem";

    // Insert
    public const string InsertTable = "insertTable";
    public const string InsertHorizontalRule = "insertHorizontalRule";
    public const string InsertLink = "insertLink";
    public const string InsertImage = "insertImage";
    public const string InsertCode = "insertCode";

    // History
    public const string Undo = "undo";
    public const string Redo = "redo";

    // Clipboard
    public const string PasteAsPlainText = "pasteAsPlainText";
    public const string ClearFormatting = "clearNodes";
}
