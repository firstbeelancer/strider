using System.Reflection;
using System.Text;

namespace Strider.Infrastructure.Editor;

/// <summary>
/// Provides the HTML/JS/CSS content for the TipTap editor embedded as resources.
///
/// In production, the TipTap bundle would be produced by a separate npm build step
/// (esbuild/rollup) and copied here. For v0.1 we ship a minimal stub that:
/// 1. Implements the JS side of the EditorBridge protocol.
/// 2. Renders a contenteditable div with basic formatting.
/// 3. Responds to all EditorCommands with document.execCommand() (deprecated but
///    functional) — to be replaced with real TipTap in v0.2.
///
/// This unblocks the C# ↔ JS bridge integration while the full TipTap editor is
/// being built. The protocol is the same, so swapping the stub for real TipTap
/// is a drop-in change.
/// </summary>
internal static class TipTapAssets
{
    private const string ResourceName = "Strider.Infrastructure.Editor.tiptap-editor.html";

    /// <summary>
    /// Returns the full HTML page (with embedded CSS and JS) for the editor.
    /// Caches after first call.
    /// </summary>
    public static string GetEditorHtml()
    {
        var assembly = typeof(TipTapAssets).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource not found: {ResourceName}. " +
                "Ensure Editor/tiptap-editor.html is marked as EmbeddedResource in csproj.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
