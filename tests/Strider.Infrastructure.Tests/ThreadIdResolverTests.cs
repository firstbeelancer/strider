using Strider.Core.Domain;
using Strider.Infrastructure.Mail;
using FluentAssertions;

namespace Strider.Infrastructure.Tests;

/// <summary>
/// Tests for ThreadIdResolver — verifies JWZ-style thread grouping by
/// References and In-Reply-To headers (F-014 fix).
/// </summary>
public class ThreadIdResolverTests
{
    [Fact]
    public void Resolve_MessageWithoutReferences_StartsNewThreadUsingMessageId()
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            MessageId = "<abc@example.com>",
            References = null,
            InReplyTo = null,
        };

        var threadId = ThreadIdResolver.Resolve(message, new Dictionary<string, string>());

        threadId.Should().Be("<abc@example.com>");
    }

    [Fact]
    public void Resolve_MessageWithoutMessageId_UsesSyntheticThreadId()
    {
        var message = new Message
        {
            Id = Guid.Parse("aabbccdd-0000-0000-0000-000000000001"),
            MessageId = null,
            References = null,
            InReplyTo = null,
        };

        var threadId = ThreadIdResolver.Resolve(message, new Dictionary<string, string>());

        threadId.Should().Be("synthetic:aabbccdd-0000-0000-0000-000000000001");
    }

    [Fact]
    public void Resolve_MessageWithKnownReference_JoinsExistingThread()
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            MessageId = "<reply@example.com>",
            References = "<original@example.com>",
            InReplyTo = "<original@example.com>",
        };

        var existing = new Dictionary<string, string>
        {
            ["<original@example.com>"] = "thread-001",
        };

        var threadId = ThreadIdResolver.Resolve(message, existing);

        threadId.Should().Be("thread-001");
    }

    [Fact]
    public void Resolve_MessageWithUnknownReference_StartsNewThread()
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            MessageId = "<reply@example.com>",
            References = "<unknown@example.com>",
            InReplyTo = "<unknown@example.com>",
        };

        var threadId = ThreadIdResolver.Resolve(message, new Dictionary<string, string>());

        // No ancestor known — message starts new thread using its own Message-ID
        threadId.Should().Be("<reply@example.com>");
    }

    [Fact]
    public void Resolve_MultipleReferences_FirstKnownWins()
    {
        var message = new Message
        {
            MessageId = "<c@example.com>",
            References = "<a@example.com> <b@example.com>",
        };

        var existing = new Dictionary<string, string>
        {
            ["<b@example.com>"] = "thread-b",
        };

        var threadId = ThreadIdResolver.Resolve(message, existing);

        threadId.Should().Be("thread-b");
    }

    [Fact]
    public void Resolve_ReferencesJsonFormat_ParsesCorrectly()
    {
        var message = new Message
        {
            MessageId = "<c@example.com>",
            References = """["<a@example.com>","<b@example.com>"]""",
        };

        var existing = new Dictionary<string, string>
        {
            ["<a@example.com>"] = "thread-from-json",
        };

        var threadId = ThreadIdResolver.Resolve(message, existing);

        threadId.Should().Be("thread-from-json");
    }

    [Fact]
    public void RegisterMessage_AddsMessageIdToLookup()
    {
        var message = new Message
        {
            MessageId = "<new@example.com>",
            References = null,
            InReplyTo = null,
        };
        var lookup = new Dictionary<string, string>();

        ThreadIdResolver.RegisterMessage(message, "thread-001", lookup);

        lookup.Should().ContainKey("<new@example.com>");
        lookup["<new@example.com>"].Should().Be("thread-001");
    }

    [Fact]
    public void RegisterMessage_RegistersReferenceIdsForFutureLookups()
    {
        // Out-of-order scenario: this message references <orig@x> but we haven't
        // seen <orig@x> yet. Registering it lets a LATER reply to <orig@x>
        // correctly join this thread.
        var message = new Message
        {
            MessageId = "<reply@example.com>",
            References = "<orig@example.com>",
            InReplyTo = "<orig@example.com>",
        };
        var lookup = new Dictionary<string, string>();

        ThreadIdResolver.RegisterMessage(message, "thread-from-reply", lookup);

        lookup.Should().ContainKey("<orig@example.com>");
        lookup["<orig@example.com>"].Should().Be("thread-from-reply");
    }

    [Fact]
    public void RegisterMessage_DoesNotOverwriteExistingReferenceMapping()
    {
        // If <orig@x> is already mapped to a different thread, don't overwrite.
        var message = new Message
        {
            MessageId = "<reply@example.com>",
            References = "<orig@example.com>",
        };
        var lookup = new Dictionary<string, string>
        {
            ["<orig@example.com>"] = "existing-thread",
        };

        ThreadIdResolver.RegisterMessage(message, "new-thread", lookup);

        lookup["<orig@example.com>"].Should().Be("existing-thread",
            "existing mapping should not be overwritten");
        lookup["<reply@example.com>"].Should().Be("new-thread");
    }

    [Fact]
    public void ParseReferences_NullOrEmpty_ReturnsEmptyList()
    {
        ThreadIdResolver.ParseReferences(null).Should().BeEmpty();
        ThreadIdResolver.ParseReferences("").Should().BeEmpty();
        ThreadIdResolver.ParseReferences("   ").Should().BeEmpty();
    }

    [Fact]
    public void ParseReferences_Rfc5322Format_ParsesAngleBracketedIds()
    {
        var refs = ThreadIdResolver.ParseReferences("<a@x> <b@x> <c@x>");

        refs.Should().HaveCount(3);
        refs.Should().ContainInOrder("<a@x>", "<b@x>", "<c@x>");
    }

    [Fact]
    public void ParseReferences_CommaSeparated_ParsesAll()
    {
        var refs = ThreadIdResolver.ParseReferences("<a@x>, <b@x>, <c@x>");

        refs.Should().HaveCount(3);
    }

    [Fact]
    public void ParseReferences_NormalizesUnbracketedIds()
    {
        // Bare Message-IDs without angle brackets get wrapped to canonical form
        var refs = ThreadIdResolver.ParseReferences("a@x b@x");

        refs.Should().HaveCount(2);
        refs.Should().Contain("<a@x>");
        refs.Should().Contain("<b@x>");
    }

    [Fact]
    public void ParseReferences_JsonArray_ParsesAll()
    {
        var refs = ThreadIdResolver.ParseReferences("""["<a@x>","<b@x>"]""");

        refs.Should().HaveCount(2);
        refs.Should().Contain("<a@x>");
        refs.Should().Contain("<b@x>");
    }

    [Fact]
    public void ParseReferences_Deduplicates()
    {
        var refs = ThreadIdResolver.ParseReferences("<a@x> <a@x> <b@x>");

        refs.Should().HaveCount(2);
    }

    /// <summary>
    /// Integration scenario: simulate a thread arriving out of order.
    /// m3 arrives first (references orig + reply1) → starts the thread.
    /// m5 arrives second (references reply1, which m3 registered) → joins m3's thread.
    /// m0 (orig) arrives third → finds <orig@x> mapped by m3 → joins m3's thread.
    /// </summary>
    [Fact]
    public void Integration_FiveMessageThreadOutOfOrder_AllJoinSameThread()
    {
        var lookup = new Dictionary<string, string>();
        var threadIds = new List<string>();

        // Message 3 arrives first (references orig + reply1)
        var m3 = new Message
        {
            Id = Guid.NewGuid(),
            MessageId = "<msg3@x>",
            References = "<orig@x> <reply1@x>",
            InReplyTo = "<reply1@x>",
        };
        var t3 = ThreadIdResolver.Resolve(m3, lookup);
        ThreadIdResolver.RegisterMessage(m3, t3, lookup);
        threadIds.Add(t3);

        // Message 5 arrives second — references reply1 which m3 registered → joins t3
        var m5 = new Message
        {
            Id = Guid.NewGuid(),
            MessageId = "<msg5@x>",
            References = "<reply1@x>",
            InReplyTo = "<reply1@x>",
        };
        var t5 = ThreadIdResolver.Resolve(m5, lookup);
        ThreadIdResolver.RegisterMessage(m5, t5, lookup);
        threadIds.Add(t5);

        // Original message arrives third — looks up <orig@x> which m3 registered → joins t3
        var m0 = new Message
        {
            Id = Guid.NewGuid(),
            MessageId = "<orig@x>",
            References = null,
            InReplyTo = null,
        };
        var t0 = ThreadIdResolver.Resolve(m0, lookup);
        ThreadIdResolver.RegisterMessage(m0, t0, lookup);
        threadIds.Add(t0);

        // All thread IDs should equal t3 (the thread started by m3)
        threadIds.Should().AllBeEquivalentTo(t3,
            "all 3 messages should join the same thread started by m3 via References");
    }
}
