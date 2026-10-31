using FluentAssertions;
using Strider.Core.Text;

namespace Strider.Core.Tests;

/// <summary>
/// The composer's formatting model: selection → style spans → HTML.
/// These tests pin the behaviour the UI depends on (split on overlap, toggle
/// off, clear colour, correct HTML encoding).
/// </summary>
public class RichTextDocumentTests
{
    private static RichTextDocument Doc(string text) => new(text);

    [Fact]
    public void ToHtml_WithoutSpans_WrapsTextAndKeepsLineBreaks()
    {
        var doc = Doc("Hello\nworld");
        doc.DefaultStyle = new TextStyle { FontFamily = "Georgia", FontSize = 15, Color = "#112233" };

        var html = doc.ToHtml();

        html.Should().StartWith("<div style=\"font-family:'Georgia',sans-serif;font-size:15px;color:#112233;\">");
        html.Should().Contain("Hello<br>world");
        html.Should().EndWith("</div>");
    }

    [Fact]
    public void ToHtml_EncodesHtmlSpecialCharacters()
    {
        var html = Doc("<script>alert(\"x\")</script> & co").ToHtml();

        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
        html.Should().Contain("&amp; co");
    }

    [Fact]
    public void Apply_ColorsExactlyTheSelectedRange()
    {
        var doc = Doc("red word here");
        doc.Apply(0, 3, new TextStyle { Color = "#E5484D" });

        var html = doc.ToHtml();

        html.Should().Contain("<span style=\"color:#E5484D;\">red</span>");
        html.Should().Contain("> word here");
        // Nothing but the selected word is wrapped.
        html.Split("<span").Length.Should().Be(2);
    }

    [Fact]
    public void Apply_SplitsAnExistingSpan_WhenASecondStyleOverlaps()
    {
        var doc = Doc("0123456789");
        doc.Apply(0, 10, new TextStyle { FontFamily = "Georgia" });   // whole text
        doc.Apply(3, 4, new TextStyle { Color = "#3E63DD" });        // 3456 coloured

        doc.Spans.Should().HaveCount(3);
        doc.Spans.Select(s => (s.Start, s.Length)).Should().Equal((0, 3), (3, 4), (7, 3));

        var html = doc.ToHtml();
        html.Should().Contain("<span style=\"color:#3E63DD;font-family:'Georgia',sans-serif;\">3456</span>");
    }

    [Fact]
    public void Apply_TwoIndependentRanges_KeepTheirOwnStyles()
    {
        var doc = Doc("one two three");
        doc.Apply(0, 3, new TextStyle { Color = "#E5484D" });
        doc.Apply(8, 5, new TextStyle { Bold = true });

        doc.Spans.Should().HaveCount(2);
        doc.ToHtml().Should().Contain("<span style=\"font-weight:bold;\">three</span>");
    }

    [Fact]
    public void Apply_SameRangeTwice_MergesStyles()
    {
        var doc = Doc("word");
        doc.Apply(0, 4, new TextStyle { Color = "#E5484D" });
        doc.Apply(0, 4, new TextStyle { Bold = true });

        doc.Spans.Should().ContainSingle();
        doc.Spans[0].Style.Color.Should().Be("#E5484D");
        doc.Spans[0].Style.Bold.Should().BeTrue();
    }

    [Fact]
    public void Clear_RemovesOnlyTheRequestedProperty()
    {
        var doc = Doc("coloured bold");
        doc.Apply(0, 13, new TextStyle { Color = "#E5484D", Bold = true });

        doc.Clear(0, 8, s => s.WithoutColor());

        // "coloured" loses the colour but stays bold; " bold" keeps both.
        doc.Spans.Should().HaveCount(2);
        doc.Spans[0].Style.Color.Should().BeNull();
        doc.Spans[0].Style.Bold.Should().BeTrue();
        doc.Spans[1].Style.Color.Should().Be("#E5484D");
    }

    [Fact]
    public void Clear_WithNothingToStrip_IsANoOp()
    {
        var doc = Doc("plain text");
        doc.Clear(0, 5, s => s.WithoutColor());

        doc.Spans.Should().BeEmpty();
    }

    [Fact]
    public void SetText_ClampsAndDropsSpansBeyondTheEnd()
    {
        var doc = Doc("0123456789");
        doc.Apply(0, 4, new TextStyle { Color = "#E5484D" });
        doc.Apply(8, 2, new TextStyle { Color = "#3E63DD" });

        doc.SetText("012345");
        doc.Spans.Should().ContainSingle();
        doc.Spans[0].Length.Should().Be(4);

        doc.SetText("01");
        doc.Spans.Should().ContainSingle();
        doc.Spans[0].Length.Should().Be(2);
    }

    [Fact]
    public void SetText_TypingBeforeASpan_ShiftsIt()
    {
        var doc = Doc("Hello world");
        doc.Apply(6, 5, new TextStyle { Color = "#E5484D" });

        doc.SetText("Oh, Hello world");

        doc.Spans.Single().Start.Should().Be(10);
        doc.Spans.Single().Length.Should().Be(5);
        doc.Text.Substring(10, 5).Should().Be("world");
    }

    [Fact]
    public void SetText_TypingInsideASpan_GrowsIt()
    {
        var doc = Doc("Hello world");
        doc.Apply(6, 5, new TextStyle { Bold = true });

        doc.SetText("Hello wo-orld");

        doc.Spans.Single().Start.Should().Be(6);
        doc.Spans.Single().Length.Should().Be(7);
    }

    [Fact]
    public void SetText_TypingRightNextToASpan_LeavesTheNewTextUnstyled()
    {
        var doc = Doc("Hello world");
        doc.Apply(0, 5, new TextStyle { Bold = true });

        doc.SetText("Hello, world");

        doc.Spans.Single().Length.Should().Be(5);
    }

    [Fact]
    public void SetText_DeletingAcrossASpan_KeepsWhatRemains()
    {
        var doc = Doc("0123456789");
        doc.Apply(2, 2, new TextStyle { Color = "#E5484D" });
        doc.Apply(6, 3, new TextStyle { Color = "#3E63DD" });

        doc.SetText("01256789"); // "34" removed: half of the first span

        doc.Spans.Should().HaveCount(2);
        doc.Spans[0].Start.Should().Be(2);
        doc.Spans[0].Length.Should().Be(1);
        doc.Spans[1].Start.Should().Be(4);
        doc.Spans[1].Length.Should().Be(3);
    }

    [Fact]
    public void SetText_EmptyText_DropsEverything()
    {
        var doc = Doc("abc");
        doc.Apply(0, 3, new TextStyle { Bold = true });

        doc.SetText("");

        doc.Spans.Should().BeEmpty();
        doc.ToHtml().Should().Be("<div style=\"\"></div>");
    }

    [Fact]
    public void Apply_OutsideTheText_IsIgnored()
    {
        var doc = Doc("abc");
        doc.Apply(5, 2, new TextStyle { Color = "#E5484D" });
        doc.Apply(-3, 2, new TextStyle { Color = "#E5484D" });

        doc.Spans.Should().BeEmpty();
    }

    [Fact]
    public void Apply_ClipsToTheEndOfTheText()
    {
        var doc = Doc("abc");
        doc.Apply(1, 99, new TextStyle { Italic = true });

        doc.Spans.Should().ContainSingle();
        doc.Spans[0].Should().Be(new StyleSpan(1, 2, new TextStyle { Italic = true }));
    }

    [Fact]
    public void HasInRange_DetectsBoldInsideTheSelection()
    {
        var doc = Doc("aaa bbb");
        doc.Apply(4, 3, new TextStyle { Bold = true });

        doc.HasInRange(0, 7, s => s.Bold).Should().BeTrue();
        doc.HasInRange(0, 4, s => s.Bold).Should().BeFalse();
    }

    [Fact]
    public void ToHtml_UsesHighlightAndFontSize()
    {
        var doc = Doc("marked");
        doc.Apply(0, 6, new TextStyle { Highlight = "#FFF3B0", FontSize = 18 });

        doc.ToHtml().Should().Contain("background-color:#FFF3B0;font-size:18px;");
    }
}
