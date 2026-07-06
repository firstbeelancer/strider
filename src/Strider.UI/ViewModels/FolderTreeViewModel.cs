using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the folder tree in the sidebar.
/// </summary>
public partial class FolderTreeViewModel : ObservableObject
{
    private readonly IAccountStore _accountStore;

    [ObservableProperty]
    private ObservableCollection<FolderItem> _folders = new();

    [ObservableProperty]
    private FolderItem? _selectedFolder;

    [ObservableProperty]
    private Guid _accountId;

    public event EventHandler<Folder>? FolderSelected;

    public FolderTreeViewModel(IAccountStore accountStore)
    {
        _accountStore = accountStore;
    }

    [RelayCommand]
    public async Task LoadFoldersAsync(Guid accountId)
    {
        AccountId = accountId;

        try
        {
            var folders = await _accountStore.GetFoldersAsync(accountId);
            var items = folders.Select(f => new FolderItem
            {
                Folder = f,
                Name = f.RemoteName,
                Icon = GetFolderIcon(f.Type),
                UnreadCount = f.UnreadCount,
            }).ToList();

            // Sort: Inbox first, then by type, then alphabetically
            items = items
                .OrderBy(f => f.Folder.Type == FolderType.Inbox ? 0 :
                              f.Folder.Type == FolderType.Sent ? 1 :
                              f.Folder.Type == FolderType.Drafts ? 2 :
                              f.Folder.Type == FolderType.Trash ? 3 :
                              f.Folder.Type == FolderType.Archive ? 4 :
                              f.Folder.Type == FolderType.Spam ? 5 : 6)
                .ThenBy(f => f.Name)
                .ToList();

            Folders = new ObservableCollection<FolderItem>(items);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load folders: {ex.Message}");
        }
    }

    partial void OnSelectedFolderChanged(FolderItem? value)
    {
        if (value != null)
        {
            FolderSelected?.Invoke(this, value.Folder);
        }
    }

    // ZAI F-027: emoji replaced with text prefixes (folder name + simple label).
    // Real Lucide icons will land in v0.2 per the design system.
    private static string GetFolderIcon(FolderType type) => type switch
    {
        FolderType.Inbox => "[Inbox]",
        FolderType.Sent => "[Sent]",
        FolderType.Drafts => "[Drafts]",
        FolderType.Trash => "[Trash]",
        FolderType.Archive => "[Archive]",
        FolderType.Spam => "[Spam]",
        _ => "[Folder]",
    };
}

/// <summary>
/// Represents a folder in the tree view.
/// </summary>
public class FolderItem
{
    public Folder Folder { get; set; } = new();
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "[Folder]";
    public int UnreadCount { get; set; }
    public string DisplayName => UnreadCount > 0 ? $"{Icon} {Name} ({UnreadCount})" : $"{Icon} {Name}";
}
