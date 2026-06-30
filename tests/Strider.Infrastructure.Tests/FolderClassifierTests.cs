using Strider.Core.Domain;
using Strider.Infrastructure.Mail;
using FluentAssertions;

namespace Strider.Infrastructure.Tests;

/// <summary>
/// Tests for FolderClassifier — verifies that IMAP folder names from
/// different providers (Gmail, Outlook, Yahoo, generic) are correctly
/// mapped to FolderType enum values.
///
/// Closes finding F-023: AccountWizard was hardcoding INBOX/Sent/Drafts/Trash
/// without knowing the actual folder names on the user's IMAP server.
/// </summary>
public class FolderClassifierTests
{
    // === Inbox ===

    [Theory]
    [InlineData("INBOX")]
    [InlineData("inbox")]
    [InlineData("Inbox")]
    [InlineData("iNbOx")]
    public void Classify_Inbox_CaseInsensitive_ReturnsInbox(string name)
    {
        FolderClassifier.Classify(name).Should().Be(FolderType.Inbox);
    }

    // === Sent — provider variants ===

    [Theory]
    [InlineData("Sent")]                       // Outlook, generic
    [InlineData("Sent Mail")]                  // Gmail (without brackets)
    [InlineData("Sent Items")]                 // Outlook alt
    [InlineData("SentMail")]                   // No-space variant
    [InlineData("[Gmail]/Sent Mail")]          // Gmail canonical
    [InlineData("[Gmail]/Sent")]               // Gmail alt
    public void Classify_Sent_Variants_ReturnsSent(string name)
    {
        FolderClassifier.Classify(name).Should().Be(FolderType.Sent);
    }

    // === Drafts ===

    [Theory]
    [InlineData("Drafts")]
    [InlineData("Draft")]
    [InlineData("[Gmail]/Drafts")]
    public void Classify_Drafts_Variants_ReturnsDrafts(string name)
    {
        FolderClassifier.Classify(name).Should().Be(FolderType.Drafts);
    }

    // === Trash / Deleted ===

    [Theory]
    [InlineData("Trash")]                      // Generic, Gmail
    [InlineData("Deleted")]                    // Short
    [InlineData("Deleted Items")]              // Outlook
    [InlineData("Deleted Messages")]           // Some providers
    [InlineData("Bin")]                        // Some providers
    [InlineData("[Gmail]/Trash")]              // Gmail canonical
    public void Classify_Trash_Variants_ReturnsTrash(string name)
    {
        FolderClassifier.Classify(name).Should().Be(FolderType.Trash);
    }

    // === Spam / Junk ===

    [Theory]
    [InlineData("Spam")]
    [InlineData("Junk")]
    [InlineData("Junk Email")]
    [InlineData("Bulk Mail")]                  // Yahoo
    [InlineData("[Gmail]/Spam")]
    public void Classify_Spam_Variants_ReturnsSpam(string name)
    {
        FolderClassifier.Classify(name).Should().Be(FolderType.Spam);
    }

    // === Archive ===

    [Theory]
    [InlineData("Archive")]
    [InlineData("All Mail")]                   // Gmail
    [InlineData("AllMail")]
    [InlineData("[Gmail]/All Mail")]
    public void Classify_Archive_Variants_ReturnsArchive(string name)
    {
        FolderClassifier.Classify(name).Should().Be(FolderType.Archive);
    }

    // === Custom / unknown ===

    [Theory]
    [InlineData("Notes")]
    [InlineData("Calendar")]
    [InlineData("My Personal Folder")]
    [InlineData("Work")]
    [InlineData("Family")]
    [InlineData("Project Alpha")]
    public void Classify_UnknownFolders_ReturnsCustom(string name)
    {
        FolderClassifier.Classify(name).Should().Be(FolderType.Custom);
    }

    // === Edge cases ===

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Classify_NullOrEmpty_ReturnsCustom(string? name)
    {
        FolderClassifier.Classify(name!).Should().Be(FolderType.Custom);
    }

    [Fact]
    public void Classify_WithLeadingTrailingSpaces_TrimsCorrectly()
    {
        FolderClassifier.Classify("  Sent  ").Should().Be(FolderType.Sent);
        FolderClassifier.Classify("  INBOX  ").Should().Be(FolderType.Inbox);
    }

    [Fact]
    public void Classify_WithBrackets_StripsBrackets()
    {
        // Gmail-style names with [Gmail] prefix — brackets should be stripped
        // from the trimmed value, but the inner "[Gmail]/Sent Mail" content
        // is matched via Contains
        FolderClassifier.Classify("[Gmail]/Sent Mail").Should().Be(FolderType.Sent);
        FolderClassifier.Classify("[Gmail]/Trash").Should().Be(FolderType.Trash);
        FolderClassifier.Classify("[Gmail]/Spam").Should().Be(FolderType.Spam);
    }

    [Fact]
    public void Classify_MixedCase_NormalizesToLowercase()
    {
        FolderClassifier.Classify("SENT").Should().Be(FolderType.Sent);
        FolderClassifier.Classify("DRAFTS").Should().Be(FolderType.Drafts);
        FolderClassifier.Classify("TRASH").Should().Be(FolderType.Trash);
        FolderClassifier.Classify("JUNK EMAIL").Should().Be(FolderType.Spam);
    }
}
