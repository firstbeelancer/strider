using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Infrastructure.Mail;

namespace Strider.UI.ViewModels;

/// <summary>
/// Main window ViewModel - orchestrates the three-panel layout.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IAccountStore _accountStore;
    private readonly IMessageStore _messageStore;
    private readonly IEventBus _eventBus;
    private readonly IServiceProvider _services;

    [ObservableProperty]
    private ObservableCollection<Account> _accounts = new();

    [ObservableProperty]
    private Account? _currentAccount;

    [ObservableProperty]
    private ObservableCollection<FolderItem> _folders = new();

    [ObservableProperty]
    private FolderItem? _selectedFolder;

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
        IEventBus eventBus,
        IServiceProvider services,
        MessageListViewModel messageList,
        MessageReaderViewModel messageReader)
    {
        _accountStore = accountStore;
        _messageStore = messageStore;
        _eventBus = eventBus;
        _services = services;
        _messageList = messageList;
        _messageReader = messageReader;

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
            else
            {
                StatusText = "No accounts configured. Click 'Add Account' to get started.";
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
            var items = folders.Select(f => new FolderItem
            {
                Folder = f,
                Name = f.RemoteName,
                Icon = GetFolderIcon(f.Type),
                UnreadCount = f.UnreadCount,
            }).OrderBy(f => f.Folder.Type == FolderType.Inbox ? 0 :
                           f.Folder.Type == FolderType.Sent ? 1 :
                           f.Folder.Type == FolderType.Drafts ? 2 :
                           f.Folder.Type == FolderType.Trash ? 3 : 4)
              .ThenBy(f => f.Name)
              .ToList();

            Folders = new ObservableCollection<FolderItem>(items);

            var inbox = items.FirstOrDefault(i => i.Folder.Type == FolderType.Inbox);
            if (inbox != null)
            {
                SelectedFolder = inbox;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading folders: {ex.Message}";
        }
    }

    partial void OnSelectedFolderChanged(FolderItem? value)
    {
        if (value != null)
        {
            _ = SelectFolderAsync(value);
        }
    }

    private async Task SelectFolderAsync(FolderItem item)
    {
        await MessageList.LoadMessagesAsync(item.Folder.Id);
        StatusText = $"{item.Name}: {MessageList.TotalCount} messages";
    }

    [RelayCommand]
    private async Task SelectMessageAsync(Message message)
    {
        await MessageReader.LoadMessageAsync(message);
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (CurrentAccount == null || SelectedFolder == null) return;

        IsSyncing = true;
        StatusText = "Syncing...";

        try
        {
            await MessageList.LoadMessagesAsync(SelectedFolder.Folder.Id);
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
    }

    [RelayCommand]
    private void ComposeNew()
    {
        var vm = _services.GetRequiredService<ComposeViewModel>();
        vm.LoadAccountsCommand.Execute(null);
        var window = new Views.ComposeWindow
        {
            DataContext = vm,
        };
        window.Show();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = _services.GetRequiredService<SettingsViewModel>();
        vm.LoadCommand.Execute(null);
        var window = new Views.SettingsWindow
        {
            DataContext = vm,
        };
        window.Show();
    }

    [RelayCommand]
    private void AddAccount()
    {
        var vm = _services.GetRequiredService<AccountWizardViewModel>();
        var window = new Views.AccountWizardWindow
        {
            DataContext = vm,
        };
        window.Show();
    }

    private void OnNewMessage(NewMessageEvent e)
    {
        if (SelectedFolder?.Folder.Id == e.FolderId)
        {
            _ = MessageList.LoadMessagesAsync(e.FolderId);
        }
    }

    // ZAI F-027: emoji replaced with text labels. Lucide icons will land in v0.2.
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
