using Strider.Core.Domain;

namespace Strider.Core.Abstractions;

/// <summary>
/// Fonts available to the composer: the fonts installed in the OS plus the ones
/// the user added from files. Adding a file copies it into the app's font
/// folder and registers it with the UI toolkit, so it survives a restart.
/// </summary>
public interface IFontLibrary
{
    /// <summary>Every family that can be chosen, system and user-added, deduplicated and sorted.</summary>
    IReadOnlyList<string> GetAvailableFamilies();

    /// <summary>Fonts the user added (family + the file it came from).</summary>
    IReadOnlyList<CustomFont> GetCustomFonts();

    /// <summary>
    /// Copies a .ttf/.otf/.ttc into the app font folder and registers it.
    /// Returns the recorded font, or throws with a readable message when the
    /// file is not a usable font.
    /// </summary>
    Task<CustomFont> AddFontFileAsync(string filePath, CancellationToken ct = default);

    /// <summary>Forgets a font (the file is deleted; the family disappears from lists).</summary>
    Task RemoveAsync(Guid id, CancellationToken ct = default);

    /// <summary>Re-registers everything from disk — called once during start-up.</summary>
    void EnsureRegistered();
}

/// <summary>Persistence for user-added fonts.</summary>
public interface ICustomFontStore
{
    Task<IReadOnlyList<CustomFont>> GetAllAsync(CancellationToken ct = default);
    Task SaveAsync(CustomFont font, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
