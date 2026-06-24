namespace Strider.Core.Domain;

/// <summary>
/// Pending operation for offline queue.
/// </summary>
public class PendingOp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public PendingOpType OpType { get; set; }
    public string Payload { get; set; } = "{}"; // JSON
    public PendingOpStatus Status { get; set; } = PendingOpStatus.Pending;
    public int RetryCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum PendingOpType
{
    Send,
    Delete,
    Move,
    Flag,
    MarkRead
}

public enum PendingOpStatus
{
    Pending,
    Sent,
    Failed
}
