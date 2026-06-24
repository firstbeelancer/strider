using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// Calendar event store.
/// </summary>
public interface ICalendarStore
{
    Task SaveEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default);
    Task<CalendarEvent?> GetEventAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task UpdateEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default);
    Task DeleteEventAsync(Guid id, CancellationToken ct = default);
}
