using CommunityToolkit.Mvvm.ComponentModel;
using Strider.Core.Domain;

namespace Strider.UI.ViewModels;

/// <summary>
/// Represents a folder in the sidebar. The folder list itself lives in
/// <see cref="MainWindowViewModel"/>; this type was extracted from the dead
/// FolderTreeViewModel in Wave 5 (the old ViewModel was never registered in DI
/// and never used by any view).
/// </summary>
public partial class FolderItem : ObservableObject
{
    public Folder Folder { get; set; } = new();
    public string Name { get; set; } = "";

    /// <summary>Text label instead of an emoji — Inter has no emoji glyphs (F-027).</summary>
    public string Icon { get; set; } = "[Folder]";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private int _unreadCount;

    public string DisplayName => UnreadCount > 0 ? $"{Icon} {Name} ({UnreadCount})" : $"{Icon} {Name}";
}
