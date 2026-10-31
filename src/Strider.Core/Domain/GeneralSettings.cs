namespace Strider.Core.Domain;

/// <summary>
/// App-wide preferences from the General tab. Stored in <c>app_settings</c> as JSON
/// under one key, the same way as <see cref="TextStyleSettings"/>.
/// </summary>
public class GeneralSettings
{
    public const string SettingsKey = "general";

    /// <summary>"system", "dark" or "light".</summary>
    public string Theme { get; set; } = "system";

    public string Language { get; set; } = "en";

    public bool ShowNotifications { get; set; } = true;

    /// <summary>Minutes between background syncs (F3.2, default 5); 0 turns it off.</summary>
    public int SyncIntervalMinutes { get; set; } = 5;

    /// <summary>How long a sent message can still be recalled (F4.7); 0 sends at once.</summary>
    public int UndoSendSeconds { get; set; } = 5;
}
