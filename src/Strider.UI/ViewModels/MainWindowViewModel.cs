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
    private readonly IMailSyncService _sync;

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
        IMailSyncService sync,
        MessageListViewModel messageList,
        MessageReaderViewModel messageReader)
    {
        _accountStore = accountStore;
        _messageStore = messageStore;
        _eventBus = eventBus;
        _services = services;
        _sync = sync;
        _messageList = messageList;
        _messageReader = messageReader;

        // Selecting a message in the list opens it in the reader.
        _messageList.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MessageListViewModel.SelectedMessage)) return;
            if (_messageList.SelectedMessage is { } m) _ = SelectMessageAsync(m);
            else _messageReader.Clear();
        };
        _messageReader.ComposeRequested += (msg, mode) => _ = OpenComposeAsync(msg, mode);

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
                CurrentAccount ??= Accounts[0];
                await LoadFoldersAsync();
                // Pull new mail right after start-up.
                await SyncAsync();
            }
            else
            {
                StatusText = "No accounts yet.";
                // First run: go straight to setup instead of an empty frame.
                if (!_firstRunWizardShown)
                {
                    _firstRunWizardShown = true;
                    Avalonia.Threading.Dispatcher.UIThread.Post(AddAccount, Avalonia.Threading.DispatcherPriority.Background);
                }
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
            var items = BuildFolderItems(folders);

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

    private static List<FolderItem> BuildFolderItems(IEnumerable<Folder> folders) =>
        folders.Select(f => new FolderItem
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

    partial void OnSelectedFolderChanged(FolderItem? value)
    {
        if (value != null && !_suppressFolderLoad)
        {
            _ = SelectFolderAsync(value);
        }
    }

    private async Task SelectFolderAsync(FolderItem item)
    {
        MessageReader.Clear();
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
            var folderId = SelectedFolder.Folder.Id;
            var isInbox = SelectedFolder.Folder.Type == FolderType.Inbox;
            // Account sync refreshes the folder list and the Inbox;
            // any other open folder is synced on its own.
            var added = await _sync.SyncAccountAsync(CurrentAccount.Id);
            if (!isInbox) added += await _sync.SyncFolderAsync(CurrentAccount.Id, folderId);

            await RefreshFolderListAsync(folderId);
            await MessageList.LoadMessagesAsync(folderId);
            StatusText = added > 0 ? $"{added} new message(s)" : "Up to date";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Sync failed");
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
    private Task ComposeNewAsync() => OpenComposeAsync(null, ComposeMode.New);

    private async Task OpenComposeAsync(Message? original, ComposeMode mode)
    {
        try
        {
            var vm = _services.GetRequiredService<ComposeViewModel>();
            if (original == null) await vm.LoadAccountsAsync();
            else await vm.LoadFromAsync(original, mode);
            ShowChild(new Views.ComposeWindow { DataContext = vm }, modal: false);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to open composer");
            StatusText = $"Could not open composer: {ex.Message}";
        }
    }

    /// <summary>Reloads folder names/unread badges, keeping the current selection.</summary>
    private async Task RefreshFolderListAsync(Guid keepSelected)
    {
        if (CurrentAccount == null) return;
        var folders = await _accountStore.GetFoldersAsync(CurrentAccount.Id);
        var items = BuildFolderItems(folders);
        _suppressFolderLoad = true;
        Folders = new ObservableCollection<FolderItem>(items);
        SelectedFolder = items.FirstOrDefault(i => i.Folder.Id == keepSelected);
        _suppressFolderLoad = false;
    }

    private bool _suppressFolderLoad;
    private bool _firstRunWizardShown;

    /// <summary>The main window, used as owner so dialogs open centred and on top of it.</summary>
    private static Avalonia.Controls.Window? MainWindowOwner =>
        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>Opens a child window: modal over the main window when there is one, otherwise free-standing.</summary>
    private static void ShowChild(Avalonia.Controls.Window window, bool modal)
    {
        var owner = MainWindowOwner;
        if (owner is { IsVisible: true })
        {
            if (modal) _ = window.ShowDialog(owner);
            else window.Show(owner);
        }
        else
        {
            window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen;
            window.Show();
        }
    }

    public bool HasNoAccounts => Accounts.Count == 0;

    partial void OnAccountsChanged(ObservableCollection<Account> value) => OnPropertyChanged(nameof(HasNoAccounts));

    private bool _wizardOpen;

    /// <summary>True while the account wizard is on screen (also lets UI tests see that a click opened it).</summary>
    public bool IsAccountWizardOpen => _wizardOpen;

    [RelayCommand]
    private void OpenSettings()
    {
        try
        {
            var vm = _services.GetRequiredService<SettingsViewModel>();
            vm.LoadCommand.Execute(null);
            ShowChild(new Views.SettingsWindow { DataContext = vm }, modal: false);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to open settings");
            StatusText = $"Could not open settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddAccount()
    {
        if (_wizardOpen) return;
        try
        {
            var vm = _services.GetRequiredService<AccountWizardViewModel>();
            var window = new Views.AccountWizardWindow { DataContext = vm };
            _wizardOpen = true;
            // Pick up the new account (and its mail) once the wizard closes.
            window.Closed += (_, _) =>
            {
                _wizardOpen = false;
                _ = LoadAccountsCommand.ExecuteAsync(null);
            };
            ShowChild(window, modal: true);
            Serilog.Log.Information("Account wizard opened");
        }
        catch (Exception ex)
        {
            _wizardOpen = false;
            Serilog.Log.Error(ex, "Failed to open account wizard");
            StatusText = $"Could not open the account wizard: {ex.Message}";
        }
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
