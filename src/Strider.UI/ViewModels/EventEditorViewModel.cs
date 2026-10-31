using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Domain;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the calendar event dialog (F6.11).
/// Edits a copy of the event; the caller saves only when the dialog is accepted.
/// </summary>
public partial class EventEditorViewModel : ObservableObject
{
    private readonly CalendarEvent _original;

    public event Action<CalendarEvent?>? CloseRequested;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _location = "";

    [ObservableProperty]
    private DateTimeOffset? _startDate;

    [ObservableProperty]
    private TimeSpan? _startTime;

    [ObservableProperty]
    private DateTimeOffset? _endDate;

    [ObservableProperty]
    private TimeSpan? _endTime;

    [ObservableProperty]
    private bool _allDay;

    [ObservableProperty]
    private string _color = "#5B6CFF";

    [ObservableProperty]
    private int? _reminderMinutes;

    [ObservableProperty]
    private string _statusMessage = "";

    public string WindowTitle { get; }

    public static IReadOnlyList<string> Colors { get; } =
        new[] { "#5B6CFF", "#22C55E", "#F59E0B", "#EF4444", "#8B5CF6", "#06B6D4" };

    public EventEditorViewModel() : this(new CalendarEvent())
    {
    }

    public EventEditorViewModel(CalendarEvent draft)
    {
        _original = draft ?? new CalendarEvent();

        var start = _original.StartUtc == default ? DateTime.Now : _original.StartUtc.ToLocalTime();
        var end = _original.EndUtc == default || _original.EndUtc <= _original.StartUtc
            ? start.AddHours(1)
            : _original.EndUtc.ToLocalTime();

        WindowTitle = string.IsNullOrWhiteSpace(_original.Title) && _original.StartUtc == default
            ? "New event"
            : "Edit event";

        Title = _original.Title;
        Description = _original.Description ?? "";
        Location = _original.Location ?? "";
        StartDate = LocalMidnight(start);
        StartTime = start.TimeOfDay;
        EndDate = LocalMidnight(end);
        EndTime = end.TimeOfDay;
        AllDay = _original.AllDay;
        Color = string.IsNullOrWhiteSpace(_original.Color) ? "#5B6CFF" : _original.Color;
        ReminderMinutes = _original.ReminderMinutes;
        StatusMessage = "";
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            StatusMessage = "Give the event a title.";
            return;
        }

        var startLocal = Combine(StartDate, StartTime ?? TimeSpan.Zero);
        var endLocal = Combine(EndDate, EndTime ?? TimeSpan.FromHours(1));

        if (AllDay)
        {
            startLocal = startLocal.Date;
            endLocal = endLocal.Date.AddDays(1);
        }

        if (endLocal <= startLocal)
        {
            StatusMessage = "The end must be after the start.";
            return;
        }

        var result = new CalendarEvent
        {
            Id = _original.Id,
            AccountId = _original.AccountId,
            Title = Title.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
            Location = string.IsNullOrWhiteSpace(Location) ? null : Location,
            StartUtc = startLocal.ToUniversalTime(),
            EndUtc = endLocal.ToUniversalTime(),
            AllDay = AllDay,
            Color = Color,
            ReminderMinutes = ReminderMinutes,
            RecurrenceRule = _original.RecurrenceRule,
            CaldavUid = _original.CaldavUid,
            CreatedAt = _original.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
        };

        CloseRequested?.Invoke(result);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    /// <summary>
    /// The date picker's value for a local day. <c>start.Date</c> has Kind=Local, and
    /// new DateTimeOffset(local, TimeSpan.Zero) throws anywhere but UTC, so the dialog
    /// could not open at all on a machine outside UTC.
    /// </summary>
    private static DateTimeOffset LocalMidnight(DateTime local) =>
        new(local.Date, TimeZoneInfo.Local.GetUtcOffset(local.Date));

    private static DateTime Combine(DateTimeOffset? date, TimeSpan time)
    {
        var day = (date?.LocalDateTime ?? DateTime.Now).Date;
        return day.Add(time);
    }
}
