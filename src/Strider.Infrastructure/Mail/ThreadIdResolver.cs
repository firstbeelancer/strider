using Strider.Core.Domain;

namespace Strider.Infrastructure.Mail;

/// <summary>
/// Resolves thread IDs for incoming messages using the JWZ threading algorithm
/// (https://www.jwz.org/doc/threading.html).
///
/// JWZ threading groups messages by their References and In-Reply-To headers:
/// - Messages sharing a common ancestor Message-ID belong to the same thread.
/// - A message with no References starts a new thread.
/// - When References and In-Reply-To disagree, References wins.
///
/// Implementation note: this is a simplified version of JWZ that handles
/// the common cases (linear threads, forked threads via References). It does
/// NOT implement the full algorithm's table-id linking or dummy-promotion;
/// those are rare edge cases that can be added in v0.2.
///
/// Closes finding F-014 from the architecture review: thread_id field in
/// the messages table was never populated.
/// </summary>
public static class ThreadIdResolver
{
    /// <summary>
    /// Resolves the thread ID for a single message, given a lookup of already-known
    /// threads by Message-ID.
    ///
    /// Returns the thread ID. If the message starts a new thread, the returned
    /// ID is the message's own Message-ID (or a generated fallback if Message-ID
    /// is missing).
    /// </summary>
    /// <param name="message">The message to resolve a thread for.</param>
    /// <param name="existingThreadsByMessageId">
    /// Lookup from RFC 5322 Message-ID → thread ID, built from messages already
    /// in the database. Caller is responsible for keeping this updated as new
    /// messages arrive.
    /// </param>
    public static string Resolve(
        Message message,
        IReadOnlyDictionary<string, string> existingThreadsByMessageId)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        if (existingThreadsByMessageId == null) throw new ArgumentNullException(nameof(existingThreadsByMessageId));

        // 0. Out-of-order scenario: this message's own Message-ID may already be in
        // the lookup (registered by a later-arriving reply that referenced it).
        // If so, join that thread.
        if (!string.IsNullOrEmpty(message.MessageId) &&
            existingThreadsByMessageId.TryGetValue(message.MessageId, out var existingThread))
        {
            return existingThread;
        }

        // 1. Collect candidate ancestor Message-IDs from References + In-Reply-To
        var referenceIds = ParseReferences(message.References);
        if (!string.IsNullOrEmpty(message.InReplyTo))
        {
            // In-Reply-To is appended last so References take precedence per JWZ
            if (!referenceIds.Contains(message.InReplyTo))
            {
                referenceIds.Add(message.InReplyTo);
            }
        }

        // 2. Look up each reference ID in existing threads
        foreach (var refId in referenceIds)
        {
            if (existingThreadsByMessageId.TryGetValue(refId, out var threadId))
            {
                return threadId;
            }
        }

        // 3. No ancestor found — start a new thread.
        // Use the message's own Message-ID as the thread ID (JWZ convention);
        // fall back to a synthetic ID if Message-ID is missing.
        return !string.IsNullOrEmpty(message.MessageId)
            ? message.MessageId
            : $"synthetic:{message.Id}";
    }

    /// <summary>
    /// Updates the lookup after a new message is added to a thread.
    /// Call this after persisting the message so subsequent messages can find it.
    /// </summary>
    public static void RegisterMessage(
        Message message,
        string threadId,
        Dictionary<string, string> threadsByMessageId)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        if (string.IsNullOrEmpty(threadId)) throw new ArgumentException("Thread ID required", nameof(threadId));
        if (threadsByMessageId == null) throw new ArgumentNullException(nameof(threadsByMessageId));

        // Register the message's own Message-ID
        if (!string.IsNullOrEmpty(message.MessageId))
        {
            threadsByMessageId[message.MessageId] = threadId;
        }

        // Also register any reference IDs that aren't yet known — this lets future
        // replies find this thread even if the referenced message never arrives
        // (e.g., message arrives out-of-order, or referenced message was deleted
        // on server before we synced).
        foreach (var refId in ParseReferences(message.References))
        {
            if (!threadsByMessageId.ContainsKey(refId))
            {
                threadsByMessageId[refId] = threadId;
            }
        }

        if (!string.IsNullOrEmpty(message.InReplyTo) &&
            !threadsByMessageId.ContainsKey(message.InReplyTo))
        {
            threadsByMessageId[message.InReplyTo] = threadId;
        }
    }

    /// <summary>
    /// Parses the References header into a list of Message-IDs.
    /// References is a space-separated list of &lt;msgid&gt; tokens per RFC 5322.
    /// Returns Message-IDs WITH angle brackets preserved (canonical RFC 5322 form),
    /// so they match what's stored in Message.MessageId and the lookup dictionary.
    /// Returns an empty list if References is null/empty/malformed.
    /// </summary>
    public static List<string> ParseReferences(string? references)
    {
        if (string.IsNullOrWhiteSpace(references)) return new List<string>(0);

        // Try JSON first (Strider stores references_json as JSON array in DB)
        if (references.TrimStart().StartsWith("["))
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(references)
                    ?? new List<string>(0);
            }
            catch
            {
                // Fall through to RFC 5322 parsing
            }
        }

        // RFC 5322: space-separated &lt;msgid&gt; tokens, optionally comma-separated
        var result = new List<string>();
        var tokens = references.Split(new[] { ' ', '\t', ',', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            // Normalize to canonical form WITH angle brackets
            var id = token.Trim();
            if (string.IsNullOrEmpty(id)) continue;

            if (!id.StartsWith("<") && !id.EndsWith(">"))
            {
                // Wrap in angle brackets if missing
                id = "<" + id + ">";
            }

            if (!result.Contains(id))
            {
                result.Add(id);
            }
        }
        return result;
    }
}
