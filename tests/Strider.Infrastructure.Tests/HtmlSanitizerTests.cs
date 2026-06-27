using Strider.Infrastructure.Security;
using FluentAssertions;

namespace Strider.Infrastructure.Tests;

/// <summary>
/// Tests for HtmlSanitizer — verifies that dangerous elements/attributes are
/// stripped while safe content is preserved. This is the F-006 fix.
/// </summary>
public class HtmlSanitizerTests
{
    private readonly HtmlSanitizer _sanitizer = new(allowExternalImages: false);

    [Fact]
    public void Sanitize_NullOrEmpty_ReturnsEmpty()
    {
        _sanitizer.Sanitize(null).Should().BeEmpty();
        _sanitizer.Sanitize("").Should().BeEmpty();
        _sanitizer.Sanitize("   ").Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_RemovesScriptTags()
    {
        var input = "<p>Hello</p><script>alert('xss')</script><p>World</p>";
        var result = _sanitizer.Sanitize(input);
        result.Should().NotContain("<script");
        result.Should().NotContain("alert");
        result.Should().Contain("Hello");
        result.Should().Contain("World");
    }

    [Fact]
    public void Sanitize_RemovesIframeTags()
    {
        var input = "<p>text</p><iframe src=\"https://evil.com\"></iframe>";
        var result = _sanitizer.Sanitize(input);
        result.Should().NotContain("<iframe");
        result.Should().NotContain("evil.com");
    }

    [Fact]
    public void Sanitize_RemovesInlineEventHandlers()
    {
        var input = "<p onclick=\"steal()\" onmouseover=\"hover()\">text</p>";
        var result = _sanitizer.Sanitize(input);
        result.Should().NotContain("onclick");
        result.Should().NotContain("onmouseover");
        result.Should().NotContain("steal");
        result.Should().Contain("text");
    }

    [Fact]
    public void Sanitize_RemovesJavascriptUrls()
    {
        var input = "<a href=\"javascript:alert(1)\">click</a>";
        var result = _sanitizer.Sanitize(input);
        result.Should().NotContain("javascript:");
        result.Should().NotContain("alert");
    }

    [Fact]
    public void Sanitize_PreservesSafeHtml()
    {
        var input = "<p>Hello <strong>world</strong> with <a href=\"https://example.com\">link</a></p>";
        var result = _sanitizer.Sanitize(input);
        result.Should().Contain("<p>");
        result.Should().Contain("<strong>world</strong>");
        result.Should().Contain("href=\"https://example.com\"");
    }

    [Fact]
    public void Sanitize_BlocksExternalImagesByDefault()
    {
        var input = "<img src=\"https://tracker.com/pixel.png\">";
        var result = _sanitizer.Sanitize(input);
        result.Should().NotContain("tracker.com");
        result.Should().NotContain("src=\"https");
    }

    [Fact]
    public void Sanitize_AllowsExternalImagesWhenConfigured()
    {
        var sanitizer = new HtmlSanitizer(allowExternalImages: true);
        var input = "<img src=\"https://example.com/image.png\">";
        var result = sanitizer.Sanitize(input);
        result.Should().Contain("src=\"https://example.com/image.png\"");
    }

    [Fact]
    public void Sanitize_PreservesInlineDataImages()
    {
        var input = "<img src=\"data:image/png;base64,iVBORw0KGgo=\">";
        var result = _sanitizer.Sanitize(input);
        result.Should().Contain("data:image/png");
    }

    [Fact]
    public void Sanitize_PreservesCidImagesForInlineAttachments()
    {
        var input = "<img src=\"cid:attachment123\">";
        var result = _sanitizer.Sanitize(input);
        result.Should().Contain("cid:attachment123");
    }

    [Fact]
    public void Sanitize_RemovesFormElements()
    {
        var input = "<form action=\"https://evil.com\"><input type=\"text\"><button>Submit</button></form>";
        var result = _sanitizer.Sanitize(input);
        result.Should().NotContain("<form");
        result.Should().NotContain("<input");
        result.Should().NotContain("<button");
    }

    [Fact]
    public void Sanitize_RemovesStyleTags()
    {
        var input = "<style>body { background: url('https://tracker.com/pixel.png') }</style><p>text</p>";
        var result = _sanitizer.Sanitize(input);
        result.Should().NotContain("<style");
        result.Should().NotContain("tracker.com");
        result.Should().Contain("text");
    }

    [Fact]
    public void Sanitize_RemovesDangerousDataUrls()
    {
        var input = "<iframe src=\"data:text/html,<script>alert(1)</script>\"></iframe>";
        var result = _sanitizer.Sanitize(input);
        result.Should().NotContain("data:text/html");
        result.Should().NotContain("<iframe");
    }

    [Fact]
    public void Sanitize_PreservesTables()
    {
        var input = "<table><tr><td colspan=\"2\">cell</td></tr></table>";
        var result = _sanitizer.Sanitize(input);
        result.Should().Contain("<table>");
        result.Should().Contain("<tr>");
        result.Should().Contain("<td");
        result.Should().Contain("colspan=\"2\"");
    }

    [Fact]
    public void Sanitize_UnwrapsUnknownElements()
    {
        var input = "<custom-tag>inner text</custom-tag>";
        var result = _sanitizer.Sanitize(input);
        result.Should().NotContain("<custom-tag");
        result.Should().Contain("inner text");
    }
}
