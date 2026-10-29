using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Strider.UI.Rendering;

/// <summary>
/// Read-only rich text view for mail HTML: renders bold/italic/underline,
/// colours, highlights, font families and sizes through
/// <see cref="HtmlInlinesBuilder"/>, with no WebView and no network access.
///
/// Usage in XAML: <c>&lt;r:HtmlTextBlock Html="{Binding DisplayHtml}"/&gt;</c>
/// </summary>
public class HtmlTextBlock : SelectableTextBlock
{
    public static readonly StyledProperty<string?> HtmlProperty =
        AvaloniaProperty.Register<HtmlTextBlock, string?>(nameof(Html));

    /// <summary>When true, colours coming from the HTML are ignored (plain reading mode).</summary>
    public static readonly StyledProperty<bool> IgnoreColorsProperty =
        AvaloniaProperty.Register<HtmlTextBlock, bool>(nameof(IgnoreColors));

    static HtmlTextBlock()
    {
        HtmlProperty.Changed.AddClassHandler<HtmlTextBlock>((block, _) => block.Rebuild());
        IgnoreColorsProperty.Changed.AddClassHandler<HtmlTextBlock>((block, _) => block.Rebuild());
    }

    public string? Html
    {
        get => GetValue(HtmlProperty);
        set => SetValue(HtmlProperty, value);
    }

    public bool IgnoreColors
    {
        get => GetValue(IgnoreColorsProperty);
        set => SetValue(IgnoreColorsProperty, value);
    }

    private void Rebuild()
    {
        var inlines = HtmlInlinesBuilder.Build(Html);
        Inlines ??= new Avalonia.Controls.Documents.InlineCollection();
        Inlines.Clear();

        if (inlines.Count == 0)
        {
            Inlines.Add(new Avalonia.Controls.Documents.Run(string.Empty));
            return;
        }

        foreach (var inline in inlines)
        {
            if (IgnoreColors && inline is Avalonia.Controls.Documents.Run run)
            {
                run.Foreground = null;
                run.Background = null;
            }

            Inlines.Add(inline);
        }
    }

    /// <summary>Convenience for code paths that need the rendered text only.</summary>
    public static string PlainText(string? html) => HtmlInlinesBuilder.ToPlainText(html);
}
