using Strider.Core.Abstractions;
using Strider.Infrastructure.Editor;
using FluentAssertions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Strider.Infrastructure.Tests;

/// <summary>
/// Tests for EditorBridge — verifies JSON message protocol between C# and JS.
/// Uses a mock SendMessage that captures outgoing messages and lets the test
/// simulate incoming JS responses/events.
///
/// Closes finding F-010: WebView editor infrastructure. The bridge logic is
/// tested here without a real WebView; platform-specific WebView2/CEF tests
/// would require a live UI thread and are out of scope for unit tests.
/// </summary>
public class EditorBridgeTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    [Fact]
    public async Task SendRequestAsync_SendsJsonRequestWithIncrementingId()
    {
        var bridge = new EditorBridge();
        var sentMessages = new List<string>();
        bridge.SendMessage = json => sentMessages.Add(json);

        var task1 = bridge.SendRequestAsync("bold");
        var task2 = bridge.SendRequestAsync("italic");

        sentMessages.Should().HaveCount(2);
        var msg1 = EditorMessage.Deserialize(sentMessages[0])!;
        var msg2 = EditorMessage.Deserialize(sentMessages[1])!;

        msg1.Id.Should().Be(1);
        msg2.Id.Should().Be(2);
        msg1.Type.Should().Be("request");
        msg1.Command.Should().Be("bold");
        msg2.Command.Should().Be("italic");

        bridge.Dispose();
        await Task.WhenAll(
            task1.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled),
            task2.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled));
    }

    [Fact]
    public async Task SendRequestAsync_WithoutSendMessage_Throws()
    {
        var bridge = new EditorBridge();
        var act = async () => await bridge.SendRequestAsync("bold");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task HandleIncomingMessage_ResponseResolvesPendingRequest()
    {
        var bridge = new EditorBridge();
        var sentMessages = new List<string>();
        bridge.SendMessage = json => sentMessages.Add(json);

        var task = bridge.SendRequestAsync("getHtml");
        var sentMsg = EditorMessage.Deserialize(sentMessages[0])!;
        var id = sentMsg.Id!.Value;

        var response = new EditorMessage
        {
            Id = id,
            Type = "response",
            Result = JsonSerializer.SerializeToElement("<p>hello</p>", JsonOpts),
        };
        bridge.HandleIncomingMessage(EditorMessage.Serialize(response));

        var result = await task;
        result?.Deserialize<string>().Should().Be("<p>hello</p>");
    }

    [Fact]
    public async Task HandleIncomingMessage_ResponseWithError_RejectsRequest()
    {
        var bridge = new EditorBridge();
        var sentMessages = new List<string>();
        bridge.SendMessage = json => sentMessages.Add(json);

        var task = bridge.SendRequestAsync("unknownCommand");
        var sentMsg = EditorMessage.Deserialize(sentMessages[0])!;
        var id = sentMsg.Id!.Value;

        var response = new EditorMessage
        {
            Id = id,
            Type = "response",
            Error = "Unknown command: unknownCommand",
        };
        bridge.HandleIncomingMessage(EditorMessage.Serialize(response));

        var act = async () => await task;
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unknown command*");
    }

    [Fact]
    public void HandleIncomingMessage_SelectionChangedEvent_DispatchesToSubscribers()
    {
        var bridge = new EditorBridge();
        var receivedSelections = new List<EditorSelection>();
        bridge.SubscribeToSelectionChanges(s => receivedSelections.Add(s));
        bridge.SendMessage = _ => { };

        var selection = new EditorSelection { IsBold = true, BlockType = "h1" };
        var eventMsg = new EditorMessage
        {
            Type = "event",
            Event = "selectionChanged",
            Data = JsonSerializer.SerializeToElement(selection, JsonOpts),
        };
        bridge.HandleIncomingMessage(EditorMessage.Serialize(eventMsg));

        receivedSelections.Should().HaveCount(1);
        receivedSelections[0].IsBold.Should().BeTrue();
        receivedSelections[0].BlockType.Should().Be("h1");
    }

    [Fact]
    public void HandleIncomingMessage_ContentChangedEvent_DispatchesToSubscribers()
    {
        var bridge = new EditorBridge();
        var receivedHtml = new List<string>();
        bridge.SubscribeToContentChanges(html => receivedHtml.Add(html));
        bridge.SendMessage = _ => { };

        var eventMsg = new EditorMessage
        {
            Type = "event",
            Event = "contentChanged",
            Data = JsonSerializer.SerializeToElement("<p>new content</p>", JsonOpts),
        };
        bridge.HandleIncomingMessage(EditorMessage.Serialize(eventMsg));

        receivedHtml.Should().ContainSingle().Which.Should().Be("<p>new content</p>");
    }

    [Fact]
    public void SubscribeToSelectionChanges_UnsubscribeStopsEvents()
    {
        var bridge = new EditorBridge();
        var count = 0;
        var subscription = bridge.SubscribeToSelectionChanges(_ => count++);
        bridge.SendMessage = _ => { };

        var eventMsg = new EditorMessage
        {
            Type = "event",
            Event = "selectionChanged",
            Data = JsonSerializer.SerializeToElement(new EditorSelection(), JsonOpts),
        };
        bridge.HandleIncomingMessage(EditorMessage.Serialize(eventMsg));
        count.Should().Be(1);

        subscription.Dispose();

        bridge.HandleIncomingMessage(EditorMessage.Serialize(eventMsg));
        count.Should().Be(1, "no further events after unsubscribe");
    }

    [Fact]
    public void HandleIncomingMessage_NullOrInvalidJson_DoesNotThrow()
    {
        var bridge = new EditorBridge();
        var act = () =>
        {
            bridge.HandleIncomingMessage(null!);
            bridge.HandleIncomingMessage("");
            bridge.HandleIncomingMessage("not json");
            bridge.HandleIncomingMessage("{}");
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CancelsPendingRequests()
    {
        var bridge = new EditorBridge();
        bridge.SendMessage = _ => { };
        var task = bridge.SendRequestAsync("bold");

        bridge.Dispose();

        task.IsCanceled.Should().BeTrue();
    }

    [Fact]
    public void Dispose_ClearsAllSubscribers()
    {
        var bridge = new EditorBridge();
        var count = 0;
        bridge.SubscribeToSelectionChanges(_ => count++);
        bridge.SendMessage = _ => { };

        bridge.Dispose();

        var eventMsg = new EditorMessage
        {
            Type = "event",
            Event = "selectionChanged",
            Data = JsonSerializer.SerializeToElement(new EditorSelection(), JsonOpts),
        };
        bridge.HandleIncomingMessage(EditorMessage.Serialize(eventMsg));
        count.Should().Be(0, "subscribers cleared on dispose");
    }

    [Fact]
    public void EditorMessage_Serialize_RoundtripsCorrectly()
    {
        var original = new EditorMessage
        {
            Id = 42,
            Type = "request",
            Command = "setHtml",
            Parameter = JsonSerializer.SerializeToElement("<p>test</p>", JsonOpts),
        };

        var json = EditorMessage.Serialize(original);
        var restored = EditorMessage.Deserialize(json)!;

        restored.Id.Should().Be(42);
        restored.Type.Should().Be("request");
        restored.Command.Should().Be("setHtml");
        restored.Parameter?.Deserialize<string>().Should().Be("<p>test</p>");
    }
}
