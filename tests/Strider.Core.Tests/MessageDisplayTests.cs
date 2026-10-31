using System.ComponentModel;
using FluentAssertions;
using Strider.Core.Domain;

namespace Strider.Core.Tests;

public class MessageDisplayTests
{
    private static readonly DateTime Now = new(2026, 10, 31, 15, 0, 0, DateTimeKind.Local);

    [Fact]
    public void ListDate_Today_IsTheLocalTime()
    {
        var local = new DateTime(2026, 10, 31, 9, 30, 0, DateTimeKind.Local);
        Message.FormatListDate(local.ToUniversalTime(), Now).Should().Be("09:30");
    }

    [Fact]
    public void ListDate_EarlierThisYear_HasNoTime()
    {
        var local = new DateTime(2026, 3, 2, 9, 30, 0, DateTimeKind.Local);
        Message.FormatListDate(local.ToUniversalTime(), Now).Should().StartWith("02 ").And.NotContain(":");
    }

    [Fact]
    public void ListDate_OlderYear_ShowsTheYear()
    {
        var local = new DateTime(2024, 3, 2, 9, 30, 0, DateTimeKind.Local);
        Message.FormatListDate(local.ToUniversalTime(), Now).Should().Contain("2024");
    }

    [Fact]
    public void ListDate_Missing_IsEmpty() => Message.FormatListDate(null, Now).Should().BeEmpty();

    [Fact]
    public void ChangingState_NotifiesTheListRow()
    {
        var message = new Message();
        var changes = new List<string?>();
        ((INotifyPropertyChanged)message).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        message.IsRead = true;
        message.IsStarred = true;
        message.FlagColor = FlagColor.Blue;

        changes.Should().Contain(new[] { "IsRead", "IsStarred", "FlagColor", "HasFlagColor", "FlagColorHex" });
    }
}
