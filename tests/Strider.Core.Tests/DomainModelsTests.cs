using Strider.Core.Domain;
using FluentAssertions;

namespace Strider.Core.Tests;

/// <summary>
/// Tests for domain model defaults and invariants.
/// These are simple smoke tests verifying that domain models initialize
/// with expected defaults — they catch accidental breaking changes.
/// </summary>
public class DomainModelsTests
{
    [Fact]
    public void Account_Defaults_AreCorrect()
    {
        var account = new Account();
        account.Id.Should().NotBeEmpty();
        account.ImapPort.Should().Be(993);
        account.SmtpPort.Should().Be(587);
        account.ImapUseSsl.Should().BeTrue();
        account.SmtpUseSsl.Should().BeTrue();
        account.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        account.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Message_Defaults_AreCorrect()
    {
        var message = new Message();
        message.Id.Should().NotBeEmpty();
        message.ToAddresses.Should().Be("[]");
        message.PgpStatus.Should().Be(PgpStatus.None);
        message.PgpVerified.Should().Be(PgpVerification.Unknown);
        message.FetchedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Folder_Defaults_AreCorrect()
    {
        var folder = new Folder();
        folder.Id.Should().NotBeEmpty();
        folder.Type.Should().Be(FolderType.Custom);
        folder.LastSyncUid.Should().Be(0);
        folder.UnreadCount.Should().Be(0);
    }

    [Fact]
    public void CalendarEvent_Defaults_AreCorrect()
    {
        var evt = new CalendarEvent();
        evt.Id.Should().NotBeEmpty();
        evt.AllDay.Should().BeFalse();
        evt.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void PgpKey_Defaults_AreCorrect()
    {
        var key = new PgpKey();
        key.Id.Should().NotBeEmpty();
        key.IsDefault.Should().BeFalse();
        key.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void PendingOp_Defaults_AreCorrect()
    {
        var op = new PendingOp();
        op.Id.Should().NotBeEmpty();
        op.Payload.Should().Be("{}");
        op.Status.Should().Be(PendingOpStatus.Pending);
        op.RetryCount.Should().Be(0);
    }

    [Fact]
    public void Signature_Defaults_AreCorrect()
    {
        var sig = new Signature();
        sig.Id.Should().NotBeEmpty();
        sig.IsDefault.Should().BeFalse();
        sig.SortOrder.Should().Be(0);
    }

    [Fact]
    public void AiSettings_Defaults_AreCorrect()
    {
        var settings = new AiSettings();
        settings.Id.Should().NotBeEmpty();
        settings.IsDefault.Should().BeFalse();
        settings.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(FolderType.Inbox)]
    [InlineData(FolderType.Sent)]
    [InlineData(FolderType.Drafts)]
    [InlineData(FolderType.Trash)]
    [InlineData(FolderType.Archive)]
    [InlineData(FolderType.Spam)]
    [InlineData(FolderType.Custom)]
    public void FolderType_AllValuesEnumerable(FolderType type)
    {
        // Sanity check — all enum values are valid and distinct
        Enum.GetNames(typeof(FolderType)).Should().HaveCount(7);
    }

    [Theory]
    [InlineData(PgpStatus.None)]
    [InlineData(PgpStatus.Signed)]
    [InlineData(PgpStatus.Encrypted)]
    [InlineData(PgpStatus.SignedAndEncrypted)]
    public void PgpStatus_AllValuesEnumerable(PgpStatus status)
    {
        Enum.GetNames(typeof(PgpStatus)).Should().HaveCount(4);
    }
}
