namespace Strider.Core.Domain;

/// <summary>
/// Email thread (group of related messages).
/// </summary>
public class EmailThread
{
    public string ThreadId { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public DateTime LastDateUtc { get; set; }
    public string LastFromAddress { get; set; } = string.Empty;
    public string? LastFromName { get; set; }
    public string? LastSnippet { get; set; }
    public bool HasUnread { get; set; }
    public bool HasAttachments { get; set; }
    public bool IsStarred { get; set; }

    // AI
    public string? AiCategory { get; set; }
    public string? AiSummary { get; set; }
}
