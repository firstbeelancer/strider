using FluentAssertions;
using Strider.Core.Domain;

namespace Strider.Core.Tests;

public class AddressListTests
{
    [Fact]
    public void ParseInput_SplitsOnCommaAndSemicolon_TrimsAndDropsEmpty()
    {
        AddressList.ParseInput(" a@x.com, b@y.com ;; c@z.com ,")
            .Should().Equal("a@x.com", "b@y.com", "c@z.com");
    }

    [Fact]
    public void ParseInput_KeepsCommaInsideQuotedName()
    {
        AddressList.ParseInput("\"Doe, John\" <j@x.com>, b@y.com")
            .Should().Equal("\"Doe, John\" <j@x.com>", "b@y.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    public void FromJson_EmptyInputs_ReturnEmpty(string? json)
    {
        AddressList.FromJson(json).Should().BeEmpty();
    }

    [Fact]
    public void FromJson_ReadsJsonArray_AndToleratesPlainText()
    {
        AddressList.FromJson("[\"a@x.com\",\"Bob <b@y.com>\"]").Should().Equal("a@x.com", "Bob <b@y.com>");
        AddressList.FromJson("a@x.com, b@y.com").Should().Equal("a@x.com", "b@y.com");
    }

    [Theory]
    [InlineData("Bob <Bob@Y.com>", "bob@y.com")]
    [InlineData("a@x.com", "a@x.com")]
    [InlineData("\"Doe, John\" <J@X.com>", "j@x.com")]
    public void BareAddress_ExtractsLowercasedEmail(string input, string expected)
    {
        AddressList.BareAddress(input).Should().Be(expected);
    }
}

public class ReplyBuilderTests
{
    private static Message Original() => new()
    {
        AccountId = Guid.NewGuid(),
        MessageId = "orig@x.com",
        References = "[\"root@x.com\",\"mid@x.com\"]",
        FromAddress = "alice@x.com",
        FromName = "Alice",
        ToAddresses = "[\"me@mine.com\",\"Bob <bob@y.com>\"]",
        CcAddresses = "[\"carol@z.com\",\"ME@mine.com\"]",
        Subject = "Lunch",
        DateUtc = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc),
    };

    [Theory]
    [InlineData("Lunch", "Re: Lunch")]
    [InlineData("Re: Lunch", "Re: Lunch")]
    [InlineData("RE: Lunch", "RE: Lunch")]
    [InlineData("Ответ: Обед", "Ответ: Обед")]
    [InlineData(null, "Re: ")]
    public void ReplySubject_AddsPrefixOnce(string? subject, string expected)
    {
        ReplyBuilder.ReplySubject(subject).Should().Be(expected);
    }

    [Theory]
    [InlineData("Lunch", "Fwd: Lunch")]
    [InlineData("FW: Lunch", "FW: Lunch")]
    public void ForwardSubject_AddsPrefixOnce(string subject, string expected)
    {
        ReplyBuilder.ForwardSubject(subject).Should().Be(expected);
    }

    [Fact]
    public void ReplyTo_IsOriginalSender()
    {
        ReplyBuilder.ReplyTo(Original()).Should().Equal("\"Alice\" <alice@x.com>");
    }

    [Fact]
    public void ReplyAll_IncludesEveryoneButSelf_NoDuplicates()
    {
        var (to, cc) = ReplyBuilder.ReplyAll(Original(), "me@mine.com");

        to.Should().Equal("\"Alice\" <alice@x.com>", "Bob <bob@y.com>");
        cc.Should().Equal("carol@z.com");
    }

    [Fact]
    public void ReplyAll_ToOwnSentMessage_KeepsRecipients()
    {
        var sent = new Message
        {
            FromAddress = "me@mine.com",
            ToAddresses = "[]",
            CcAddresses = "[\"x@y.com\"]",
        };
        var (to, cc) = ReplyBuilder.ReplyAll(sent, "me@mine.com");
        to.Should().Equal("x@y.com");
        cc.Should().BeEmpty();
    }

    [Fact]
    public void References_AppendsOriginalMessageId()
    {
        ReplyBuilder.References(Original()).Should().Equal("root@x.com", "mid@x.com", "orig@x.com");
    }

    [Fact]
    public void References_StripsAngleBrackets_AndDoesNotDuplicate()
    {
        var m = new Message { MessageId = "<a@x>", References = "[\"<a@x>\"]" };
        ReplyBuilder.References(m).Should().Equal("a@x");
    }

    [Fact]
    public void QuotePlain_PrefixesEveryLine()
    {
        ReplyBuilder.QuotePlain("one\r\ntwo").Should().Be("> one\n> two");
    }
}
