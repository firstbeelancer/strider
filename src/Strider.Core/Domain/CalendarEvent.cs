namespace Strider.Core.Domain;

/// <summary>
/// Calendar event (local storage).
/// </summary>
public class CalendarEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? AccountId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool AllDay { get; set; }
    public string? Color { get; set; } // hex color
    public int? ReminderMinutes { get; set; }
    public string? RecurrenceRule { get; set; } // iCal RRULE
    public string? CaldavUid { get; set; } // for future CalDAV sync

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
