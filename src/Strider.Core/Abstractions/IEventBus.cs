namespace Strider.Core.Abstractions;

/// <summary>
/// In-process event bus for UI updates.
/// </summary>
public interface IEventBus
{
    void Publish<T>(T @event) where T : class;
    IDisposable Subscribe<T>(Action<T> handler) where T : class;
}

// Events
public record NewMessageEvent(Guid AccountId, Guid FolderId, Guid MessageId);
public record MessageUpdatedEvent(Guid MessageId);
public record MessageDeletedEvent(Guid MessageId);
public record FolderUpdatedEvent(Guid FolderId);
public record SyncCompletedEvent(Guid AccountId, string FolderName);
public record SyncErrorEvent(Guid AccountId, string Error);
public record AiClassificationEvent(Guid MessageId, string Category);
