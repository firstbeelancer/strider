using Strider.Core.Domain;

namespace Strider.Infrastructure.Mail;

/// <summary>
/// Maps remote IMAP folder names (case-insensitive, with provider variants)
/// to the <see cref="FolderType"/> enum.
///
/// Different IMAP providers use different names for well-known folders:
/// - Gmail: [Gmail]/Sent Mail, [Gmail]/Drafts, [Gmail]/Trash, [Gmail]/Spam, [Gmail]/All Mail
/// - Outlook: Sent, Drafts, Deleted Items, Junk Email, Archive
/// - Yahoo: Sent, Drafts, Trash, Bulk Mail
/// - RFC 3501: INBOX (case-insensitive, but conventionally uppercase)
///
/// This class is public and static so it can be unit-tested independently
/// of AccountWizardViewModel.
/// </summary>
public static class FolderClassifier
{
    /// <summary>
    /// Maps a remote folder name to a FolderType. Returns FolderType.Custom
    /// for any unrecognized name (user-created folders, provider-specific
    /// folders like "Notes" or "Calendar").
    /// </summary>
    public static FolderType Classify(string remoteName)
    {
        if (string.IsNullOrEmpty(remoteName)) return FolderType.Custom;

        // Normalize: lowercase, remove [Gmail]/-style brackets entirely (so
        // "[Gmail]/Sent Mail" becomes "gmail/sent mail"). Just Trim() with
        // bracket chars doesn't work because the closing bracket is in the
        // middle of the string.
        var name = remoteName.ToLowerInvariant()
            .Replace("[", "")
            .Replace("]", "")
            .Trim();

        // INBOX is special — RFC 3501 mandates case-insensitive match
        if (name == "inbox") return FolderType.Inbox;

        // Sent — many variants across providers
        if (name == "sent" ||
            name == "sent mail" ||
            name == "sent items" ||
            name == "sentmail" ||
            name.Contains("gmail/sent") ||
            // IMAP UTF-7 encoded "Sent" in some locales
            name == "&bcc-sented" ||
            name == "&x-f_sent")
            return FolderType.Sent;

        // Drafts
        if (name == "drafts" ||
            name == "draft" ||
            name == "drafts folder" ||
            name.Contains("gmail/drafts"))
            return FolderType.Drafts;

        // Trash / Deleted
        if (name == "trash" ||
            name == "deleted" ||
            name == "deleted items" ||
            name == "deleted messages" ||
            name == "bin" ||
            name.Contains("gmail/trash") ||
            name == "&bcm-abpf-" ||  // IMAP UTF-7 "Trash" in some locales
            name == "deleteditems")
            return FolderType.Trash;

        // Spam / Junk
        if (name == "spam" ||
            name == "junk" ||
            name == "junk email" ||
            name == "junkemail" ||
            name == "bulk mail" ||
            name == "bulkmail" ||
            name.Contains("gmail/spam"))
            return FolderType.Spam;

        // Archive
        if (name == "archive" ||
            name == "all mail" ||
            name == "allmail" ||
            name.Contains("gmail/all mail") ||
            name == "all messages")
            return FolderType.Archive;

        return FolderType.Custom;
    }
}
