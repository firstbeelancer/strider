using Dapper;
using Microsoft.Data.Sqlite;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation of calendar event storage.
/// </summary>
public class SqliteCalendarStore : ICalendarStore
{
    private readonly string _connectionString;

    public SqliteCalendarStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task SaveEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        const string sql = """
            INSERT INTO calendar_events (id, account_id, title, description, location,
                start_utc, end_utc, all_day, color, reminder_minutes, recurrence_rule,
                caldav_uid, created_at, updated_at)
            VALUES (@Id, @AccountId, @Title, @Description, @Location,
                @StartUtc, @EndUtc, @AllDay, @Color, @ReminderMinutes, @RecurrenceRule,
                @CaldavUid, @CreatedAt, @UpdatedAt)
            ON CONFLICT(id) DO UPDATE SET
                title=@Title, description=@Description, location=@Location,
                start_utc=@StartUtc, end_utc=@EndUtc, all_day=@AllDay,
                color=@Color, reminder_minutes=@ReminderMinutes,
                recurrence_rule=@RecurrenceRule, caldav_uid=@CaldavUid, updated_at=@UpdatedAt
            """;

        await db.ExecuteAsync(new CommandDefinition(sql, MapToParams(calendarEvent), cancellationToken: ct));
    }

    public async Task<CalendarEvent?> GetEventAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        const string sql = "SELECT * FROM calendar_events WHERE id = @Id";
        var row = await db.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id.ToString() }, cancellationToken: ct));

        return row is null ? null : MapEvent(row);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        const string sql = """
            SELECT * FROM calendar_events 
            WHERE start_utc >= @FromUtc AND start_utc < @ToUtc
            ORDER BY start_utc
            """;

        var rows = await db.QueryAsync(new CommandDefinition(sql, new
        {
            FromUtc = new DateTimeOffset(fromUtc).ToUnixTimeSeconds(),
            ToUtc = new DateTimeOffset(toUtc).ToUnixTimeSeconds(),
        }, cancellationToken: ct));

        return rows.Select(MapEvent).ToList();
    }

    public async Task UpdateEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        calendarEvent.UpdatedAt = DateTime.UtcNow;
        await SaveEventAsync(calendarEvent, ct);
    }

    public async Task DeleteEventAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        const string sql = "DELETE FROM calendar_events WHERE id = @Id";
        await db.ExecuteAsync(new CommandDefinition(sql, new { Id = id.ToString() }, cancellationToken: ct));
    }

    private static object MapToParams(CalendarEvent e) => new
    {
        Id = e.Id.ToString(),
        AccountId = e.AccountId?.ToString(),
        e.Title,
        e.Description,
        e.Location,
        StartUtc = new DateTimeOffset(e.StartUtc).ToUnixTimeSeconds(),
        EndUtc = new DateTimeOffset(e.EndUtc).ToUnixTimeSeconds(),
        AllDay = e.AllDay ? 1 : 0,
        e.Color,
        e.ReminderMinutes,
        e.RecurrenceRule,
        e.CaldavUid,
        CreatedAt = new DateTimeOffset(e.CreatedAt).ToUnixTimeSeconds(),
        UpdatedAt = new DateTimeOffset(e.UpdatedAt).ToUnixTimeSeconds(),
    };

    private static CalendarEvent MapEvent(dynamic row) => new()
    {
        Id = Guid.Parse((string)row.id),
        AccountId = row.account_id is null ? null : Guid.Parse((string)row.account_id),
        Title = (string)row.title,
        Description = (string?)row.description,
        Location = (string?)row.location,
        StartUtc = DateTimeOffset.FromUnixTimeSeconds((long)row.start_utc).UtcDateTime,
        EndUtc = DateTimeOffset.FromUnixTimeSeconds((long)row.end_utc).UtcDateTime,
        AllDay = (long)row.all_day == 1,
        Color = (string?)row.color,
        ReminderMinutes = row.reminder_minutes is null ? null : (int)(long)row.reminder_minutes,
        RecurrenceRule = (string?)row.recurrence_rule,
        CaldavUid = (string?)row.caldav_uid,
        CreatedAt = DateTimeOffset.FromUnixTimeSeconds((long)row.created_at).UtcDateTime,
        UpdatedAt = DateTimeOffset.FromUnixTimeSeconds((long)row.updated_at).UtcDateTime,
    };
}
