using FluentAssertions;
using Strider.Core.Search;

namespace Strider.Core.Tests;

/// <summary>
/// Search-box syntax (ТЗ F2.8). The parser decides what reaches SQL, so a
/// regression here silently breaks search — hence the detailed coverage.
/// </summary>
public class MessageQueryTests
{
    [Fact]
    public void Parse_EmptyOrWhitespace_IsEmptyQuery()
    {
        MessageQuery.Parse(null).IsEmpty.Should().BeTrue();
        MessageQuery.Parse("").IsEmpty.Should().BeTrue();
        MessageQuery.Parse("   ").IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Parse_BareWords_BecomeFreeText()
    {
        var query = MessageQuery.Parse("quarterly report");

        query.Text.Should().Equal("quarterly", "report");
        query.IsEmpty.Should().BeFalse();
        query.NeedsBody.Should().BeTrue("free text also matches the body");
    }

    [Fact]
    public void Parse_FieldPrefixes_AreCaseInsensitive()
    {
        var query = MessageQuery.Parse("FROM:alice TO:bob Subject:invoice Body:payment");

        query.From.Should().Equal("alice");
        query.To.Should().Equal("bob");
        query.Subject.Should().Equal("invoice");
        query.Body.Should().Equal("payment");
    }

    [Fact]
    public void Parse_QuotedValues_KeepSpaces()
    {
        var query = MessageQuery.Parse("subject:\"quarterly report\" from:bob");

        query.Subject.Should().Equal("quarterly report");
        query.From.Should().Equal("bob");
    }

    [Fact]
    public void Parse_RepeatedField_KeepsAllTerms()
    {
        var query = MessageQuery.Parse("from:alice from:bob subject:hi");

        query.From.Should().Equal("alice", "bob");
        query.Subject.Should().Equal("hi");
    }

    [Theory]
    [InlineData("is:unread", true)]
    [InlineData("is:new", true)]
    [InlineData("is:read", false)]
    public void Parse_IsUnread_Modifier(string input, bool expected)
    {
        MessageQuery.Parse(input).IsUnread.Should().Be(expected);
    }

    [Fact]
    public void Parse_StarredAndAttachment_Modifiers()
    {
        var starred = MessageQuery.Parse("is:starred has:attachment");

        starred.IsStarred.Should().BeTrue();
        starred.HasAttachment.Should().BeTrue();
        starred.NeedsBody.Should().BeFalse("flags do not need the body table");
    }

    [Fact]
    public void Parse_UnknownModifier_IsKeptAsText()
    {
        // Typing "foo:bar" must search for the literal text, never disappear.
        var query = MessageQuery.Parse("priority:high");

        query.Text.Should().Equal("priority:high");
        query.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Parse_UnknownIsValue_IsKeptAsText()
    {
        MessageQuery.Parse("is:weird").Text.Should().Equal("is:weird");
    }

    [Fact]
    public void Parse_Url_StaysOneTokenAndIsNotTreatedAsField()
    {
        var query = MessageQuery.Parse("https://example.com/invoice");

        query.Text.Should().Equal("https://example.com/invoice");
        query.From.Should().BeEmpty();
    }

    [Fact]
    public void ToString_RoundTripsThroughParse()
    {
        var original = MessageQuery.Parse("from:alice subject:\"q3 report\" is:unread has:attachment");

        var reparsed = MessageQuery.Parse(original.ToString());

        reparsed.From.Should().Equal(original.From);
        reparsed.Subject.Should().Equal(original.Subject);
        reparsed.IsUnread.Should().Be(original.IsUnread);
        reparsed.HasAttachment.Should().Be(original.HasAttachment);
    }
}
