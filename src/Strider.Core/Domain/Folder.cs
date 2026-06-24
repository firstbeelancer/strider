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
