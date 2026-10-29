using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the calendar view.
///
/// Wave 5: month grid fixed (the old Monday-start offset put Sundays in the
/// wrong column) and event creation/editing is real — the view supplies an
/// editor through <see cref="EditEventInteraction"/>, so the ViewModel stays
/// testable without opening a window.
/// </summary>
public partial class CalendarViewModel : ObservableObject
{
    private readonly ICalendarStore? _calendarStore;

    [ObservableProperty]
    private ObservableCollection<CalendarEvent> _events = new();

    [ObservableProperty]
    private CalendarEvent? _selectedEvent;

    [ObservableProperty]
    private DateTime _currentMonth = DateTime.UtcNow;

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.UtcNow;

    [ObservableProperty]
    private ObservableCollection<CalendarDay> _days = new();

    /// <summary>Events of <see cref="SelectedDate"/>, shown under the grid for editing.</summary>
    [ObservableProperty]
    private ObservableCollection<CalendarEvent> _selectedDateEvents = new();

    partial void OnSelectedDateChanged(DateTime value) => RefreshSelectedDateEvents();

    private void RefreshSelectedDateEvents() =>
        SelectedDateEvents = new ObservableCollection<CalendarEvent>(
            Events.Where(e => e.StartUtc.Date == SelectedDate.Date).OrderBy(e => e.StartUtc));

    [ObservableProperty]
    private string _viewMode = "month"; // month, week, day

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>
    /// Set by <c>CalendarWindow</c>: opens the event editor for the given event
    /// (or a new one when null) and returns the edited event, or null on cancel.
    /// </summary>
    public Func<CalendarEvent, Task<CalendarEvent?>>? EditEventInteraction { get; set; }

    /// <summary>
    /// ICalendarStore is optional so the calendar can open before the database is
    /// ready (it then shows an empty month instead of crashing the app).
    /// </summary>
    public CalendarViewModel(ICalendarStore? calendarStore = null)
    {
        _calendarStore = calendarStore;
    }

    [RelayCommand]
    public async Task LoadMonthAsync()
    {
        IsLoading = true;

        try
        {
            var firstDay = new DateTime(CurrentMonth.Year, CurrentMonth.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            var events = new List<CalendarEvent>();
            if (_calendarStore != null)
            {
                // Pad the range by a week on each side: the grid shows days of
                // the neighbouring months and they carry events too.
                events = (await _calendarStore.GetEventsAsync(firstDay.AddDays(-7), lastDay.AddDays(8))).ToList();
            }

            Events = new ObservableCollection<CalendarEvent>(events);

            // Monday-first grid. DayOfWeek is Sunday=0, so Sunday must land in
            // column 6: ((int)DayOfWeek + 6) % 7. The old formula (-(int)DayOfWeek + 1)
            // shifted the whole grid by one day for every month starting on a Sunday.
            var offset = ((int)firstDay.DayOfWeek + 6) % 7;
            var startDate = firstDay.AddDays(-offset);

            var days = new List<CalendarDay>();
            for (var i = 0; i < 42; i++) // 6 weeks
            {
                var date = startDate.AddDays(i);
                var dayEvents = events
                    .Where(e => e.StartUtc.Date == date.Date)
                    .OrderBy(e => e.StartUtc)
                    .ToList();

                days.Add(new CalendarDay
                {
                    Date = date,
                    DayNumber = date.Day,
                    IsCurrentMonth = date.Month == CurrentMonth.Month && date.Year == CurrentMonth.Year,
                    IsToday = date.Date == DateTime.UtcNow.Date,
                    HasEvents = dayEvents.Count > 0,
                    Events = new ObservableCollection<CalendarEvent>(dayEvents),
                });
            }

            Days = new ObservableCollection<CalendarDay>(days);
            RefreshSelectedDateEvents();
            StatusMessage = $"{events.Count} event(s) this month.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Loading the calendar failed");
            StatusMessage = $"Could not load the calendar: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PreviousMonthAsync()
    {
        CurrentMonth = CurrentMonth.AddMonths(-1);
        await LoadMonthAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        CurrentMonth = CurrentMonth.AddMonths(1);
        await LoadMonthAsync();
    }

    [RelayCommand]
    private async Task TodayAsync()
    {
        CurrentMonth = DateTime.UtcNow;
        SelectedDate = DateTime.UtcNow;
        await LoadMonthAsync();
    }

    [RelayCommand]
    private void SelectDate(CalendarDay day) => SelectedDate = day.Date;

    /// <summary>Creates an event on the selected day (F6.11).</summary>
    [RelayCommand]
    private async Task AddEventAsync()
    {
        var start = SelectedDate.Date.AddHours(9);
        var draft = new CalendarEvent
        {
            Title = "",
            StartUtc = start,
            EndUtc = start.AddHours(1),
        };

        await EditAndSaveAsync(draft, isNew: true);
    }

    /// <summary>Edits an existing event (F6.11).</summary>
    [RelayCommand]
    private async Task EditEventAsync(CalendarEvent? calendarEvent)
    {
        var target = calendarEvent ?? SelectedEvent;
        if (target is null) return;

        // Edit a copy: cancelling must not leave half-edited values in the grid.
        var copy = new CalendarEvent
        {
            Id = target.Id,
            AccountId = target.AccountId,
            Title = target.Title,
            Description = target.Description,
            Location = target.Location,
            StartUtc = target.StartUtc,
            EndUtc = target.EndUtc,
            AllDay = target.AllDay,
            Color = target.Color,
            ReminderMinutes = target.ReminderMinutes,
            RecurrenceRule = target.RecurrenceRule,
            CreatedAt = target.CreatedAt,
            UpdatedAt = target.UpdatedAt,
        };

        await EditAndSaveAsync(copy, isNew: false);
    }

    private async Task EditAndSaveAsync(CalendarEvent draft, bool isNew)
    {
        if (EditEventInteraction is null)
        {
            StatusMessage = "The event editor is not available.";
            return;
        }

        CalendarEvent? edited;
        try
        {
            edited = await EditEventInteraction(draft);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Event editor failed");
            StatusMessage = $"Editor failed: {ex.Message}";
            return;
        }

        if (edited is null) return; // cancelled

        try
        {
            if (_calendarStore != null)
            {
                if (isNew) await _calendarStore.SaveEventAsync(edited);
                else await _calendarStore.UpdateEventAsync(edited);
            }

            SelectedDate = edited.StartUtc;
            CurrentMonth = edited.StartUtc;
            await LoadMonthAsync();
            StatusMessage = isNew ? $"Created \"{edited.Title}\"." : $"Saved \"{edited.Title}\".";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Saving the event failed");
            StatusMessage = $"Could not save the event: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteEventAsync(CalendarEvent? calendarEvent)
    {
        var target = calendarEvent ?? SelectedEvent;
        if (target is null) return;

        try
        {
            if (_calendarStore != null) await _calendarStore.DeleteEventAsync(target.Id);
            await LoadMonthAsync();
            StatusMessage = $"Deleted \"{target.Title}\".";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Deleting the event failed");
            StatusMessage = $"Could not delete the event: {ex.Message}";
        }
    }
}

/// <summary>
/// Represents a single day in the calendar grid.
/// </summary>
public class CalendarDay
{
    public DateTime Date { get; set; }
    public int DayNumber { get; set; }
    public bool IsCurrentMonth { get; set; }
    public bool IsToday { get; set; }
    public bool HasEvents { get; set; }
    public ObservableCollection<CalendarEvent> Events { get; set; } = new();
}
