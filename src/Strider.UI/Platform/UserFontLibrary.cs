using Avalonia.Media;
using Avalonia.Media.Fonts;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Core.Platform;

namespace Strider.UI.Platform;

/// <summary>
/// Fonts for the composer: OS fonts plus files the user added.
///
/// Avalonia can load a folder of font files at runtime through
/// <see cref="EmbeddedFontCollection"/> with a <c>file://</c> source, so adding a
/// font is: copy the file into <see cref="AppPaths.Fonts"/>, (re)register the
/// folder, remember the family name. Everything stays local — no font is ever
/// downloaded.
/// </summary>
public sealed class UserFontLibrary : IFontLibrary
{
    private readonly ICustomFontStore _store;

    /// <summary>Key of the currently registered collection, so it can be replaced.</summary>
    private Uri? _registeredKey;

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".ttf", ".otf", ".ttc", ".otc" };

    public UserFontLibrary(ICustomFontStore store)
    {
        _store = store;
    }

    public IReadOnlyList<string> GetAvailableFamilies()
    {
        var families = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var family in FontManager.Current.SystemFonts)
            {
                if (!string.IsNullOrWhiteSpace(family.Name)) families.Add(family.Name);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not enumerate system fonts");
        }

        try
        {
            foreach (var custom in _store.GetAllAsync().GetAwaiter().GetResult())
            {
                if (!string.IsNullOrWhiteSpace(custom.Family)) families.Add(custom.Family);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not read the custom font list");
        }

        // A couple of mail-safe families that may not be installed everywhere.
        families.Add("Inter");
        families.Add("Arial");
        families.Add("Georgia");
        families.Add("Times New Roman");
        families.Add("Courier New");

        return families.ToList();
    }

    public IReadOnlyList<CustomFont> GetCustomFonts()
    {
        try
        {
            return _store.GetAllAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not read the custom font list");
            return Array.Empty<CustomFont>();
        }
    }

    public Task<CustomFont> AddFontFileAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("Font file not found.", filePath);

        var extension = Path.GetExtension(filePath);
        if (!SupportedExtensions.Contains(extension))
            throw new InvalidOperationException(
                $"'{extension}' is not a font file. Supported: .ttf, .otf, .ttc, .otc.");

        var fontsDir = AppPaths.Fonts;
        var target = Path.Combine(fontsDir, Path.GetFileName(filePath));
        if (!string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            File.Copy(filePath, target, overwrite: true);

        var familiesBefore = RegisteredFamilyNames();
        RegisterFontsFolder(force: true);
        var familiesAfter = RegisteredFamilyNames();

        var added = familiesAfter.Except(familiesBefore, StringComparer.OrdinalIgnoreCase).ToList();
        var family = added.FirstOrDefault()
                     ?? Path.GetFileNameWithoutExtension(target);

        var font = new CustomFont
        {
            Family = family,
            FilePath = target,
            AddedAt = DateTime.UtcNow,
        };

        _store.SaveAsync(font, ct).GetAwaiter().GetResult();
        Serilog.Log.Information("Custom font '{Family}' added from {File}", family, target);
        return Task.FromResult(font);
    }

    public Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var font = GetCustomFonts().FirstOrDefault(f => f.Id == id);
        if (font is null) return Task.CompletedTask;

        try
        {
            if (File.Exists(font.FilePath)) File.Delete(font.FilePath);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not delete the font file {File}", font.FilePath);
        }

        _store.DeleteAsync(id, ct).GetAwaiter().GetResult();
        RegisterFontsFolder(force: true);
        return Task.CompletedTask;
    }

    public void EnsureRegistered() => RegisterFontsFolder(force: true);

    // === registration ===

    private void RegisterFontsFolder(bool force)
    {
        try
        {
            var dir = AppPaths.Fonts;
            if (!Directory.Exists(dir)) return;
            if (!force && _registeredKey is not null) return;

            if (_registeredKey is not null)
            {
                try { FontManager.Current.RemoveFontCollection(_registeredKey); }
                catch (Exception ex) { Serilog.Log.Debug(ex, "Removing the previous font collection failed"); }
            }

            // A new key per registration: Avalonia caches collections by key, so
            // reusing one would keep serving the old file list.
            var key = new Uri($"fonts:StriderUser-{Guid.NewGuid():N}", UriKind.Absolute);
            var source = new Uri("file://" + dir.TrimEnd(Path.DirectorySeparatorChar) + "/");
            var collection = new EmbeddedFontCollection(key, source);
            FontManager.Current.AddFontCollection(collection);
            _registeredKey = key;

            Serilog.Log.Information("User font folder registered ({Count} families)", collection.Count);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not register the user font folder");
        }
    }

    /// <summary>Family names served by our own collections (i.e. the user's fonts).</summary>
    private static HashSet<string> RegisteredFamilyNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var dir = AppPaths.Fonts;
            if (!Directory.Exists(dir)) return names;

            var probeKey = new Uri($"fonts:StriderProbe-{Guid.NewGuid():N}", UriKind.Absolute);
            var probe = new EmbeddedFontCollection(probeKey, new Uri("file://" + dir.TrimEnd(Path.DirectorySeparatorChar) + "/"));
            foreach (var family in probe)
            {
                if (!string.IsNullOrWhiteSpace(family.Name)) names.Add(family.Name);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Probing user fonts failed");
        }

        return names;
    }
}
