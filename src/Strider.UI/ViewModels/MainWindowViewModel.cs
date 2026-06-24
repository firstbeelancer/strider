using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.UI.ViewModels;

/// <summary>
/// Main window ViewModel - orchestrates the three-panel layout.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IAccountStore _accountStore;
    private readonly IMessageStore _messageStore;
    private readonly IImapGateway _imapGateway;
    private readonly ISmtpGateway _smtpGateway;
    private readonly IEventBus _eventBus;

    [ObservableProperty]
    private ObservableCollection<Account> _accounts = new();

    [ObservableProperty]
    private Account? _currentAccount;

    [ObservableProperty]
    private ObservableCollection<Folder> _folders = new();

    [ObservableProperty]
    private Folder? _currentFolder;

    [ObservableProperty]
    private MessageListViewModel _messageList;

    [ObservableProperty]
    private MessageReaderViewModel _messageReader;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isDarkTheme;

    public MainWindowViewModel(
        IAccountStore accountStore,
        IMessageStore messageStore,
        IImapGateway imapGateway,
        ISmtpGateway smtpGateway,
        IEventBus eventBus,
        MessageListViewModel messageList,
        MessageReaderViewModel messageReader)
    {
        _accountStore = accountStore;
        _messageStore = messageStore;
        _imapGateway = imapGateway;
        _smtpGateway = smtpGateway;
        _eventBus = eventBus;
        _messageList = messageList;
        _messageReader = messageReader;

        // Subscribe to events
        _eventBus.Subscribe<NewMessageEvent>(OnNewMessage);
    }

    [RelayCommand]
    private async Task LoadAccountsAsync()
    {
        try
        {
            var accounts = await _accountStore.GetAllAccountsAsync();
            Accounts = new ObservableCollection<Account>(accounts);

            if (Accounts.Count > 0)
            {
                CurrentAccount = Accounts[0];
                await LoadFoldersAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading accounts: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadFoldersAsync()
    {
        if (CurrentAccount == null) return;

        try
        {
            var folders = await _accountStore.GetFoldersAsync(CurrentAccount.Id);
            Folders = new ObservableCollection<Folder>(folders);

            // Auto-select inbox
            var inbox = folders.FirstOrDefault(f => f.Type == FolderType.Inbox);
            if (inbox != null)
            {
                CurrentFolder = inbox;
                await SelectFolderAsync(inbox);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading folders: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SelectFolderAsync(Folder folder)
    {
        CurrentFolder = folder;
        await MessageList.LoadMessagesAsync(folder.Id);
        StatusText = $"{folder.RemoteName}: {MessageList.TotalCount} messages";
    }

    [RelayCommand]
    private async Task SelectMessageAsync(Message message)
    {
        await MessageReader.LoadMessageAsync(message);
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (CurrentAccount == null || CurrentFolder == null) return;

        IsSyncing = true;
        StatusText = "Syncing...";

        try
        {
            // TODO: Implement actual IMAP sync
            // var messages = await _imapGateway.FetchMessagesAsync(CurrentAccount, CurrentFolder.RemoteName, ...);
            // await _messageStore.SaveMessagesAsync(messages);

            await MessageList.LoadMessagesAsync(CurrentFolder.Id);
            StatusText = $"Synced: {MessageList.TotalCount} messages";
        }
        catch (Exception ex)
        {
            StatusText = $"Sync error: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        // TODO: Apply theme change
    }

    [RelayCommand]
    private void ComposeNew()
    {
        // TODO: Open compose window
        StatusText = "Compose new message";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        // TODO: Open settings window
        StatusText = "Settings";
    }

    [RelayCommand]
    private void AddAccount()
    {
        // TODO: Open account wizard
        StatusText = "Add account";
    }

    private void OnNewMessage(NewMessageEvent e)
    {
        // Refresh message list if we're viewing the affected folder
        if (CurrentFolder?.Id == e.FolderId)
        {
            MessageList.LoadMessagesAsync(e.FolderId).ConfigureAwait(false);
        }
    }
}
