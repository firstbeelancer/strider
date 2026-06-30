using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the calendar view.
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

    /// <summary>
    /// Constructor for DI. ICalendarStore is optional because the calendar
    /// view can be shown before any account is configured (local-only events
    /// are stored in the same SQLite DB but accessed via ICalendarStore).
    /// </summary>
    public CalendarViewModel(ICalendarStore? calendarStore = null)
    {
        _calendarStore = calendarStore;
    }

    [ObservableProperty]
    private string _viewMode = "month"; // month, week, day

    [ObservableProperty]
    private bool _isLoading;

    [RelayCommand]
    public async Task LoadMonthAsync()
    {
        IsLoading = true;

        try
        {
            var firstDay = new DateTime(CurrentMonth.Year, CurrentMonth.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            // Load events for the month
            var events = new List<CalendarEvent>();
            if (_calendarStore != null)
            {
                events = (await _calendarStore.GetEventsAsync(firstDay, lastDay.AddDays(1))).ToList();
            }

            Events = new ObservableCollection<CalendarEvent>(events);

            // Build calendar grid
            var days = new List<CalendarDay>();
            var startDate = firstDay.AddDays(-(int)firstDay.DayOfWeek + 1); // Monday start

            for (int i = 0; i < 42; i++) // 6 weeks
            {
                var date = startDate.AddDays(i);
                var dayEvents = events.Where(e => e.StartUtc.Date == date.Date).ToList();
                days.Add(new CalendarDay
                {
                    Date = date,
                    DayNumber = date.Day,
                    IsCurrentMonth = date.Month == CurrentMonth.Month,
                    IsToday = date.Date == DateTime.UtcNow.Date,
                    Events = new ObservableCollection<CalendarEvent>(dayEvents),
                    HasEvents = dayEvents.Count > 0,
                });
            }

            Days = new ObservableCollection<CalendarDay>(days);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void PreviousMonth()
    {
        CurrentMonth = CurrentMonth.AddMonths(-1);
        _ = LoadMonthAsync();
    }

    [RelayCommand]
    private void NextMonth()
    {
        CurrentMonth = CurrentMonth.AddMonths(1);
        _ = LoadMonthAsync();
    }

    [RelayCommand]
    private void Today()
    {
        CurrentMonth = DateTime.UtcNow;
        SelectedDate = DateTime.UtcNow;
        _ = LoadMonthAsync();
    }

    [RelayCommand]
    private void SelectDate(CalendarDay day)
    {
        SelectedDate = day.Date;
    }

    [RelayCommand]
    private void AddEvent()
    {
        // TODO: Open event dialog
    }

    [RelayCommand]
    private void EditEvent(CalendarEvent evt)
    {
        SelectedEvent = evt;
        // TODO: Open event dialog with existing event
    }

    [RelayCommand]
    private async Task DeleteEventAsync(CalendarEvent evt)
    {
        if (_calendarStore != null)
        {
            await _calendarStore.DeleteEventAsync(evt.Id);
        }
        Events.Remove(evt);
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
