namespace Strider.Core.Domain;

/// <summary>
/// Email folder (inbox, sent, drafts, trash, custom).
/// </summary>
public class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string RemoteName { get; set; } = string.Empty;
    public FolderType Type { get; set; } = FolderType.Custom;
    public Guid? ParentId { get; set; }
    public int LastSyncUid { get; set; }
    public int UnreadCount { get; set; }

    /// <summary>IMAP UIDVALIDITY at the last sync; a change means every cached UID is void.</summary>
    public long UidValidity { get; set; }
}

/// <summary>What the server currently knows about one message of a folder.</summary>
public sealed record RemoteMessageState(int Uid, bool IsRead, bool IsStarred);

/// <summary>What the local cache holds for one message of a folder.</summary>
public sealed record LocalMessageState(Guid Id, int Uid, bool IsRead, bool IsStarred);

/// <summary>Server view of a folder, used to reconcile the cache with other devices.</summary>
public sealed class FolderSnapshot
{
    public long UidValidity { get; init; }
    public IReadOnlyList<RemoteMessageState> Messages { get; init; } = Array.Empty<RemoteMessageState>();
}

public enum FolderType
{
    Inbox,
    Sent,
    Drafts,
    Trash,
    Archive,
    Spam,
    Custom
}
