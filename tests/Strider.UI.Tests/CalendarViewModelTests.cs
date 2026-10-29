using FluentAssertions;
using Strider.Core.Domain;
using Strider.UI.ViewModels;

namespace Strider.UI.Tests;

/// <summary>
/// Wave 5 regression tests for the calendar grid and the event editor.
/// </summary>
public class CalendarViewModelTests
{
    [Fact]
    public async Task MonthGrid_StartsOnMonday_EvenWhenTheMonthStartsOnSunday()
    {
        // 1 March 2026 is a Sunday — the old formula shifted the grid by a day.
        var vm = new CalendarViewModel { CurrentMonth = new DateTime(2026, 3, 1) };

        await vm.LoadMonthAsync();

        vm.Days.Should().HaveCount(42);
        vm.Days[0].Date.DayOfWeek.Should().Be(DayOfWeek.Monday);
        vm.Days[0].Date.Should().Be(new DateTime(2026, 2, 23));
        vm.Days[1].Date.Should().Be(new DateTime(2026, 2, 24));
    }

    [Fact]
    public async Task MonthGrid_ColumnOrder_IsMondayToSunday()
    {
        var vm = new CalendarViewModel { CurrentMonth = new DateTime(2026, 10, 1) };

        await vm.LoadMonthAsync();

        vm.Days.Take(7).Select(d => d.Date.DayOfWeek).Should().Equal(
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday);
    }

    [Fact]
    public async Task MonthGrid_MarksToday_AndOnlyTheCurrentMonth()
    {
        var vm = new CalendarViewModel { CurrentMonth = DateTime.UtcNow };

        await vm.LoadMonthAsync();

        vm.Days.Count(d => d.IsToday).Should().BeLessThanOrEqualTo(1);
        vm.Days.Count(d => d.IsCurrentMonth).Should().BeInRange(28, 31);
    }

    [Fact]
    public async Task AddEvent_CancelledEditor_SavesNothing()
    {
        var vm = new CalendarViewModel { CurrentMonth = new DateTime(2026, 3, 1) };
        CalendarEvent? offered = null;
        vm.EditEventInteraction = draft =>
        {
            offered = draft;
            return Task.FromResult<CalendarEvent?>(null); // cancel
        };

        await vm.AddEventCommand.ExecuteAsync(null);

        offered.Should().NotBeNull("the editor must have been offered the default event");
        vm.StatusMessage.Should().NotBe("Created", "nothing was accepted, so nothing was saved");
    }

    [Fact]
    public async Task AddEvent_AcceptedEditor_ReportsTheNewEvent()
    {
        var vm = new CalendarViewModel { CurrentMonth = new DateTime(2026, 3, 1) };
        vm.EditEventInteraction = draft =>
        {
            draft.Title = "Standup";
            return Task.FromResult<CalendarEvent?>(draft);
        };

        await vm.AddEventCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("Standup");
    }

    [Fact]
    public async Task AddEvent_WithoutEditor_DoesNotThrow()
    {
        var vm = new CalendarViewModel { CurrentMonth = new DateTime(2026, 3, 1) };

        await vm.AddEventCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("not available");
    }
}

public class EventEditorViewModelTests
{
    private static EventEditorViewModel Editor(CalendarEvent? draft = null) =>
        draft is null ? new EventEditorViewModel() : new EventEditorViewModel(draft);

    [Fact]
    public void Save_WithoutTitle_RefusesAndKeepsDialogOpen()
    {
        var vm = Editor();
        CalendarEvent? result = null;
        vm.CloseRequested += e => result = e;

        vm.SaveCommand.Execute(null);

        result.Should().BeNull();
        vm.StatusMessage.Should().Contain("title");
    }

    [Fact]
    public void Save_WithTitle_ReturnsEventInUtc()
    {
        var vm = Editor();
        CalendarEvent? result = null;
        vm.CloseRequested += e => result = e;

        vm.Title = "Planning";
        vm.StartDate = new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero);
        vm.StartTime = new TimeSpan(9, 0, 0);
        vm.EndDate = new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero);
        vm.EndTime = new TimeSpan(10, 0, 0);

        vm.SaveCommand.Execute(null);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Planning");
        result.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
        result.EndUtc.Should().Be(result.StartUtc.AddHours(1));
    }

    [Fact]
    public void Save_WithEndBeforeStart_Refuses()
    {
        var vm = Editor();
        vm.Title = "Backwards";
        vm.StartDate = new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero);
        vm.StartTime = new TimeSpan(12, 0, 0);
        vm.EndDate = new DateTimeOffset(new DateTime(2026, 3, 2), TimeSpan.Zero);
        vm.EndTime = new TimeSpan(11, 0, 0);

        vm.SaveCommand.Execute(null);

        vm.StatusMessage.Should().Contain("end must be after the start");
    }

    [Fact]
    public void ExistingEvent_IsPrefilled_AndKeepsItsId()
    {
        var existing = new CalendarEvent
        {
            Title = "Review",
            StartUtc = new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc),
            Location = "Room 3",
        };

        var vm = Editor(existing);
        CalendarEvent? result = null;
        vm.CloseRequested += e => result = e;

        vm.Title.Should().Be("Review");
        vm.Location.Should().Be("Room 3");

        vm.SaveCommand.Execute(null);

        result!.Id.Should().Be(existing.Id, "editing must update, not duplicate");
    }

    [Fact]
    public void Cancel_ReturnsNull()
    {
        var vm = Editor();
        var closed = false;
        CalendarEvent? result = new CalendarEvent();
        vm.CloseRequested += e =>
        {
            closed = true;
            result = e;
        };

        vm.CancelCommand.Execute(null);

        closed.Should().BeTrue();
        result.Should().BeNull();
    }
}
