namespace Strider.Infrastructure.Ai;

/// <summary>
/// Prompt templates for AI features.
/// Users can customize these in settings.
/// </summary>
public static class PromptTemplates
{
    public static string SummarizeThread(string threadContent) => $"""
        Summarize the following email thread in 2-4 concise bullet points.
        Focus on: key decisions, action items, and deadlines.
        Ignore: greetings, signatures, quoted text.

        Thread:
        {threadContent}

        Format as bullet points only.
        """;

    public static string DraftReply(string originalMessage, string userStyle = "professional") => $"""
        Draft a reply to the following email.
        Style: {userStyle}
        Be concise and direct. Match the tone of the original message.

        Original message:
        {originalMessage}

        Draft reply:
        """;

    public static string ClassifyEmail(string subject, string from, string body) => $"""
        Classify this email into exactly one category:
        - Work: business communication, project updates, meetings
        - Personal: friends, family, personal matters
        - Newsletter: mailing lists, subscriptions, marketing
        - Transactional: receipts, confirmations, notifications
        - Action Required: needs a response or action from me
        - Spam-like: suspicious, phishing, unwanted

        Subject: {subject}
        From: {from}
        Body preview: {body[..Math.Min(body.Length, 500)]}

        Reply with ONLY the category name.
        """;

    public static string ExtractActionItems(string threadContent) => $"""
        Extract all action items and TODOs from this email thread.
        For each item, specify:
        - What needs to be done
        - Who is responsible (if mentioned)
        - Deadline (if mentioned)

        Thread:
        {threadContent}

        Format as numbered list.
        """;

    public static string TranslateMessage(string content, string targetLanguage) => $"""
        Translate the following email to {targetLanguage}.
        Preserve the original tone and formatting.

        {content}
        """;

    public static string ImproveWriting(string content) => $"""
        Improve the following email draft:
        - Fix grammar and spelling
        - Make it more concise
        - Maintain the original intent and tone

        Original:
        {content}

        Improved version:
        """;
}
