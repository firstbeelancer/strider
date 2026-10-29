using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the message list panel.
/// </summary>
public partial class MessageListViewModel : ObservableObject
{
    private readonly IMessageStore _messageStore;
    private readonly IMailActionService _actions;

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

    public MessageListViewModel(IMessageStore messageStore, IMailActionService actions)
    {
        _messageStore = messageStore;
        _actions = actions;
    }

    [RelayCommand]
    public async Task LoadMessagesAsync(Guid folderId)
    {
        CurrentFolderId = folderId;
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

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            await LoadMessagesAsync(CurrentFolderId);
            return;
        }

        IsLoading = true;

        try
        {
            var allMessages = await _messageStore.GetMessagesAsync(CurrentFolderId, limit: 1000);
            var filtered = allMessages.Where(m =>
                m.Subject.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                m.FromAddress.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                (m.FromName?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) == true)
            ).ToList();

            Messages = new ObservableCollection<Message>(filtered);
            TotalCount = filtered.Count;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task MarkAsReadAsync(Message message)
    {
        message.IsRead = true;
        await _messageStore.UpdateMessageAsync(message);
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
