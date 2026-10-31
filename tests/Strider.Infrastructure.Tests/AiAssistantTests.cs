using FluentAssertions;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Infrastructure.Ai;

namespace Strider.Infrastructure.Tests;

/// <summary>
/// AI features (F5.4–F5.8) against a fake provider, plus the graceful
/// degradation path when the user has not configured anything yet.
/// </summary>
public class AiAssistantTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid FolderId = Guid.NewGuid();

    private static Message Message(int uid, string subject, string from = "alice@example.com", string? threadId = "t-1") => new()
    {
        Id = Guid.NewGuid(),
        AccountId = AccountId,
        FolderId = FolderId,
        MessageUid = uid,
        MessageId = $"<m{uid}@example.com>",
        FromAddress = from,
        FromName = "Alice",
        Subject = subject,
        DateUtc = DateTime.UtcNow.AddMinutes(-uid),
        ThreadId = threadId,
    };

    private static AiAssistant CreateAssistant(
        AiSettings? settings,
        FakeMessageStore? messages = null,
        FakeGatewayFactory? factory = null)
    {
        var store = new FakeAiSettingsStore(settings);
        factory ??= new FakeGatewayFactory();
        return new AiAssistant(store, factory, messages ?? new FakeMessageStore());
    }

    [Fact]
    public async Task IsConfigured_IsFalse_WhenNoSettings()
    {
        var assistant = CreateAssistant(null);

        (await assistant.IsConfiguredAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task IsConfigured_IsFalse_WhenModelMissing()
    {
        var assistant = CreateAssistant(new AiSettings { Provider = "openai", Model = "" });

        (await assistant.IsConfiguredAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Summarize_WithoutProvider_ThrowsAiNotConfigured()
    {
        var assistant = CreateAssistant(null);

        var act = async () => await assistant.SummarizeThreadAsync(new[] { Message(1, "Hi") });

        await act.Should().ThrowAsync<AiNotConfiguredException>()
            .WithMessage("*Settings*AI*");
    }

    [Fact]
    public async Task Summarize_IncludesThreadBodies_InPrompt()
    {
        var first = Message(1, "Kickoff");
        var second = Message(2, "Re: Kickoff");
        var messages = new FakeMessageStore()
            .WithBody(first.Id, "We agreed to ship on Friday.")
            .WithBody(second.Id, "Friday works, I will prepare the demo.");
        var factory = new FakeGatewayFactory();
        var assistant = CreateAssistant(
            new AiSettings { Provider = "openai", Model = "gpt-4o-mini" }, messages, factory);

        var result = await assistant.SummarizeThreadAsync(new[] { second, first });

        var prompt = factory.LastGateway.LastPrompt;
        prompt.Should().Contain("We agreed to ship on Friday.");
        prompt.Should().Contain("Friday works, I will prepare the demo.");
        prompt.Should().Contain("Summarize", "the summary template must be used");
        result.Text.Should().Be("fake completion");
    }

    [Fact]
    public async Task Summarize_ReportsTokensAndCost()
    {
        var factory = new FakeGatewayFactory(inputTokens: 1200, outputTokens: 300);
        var assistant = CreateAssistant(
            new AiSettings { Provider = "openai", Model = "gpt-4o-mini" }, factory: factory);

        var result = await assistant.SummarizeThreadAsync(new[] { Message(1, "Hi") });

        result.InputTokens.Should().Be(1200);
        result.OutputTokens.Should().Be(300);
        result.TotalTokens.Should().Be(1500);
        result.EstimatedCostUsd.Should().BeGreaterThan(0);
        result.UsageLabel.Should().Contain("1500 tok");
    }

    [Fact]
    public async Task Summarize_TruncatesVeryLongThreads()
    {
        var longMessage = Message(1, "Long one");
        var messages = new FakeMessageStore()
            .WithBody(longMessage.Id, new string('x', AiAssistant.MaxPromptChars * 2));
        var factory = new FakeGatewayFactory();
        var assistant = CreateAssistant(
            new AiSettings { Provider = "openai", Model = "gpt-4o-mini" }, messages, factory);

        await assistant.SummarizeThreadAsync(new[] { longMessage });

        factory.LastGateway.LastPrompt!.Length.Should()
            .BeLessThan(AiAssistant.MaxPromptChars + 1000, "prompts are capped");
    }

    [Fact]
    public async Task DraftReply_UsesOriginalMessageText()
    {
        var original = Message(1, "Invoice");
        var messages = new FakeMessageStore().WithBody(original.Id, "Please send the invoice by Monday.");
        var factory = new FakeGatewayFactory();
        var assistant = CreateAssistant(
            new AiSettings { Provider = "openai", Model = "gpt-4o-mini" }, messages, factory);

        var result = await assistant.DraftReplyAsync(original);

        factory.LastGateway.LastPrompt.Should().Contain("Please send the invoice by Monday.");
        result.Text.Should().Be("fake completion");
    }

    [Theory]
    [InlineData("Work", "Work")]
    [InlineData("  **Work** ", "Work")]
    [InlineData("Category: Action Required", "Action Required")]
    [InlineData("newsletter", "Newsletter")]
    public void NormalizeCategory_KeepsOnlyKnownLabel(string raw, string expected)
    {
        AiAssistant.NormalizeCategory(raw).Should().Be(expected);
    }

    [Fact]
    public async Task TestConnection_WithoutConfig_FailsGracefully()
    {
        var assistant = CreateAssistant(null);

        var result = await assistant.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("No AI provider");
    }

    [Fact]
    public async Task TestConnection_WithConfig_Succeeds()
    {
        var assistant = CreateAssistant(new AiSettings { Provider = "openai", Model = "gpt-4o-mini" });

        var result = await assistant.TestConnectionAsync();

        result.Success.Should().BeTrue();
        result.Model.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public async Task TestConnection_WhenProviderThrows_ReportsMessage()
    {
        var factory = new FakeGatewayFactory { ThrowOnChat = new HttpRequestException("401 unauthorized") };
        var assistant = CreateAssistant(
            new AiSettings { Provider = "openai", Model = "gpt-4o-mini" }, factory: factory);

        var result = await assistant.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("401");
    }

    // === Fakes ===

    private sealed class FakeAiSettingsStore : IAiSettingsStore
    {
        private AiSettings? _settings;

        public FakeAiSettingsStore(AiSettings? settings) => _settings = settings;

        public Task<IReadOnlyList<AiSettings>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AiSettings>>(_settings is null ? Array.Empty<AiSettings>() : new[] { _settings });

        public Task<AiSettings?> GetDefaultAsync(CancellationToken ct = default) => Task.FromResult(_settings);

        public Task SaveAsync(AiSettings settings, CancellationToken ct = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _settings = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGatewayFactory : IAiGatewayFactory
    {
        public FakeGateway LastGateway { get; private set; } = new();
        public Exception? ThrowOnChat { get; init; }
        private readonly int _inputTokens;
        private readonly int _outputTokens;

        public FakeGatewayFactory(int inputTokens = 10, int outputTokens = 5)
        {
            _inputTokens = inputTokens;
            _outputTokens = outputTokens;
        }

        public IAiGateway Create(string provider, string? apiKeyRef, string? baseUrl = null)
        {
            LastGateway = new FakeGateway(ThrowOnChat, _inputTokens, _outputTokens);
            return LastGateway;
        }
    }

    private sealed class FakeGateway : IAiGateway
    {
        private readonly Exception? _throw;
        private readonly int _inputTokens;
        private readonly int _outputTokens;

        public string? LastPrompt { get; private set; }

        public FakeGateway(Exception? toThrow = null, int inputTokens = 10, int outputTokens = 5)
        {
            _throw = toThrow;
            _inputTokens = inputTokens;
            _outputTokens = outputTokens;
        }

        public Task<AiResponse> ChatAsync(
            IEnumerable<AiMessage> messages, AiRequestOptions options, CancellationToken ct = default)
        {
            if (_throw is not null) throw _throw;

            LastPrompt = string.Join("\n", messages.Select(m => m.Content));
            return Task.FromResult(new AiResponse
            {
                Content = "fake completion",
                Usage = new AiUsage { InputTokens = _inputTokens, OutputTokens = _outputTokens },
            });
        }

        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AiModel>>(Array.Empty<AiModel>());

        public Task<AiUsage> EstimateCostAsync(
            IEnumerable<AiMessage> messages, string model, CancellationToken ct = default) =>
            Task.FromResult(new AiUsage());
    }

    private sealed class FakeMessageStore : IMessageStore
    {
        public Task<IReadOnlyList<LocalMessageState>> GetLocalStatesAsync(Guid f, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ApplyRemoteChangesAsync(IReadOnlyCollection<Guid> r, IReadOnlyCollection<(Guid Id, bool IsRead, bool IsStarred)> c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteFolderMessagesAsync(Guid f, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, string>> GetThreadLookupAsync(Guid a, IReadOnlyCollection<string> ids, CancellationToken ct = default) => throw new NotSupportedException();
        private readonly Dictionary<Guid, MessageBody> _bodies = new();

        public FakeMessageStore WithBody(Guid messageId, string text)
        {
            _bodies[messageId] = new MessageBody { MessageId = messageId, TextPlain = text };
            return this;
        }

        public Task<MessageBody?> GetBodyAsync(Guid messageId, CancellationToken ct = default) =>
            Task.FromResult(_bodies.TryGetValue(messageId, out var body) ? body : null);

        public Task SaveMessageAsync(Message message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveMessagesAsync(IEnumerable<Message> messages, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Message?> GetMessageAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Message?>(null);
        public Task<Message?> GetMessageByUidAsync(Guid accountId, Guid folderId, int uid, CancellationToken ct = default) =>
            Task.FromResult<Message?>(null);
        public Task<IReadOnlyList<Message>> GetMessagesAsync(Guid folderId, int limit = 100, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Message>>(Array.Empty<Message>());
        public Task<IReadOnlyList<Message>> GetThreadMessagesAsync(string threadId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Message>>(Array.Empty<Message>());
        public Task UpdateMessageAsync(Message message, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteMessageAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> GetMessageCountAsync(Guid folderId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> GetUnreadCountAsync(Guid folderId, CancellationToken ct = default) => Task.FromResult(0);
        public Task SaveBodyAsync(MessageBody body, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveAttachmentAsync(Attachment attachment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(Guid messageId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Attachment>>(Array.Empty<Attachment>());
        public Task<Attachment?> GetAttachmentAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Attachment?>(null);
        public Task<IReadOnlyList<Message>> SearchAsync(Guid accountId, string query, int limit = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Message>>(Array.Empty<Message>());
        public Task<IReadOnlyList<Message>> SearchMessagesAsync(Guid accountId, Guid? folderId, Strider.Core.Search.MessageQuery query, int limit = 200, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Message>>(Array.Empty<Message>());
        public Task<IReadOnlyList<EmailThread>> GetThreadsAsync(Guid folderId, int limit = 50, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmailThread>>(Array.Empty<EmailThread>());
    }
}
