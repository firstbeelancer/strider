namespace Strider.Core.Domain;

/// <summary>
/// Email attachment.
/// </summary>
public class Attachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public string? Filename { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? ContentId { get; set; } // for inline images
    public string? LocalPath { get; set; } // where downloaded
}
