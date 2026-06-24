namespace Strider.Core.Domain;

/// <summary>
/// Email signature (multiple per account).
/// </summary>
public class Signature
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty; // e.g., "Work", "Personal"
    public string? ContentHtml { get; set; }
    public string? ContentPlain { get; set; }
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
