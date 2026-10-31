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

    /// <summary>Rows loaded per page; older mail comes with "Load more".</summary>
    public const int PageSize = 200;

    private int _loadVersion;

    /// <summary>
    /// True while the rows are being rebuilt with the selection kept: the list control
    /// briefly reports "nothing selected", which must not close the open message.
    /// </summary>
    public bool IsPreservingSelection { get; private set; }

    /// <summary>The selected row was rebuilt (new instance) or is gone (null) after a refresh.</summary>
    public event Action<Message?>? SelectionReplaced;

    /// <summary>A read state changed: the unread badges are out of date.</summary>
    public event Action? UnreadChanged;

    /// <summary>True when the folder holds more mail than is loaded.</summary>
    [ObservableProperty]
    private bool _hasMore;

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
        var sameFolder = folderId == CurrentFolderId && !IsSearchResult;
        var count = sameFolder ? Math.Max(Messages.Count, PageSize) : PageSize;
        var keepId = sameFolder ? SelectedMessage?.Id : null;

        CurrentFolderId = folderId;
        IsSearchResult = false;
        IsLoading = true;
        var version = ++_loadVersion;

        try
        {
            var messages = await _messageStore.GetMessagesAsync(folderId, limit: count);
            var total = await _messageStore.GetMessageCountAsync(folderId);
            // The user moved on to another folder while this one was loading.
            if (version != _loadVersion) return;

            ReplaceRows(Sorted(messages), keepId);
            TotalCount = total;
            HasMore = messages.Count < total;
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load messages");
            StatusMessage = $"Failed to load messages: {ex.Message}";
        }
        finally
        {
            if (version == _loadVersion) IsLoading = false;
        }
    }

    /// <summary>Re-reads the open folder after a sync. Search results stay on screen.</summary>
    public Task RefreshAsync() =>
        IsSearchResult || CurrentFolderId == Guid.Empty ? Task.CompletedTask : LoadMessagesAsync(CurrentFolderId);

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsSearchResult) return;
        var version = ++_loadVersion;
        try
        {
            var page = await _messageStore.GetMessagesAsync(CurrentFolderId, limit: PageSize, offset: Messages.Count);
            if (version != _loadVersion) return;

            var known = Messages.Select(m => m.Id).ToHashSet();
            var rows = Messages.Concat(page.Where(m => known.Add(m.Id))).ToList();
            ReplaceRows(Sorted(rows), SelectedMessage?.Id);
            HasMore = page.Count == PageSize && rows.Count < TotalCount;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load more messages");
            StatusMessage = $"Failed to load messages: {ex.Message}";
        }
    }

    private void ReplaceRows(IReadOnlyList<Message> rows, Guid? keepId)
    {
        IsPreservingSelection = true;
        Message? kept;
        try
        {
            kept = keepId is { } id ? rows.FirstOrDefault(m => m.Id == id) : null;
            Messages = new ObservableCollection<Message>(rows);
            SelectedMessage = kept;
        }
        finally
        {
            IsPreservingSelection = false;
        }

        if (keepId is not null) SelectionReplaced?.Invoke(kept);
    }

    /// <summary>Opens the next (+1) or previous (-1) row (keys J and K).</summary>
    public void MoveSelection(int step)
    {
        if (Messages.Count == 0) return;
        var index = SelectedMessage is null ? -1 : Messages.IndexOf(SelectedMessage);
        var next = index < 0 ? 0 : Math.Clamp(index + step, 0, Messages.Count - 1);
        SelectedMessage = Messages[next];
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

            ReplaceRows(Sorted(results), null);
            TotalCount = results.Count;
            HasMore = false;
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
        UnreadChanged?.Invoke();

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

    /// <summary>
    /// Star and read state live on the server (\\Flagged, \\Seen). The row reacts at
    /// once and goes back if the server refuses, so what is shown never drifts from it.
    /// </summary>
    [RelayCommand]
    private async Task ToggleStarAsync(Message? message)
    {
        message ??= SelectedMessage;
        if (message is null) return;

        var wanted = !message.IsStarred;
        message.IsStarred = wanted;
        try
        {
            await _actions.SetStarredAsync(message, wanted);
        }
        catch (Exception ex)
        {
            message.IsStarred = !wanted;
            Serilog.Log.Warning(ex, "Changing the star failed");
            StatusMessage = $"Star not saved: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleReadAsync(Message? message)
    {
        message ??= SelectedMessage;
        if (message is null) return;

        var wanted = !message.IsRead;
        message.IsRead = wanted;
        UnreadChanged?.Invoke();
        try
        {
            await _actions.SetReadAsync(message, wanted);
        }
        catch (Exception ex)
        {
            message.IsRead = !wanted;
            UnreadChanged?.Invoke();
            Serilog.Log.Warning(ex, "Changing the read state failed");
            StatusMessage = $"Read state not saved: {ex.Message}";
        }
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
            // By id: the reader may hold an instance from before the last refresh.
            var id = message.Id;
            var row = Messages.FirstOrDefault(m => m.Id == id);
            var index = row is null ? -1 : Messages.IndexOf(row);
            var wasSelected = SelectedMessage?.Id == id;
            if (row is not null) Messages.Remove(row);
            TotalCount = Math.Max(0, TotalCount - 1);
            // Go on to the neighbour, as mail clients do; an empty folder closes the reader.
            if (wasSelected)
                SelectedMessage = Messages.Count == 0 ? null : Messages[Math.Clamp(index, 0, Messages.Count - 1)];
            if (!message.IsRead) UnreadChanged?.Invoke();
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

    [RelayCommand]
    private void SortBySize()
    {
        if (SortField == "size")
            SortDescending = !SortDescending;
        else
            SortField = "size";

        ApplySorting();
    }

    private void ApplySorting() => ReplaceRows(Sorted(Messages), SelectedMessage?.Id);

    /// <summary>Applies the current sort; refreshes go through here so a sync does not reset it.</summary>
    private List<Message> Sorted(IEnumerable<Message> rows)
    {
        var text = StringComparer.CurrentCultureIgnoreCase;
        return (SortField, SortDescending) switch
        {
            ("sender", true) => rows.OrderByDescending(m => m.FromName ?? m.FromAddress, text).ToList(),
            ("sender", false) => rows.OrderBy(m => m.FromName ?? m.FromAddress, text).ToList(),
            ("subject", true) => rows.OrderByDescending(m => m.Subject, text).ToList(),
            ("subject", false) => rows.OrderBy(m => m.Subject, text).ToList(),
            ("size", true) => rows.OrderByDescending(m => m.Size).ToList(),
            ("size", false) => rows.OrderBy(m => m.Size).ToList(),
            (_, false) => rows.OrderBy(m => m.DateUtc).ToList(),
            _ => rows.OrderByDescending(m => m.DateUtc).ToList(),
        };
    }
}
