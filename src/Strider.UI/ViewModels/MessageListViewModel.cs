using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Core.Search;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the message list panel.
/// </summary>
public partial class MessageListViewModel : ObservableObject
{
    private readonly IMessageStore _messageStore;
    private readonly IMailActionService _actions;
    private readonly IMailSyncService _sync;
    private readonly IFlagService? _flags;

    [ObservableProperty]
    private ObservableCollection<Message> _messages = new();

    [ObservableProperty]
    private Message? _selectedMessage;

    [ObservableProperty]
    private Guid _currentFolderId;

    [ObservableProperty]
    private string _currentFolderName = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _sortField = "date";

    [ObservableProperty]
    private bool _sortDescending = true;

    [ObservableProperty]
    private string _statusMessage = "";

    public MessageListViewModel(
        IMessageStore messageStore,
        IMailActionService actions,
        IMailSyncService sync,
        IFlagService? flags = null)
    {
        _messageStore = messageStore;
        _actions = actions;
        _sync = sync;
        _flags = flags;
    }

    /// <summary>Colours offered in the reply pane's flag palette (ТЗ Wave 6).</summary>
    public IReadOnlyList<FlagColor> FlagColorChoices => FlagColors.All;

    /// <summary>True while the colour palette under the toolbar is open.</summary>
    [ObservableProperty]
    private bool _isFlagPickerOpen;

    [RelayCommand]
    private void ToggleFlagPicker() => IsFlagPickerOpen = !IsFlagPickerOpen;

    /// <summary>
    /// Manual colour flag (Wave 6). A manual choice sets
    /// <see cref="Message.FlagColorIsAutomatic"/> to false, so later rule runs
    /// never overwrite it.
    /// </summary>
    [RelayCommand]
    private async Task SetFlagAsync(FlagColor color)
    {
        var message = SelectedMessage;
        if (message is null || _flags is null) return;

        try
        {
            await _flags.SetFlagAsync(message, color);
            RefreshRow(message);
            StatusMessage = color == FlagColor.None
                ? "Flag cleared"
                : $"{FlagColors.ToDisplayName(color)} flag set";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Setting the flag failed");
            StatusMessage = $"Flag failed: {ex.Message}";
        }
    }

    /// <summary>
    /// <see cref="Message"/> is a plain POCO, so replacing it in the collection is
    /// what makes the coloured bar and subject repaint immediately.
    /// </summary>
    private void RefreshRow(Message message)
    {
        var index = Messages.IndexOf(message);
        if (index >= 0) Messages[index] = message;
    }

    /// <summary>Account whose mail the list currently shows; set by the main window.</summary>
    public Guid CurrentAccountId { get; set; }

    /// <summary>When true the search box looks at every folder, not just the open one (F2.8).</summary>
    [ObservableProperty]
    private bool _searchAllFolders;

    /// <summary>True when the list currently shows search results rather than folder contents.</summary>
    [ObservableProperty]
    private bool _isSearchResult;

    [RelayCommand]
    public async Task LoadMessagesAsync(Guid folderId)
    {
        CurrentFolderId = folderId;
        IsSearchResult = false;
        IsLoading = true;

        try
        {
            var messages = await _messageStore.GetMessagesAsync(folderId, limit: 200);
            Messages = new ObservableCollection<Message>(messages);
            TotalCount = messages.Count;
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load messages");
            StatusMessage = $"Failed to load messages: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Runs the toolbar search (F2.8). Supports <c>from:</c>, <c>to:</c>,
    /// <c>subject:</c>, <c>body:</c>, <c>is:unread</c>, <c>is:starred</c>,
    /// <c>has:attachment</c> and free text; searches the bodies already cached
    /// locally. Empty query = back to the folder list.
    /// </summary>
    [RelayCommand]
    public async Task SearchAsync()
    {
        var query = MessageQuery.Parse(SearchQuery);

        if (query.IsEmpty)
        {
            IsSearchResult = false;
            await LoadMessagesAsync(CurrentFolderId);
            return;
        }

        IsLoading = true;
        try
        {
            var accountId = CurrentAccountId != Guid.Empty
                ? CurrentAccountId
                : (Messages.FirstOrDefault()?.AccountId ?? Guid.Empty);

            if (accountId == Guid.Empty)
            {
                StatusMessage = "Open a folder first.";
                return;
            }

            var folderId = SearchAllFolders ? (Guid?)null : CurrentFolderId;
            var results = await _messageStore.SearchMessagesAsync(accountId, folderId, query, limit: 500);

            Messages = new ObservableCollection<Message>(results);
            TotalCount = results.Count;
            IsSearchResult = true;
            StatusMessage = results.Count == 0
                ? $"Nothing matches {query}."
                : $"{results.Count} match(es) for {query}{(folderId is null ? " (all mail)" : "")}.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Search failed");
            StatusMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Leaves search mode and reloads the folder.</summary>
    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        SearchQuery = "";
        IsSearchResult = false;
        await LoadMessagesAsync(CurrentFolderId);
    }

    /// <summary>
    /// Marks a message read locally *and* on the server — the local-only version
    /// that shipped in Wave 3 made unread badges come back on the next sync (F-024).
    /// </summary>
    [RelayCommand]
    private async Task MarkAsReadAsync(Message message)
    {
        message.IsRead = true;
        await _messageStore.UpdateMessageAsync(message);

        try
        {
            await _sync.MarkAsReadOnServerAsync(message);
        }
        catch (Exception ex)
        {
            // Offline is fine: the next sync reconciles.
            Serilog.Log.Warning(ex, "Marking the message read on the server failed");
        }
    }

    [RelayCommand]
    private async Task ToggleStarAsync(Message message)
    {
        message.IsStarred = !message.IsStarred;
        await _messageStore.UpdateMessageAsync(message);
    }

    [RelayCommand]
    private Task ArchiveAsync(Message? message) =>
        RunServerActionAsync(message, m => _actions.ArchiveAsync(m), "Archived");

    [RelayCommand]
    private Task DeleteAsync(Message? message) =>
        RunServerActionAsync(message, m => _actions.TrashAsync(m), "Deleted");

    /// <summary>
    /// The message leaves the list only after the server confirmed the move,
    /// so a failed action never makes mail silently "disappear".
    /// </summary>
    private async Task RunServerActionAsync(Message? message, Func<Message, Task> action, string done)
    {
        message ??= SelectedMessage;
        if (message == null) return;
        try
        {
            await action(message);
            Messages.Remove(message);
            TotalCount = Messages.Count;
            if (ReferenceEquals(SelectedMessage, message)) SelectedMessage = null;
            StatusMessage = done;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Mail action failed");
            StatusMessage = $"Failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SortByDate()
    {
        if (SortField == "date")
            SortDescending = !SortDescending;
        else
            SortField = "date";

        ApplySorting();
    }

    [RelayCommand]
    private void SortBySender()
    {
        if (SortField == "sender")
            SortDescending = !SortDescending;
        else
            SortField = "sender";

        ApplySorting();
    }

    [RelayCommand]
    private void SortBySubject()
    {
        if (SortField == "subject")
            SortDescending = !SortDescending;
        else
            SortField = "subject";

        ApplySorting();
    }

    private void ApplySorting()
    {
        var sorted = SortField switch
        {
            "date" => SortDescending
                ? Messages.OrderByDescending(m => m.DateUtc).ToList()
                : Messages.OrderBy(m => m.DateUtc).ToList(),
            "sender" => SortDescending
                ? Messages.OrderByDescending(m => m.FromName ?? m.FromAddress).ToList()
                : Messages.OrderBy(m => m.FromName ?? m.FromAddress).ToList(),
            "subject" => SortDescending
                ? Messages.OrderByDescending(m => m.Subject).ToList()
                : Messages.OrderBy(m => m.Subject).ToList(),
            _ => Messages.ToList(),
        };

        Messages = new ObservableCollection<Message>(sorted);
    }
}
