namespace Strider.Core.Domain;

/// <summary>
/// Email message body (HTML and plain text).
/// </summary>
public class MessageBody
{
    public Guid MessageId { get; set; }
    public string? TextPlain { get; set; }
    public string? TextHtml { get; set; } // sanitized
    public string? RawMimePath { get; set; } // path to raw binary file
}
