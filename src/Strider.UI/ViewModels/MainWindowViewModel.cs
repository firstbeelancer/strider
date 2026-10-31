using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Infrastructure.Mail;
using Strider.Infrastructure.Services;

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
    private readonly IAppSettingsStore _settings;
    private readonly BackgroundSyncService _background;

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
        MessageReaderViewModel messageReader,
        IAppSettingsStore settings,
        BackgroundSyncService background)
    {
        _accountStore = accountStore;
        _messageStore = messageStore;
        _eventBus = eventBus;
        _services = services;
        _sync = sync;
        _settings = settings;
        _background = background;
        _messageList = messageList;
        _messageReader = messageReader;

        IsDarkTheme = Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;

        // Selecting a message in the list opens it in the reader.
        _messageList.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MessageListViewModel.SelectedMessage)) return;
            if (_messageList.IsPreservingSelection) return;
            if (_messageList.SelectedMessage is { } m) _ = SelectMessageAsync(m);
            else _messageReader.Clear();
        };
        _messageList.SelectionReplaced += m =>
        {
            if (m is null) _messageReader.Clear();
            else _messageReader.Rebind(m);
        };
        _messageList.UnreadChanged += () => _ = RefreshBadgesAsync();
        _messageReader.UnreadChanged += () => _ = RefreshBadgesAsync();
        _messageReader.ComposeRequested += (msg, mode) => _ = OpenComposeAsync(msg, mode);
        _background.Synced += OnBackgroundSynced;

        _eventBus.Subscribe<NewMessageEvent>(OnNewMessage);
    }

    [RelayCommand]
    private async Task LoadAccountsAsync()
    {
        try
        {
            var accounts = await _accountStore.GetAllAccountsAsync();
            var currentId = CurrentAccount?.Id;
            _loadingAccounts = true;
            try
            {
                Accounts = new ObservableCollection<Account>(accounts);
                // Fresh instances: the account switcher only knows the ones in the new list.
                CurrentAccount = Accounts.FirstOrDefault(a => a.Id == currentId) ?? Accounts.FirstOrDefault();
            }
            finally
            {
                _loadingAccounts = false;
            }

            if (Accounts.Count > 0)
            {
                MessageList.CurrentAccountId = CurrentAccount!.Id;
                _skipNextFolderSync = true;
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
        MessageReader.CanEditDraft = item.Folder.Type == FolderType.Drafts;
        await MessageList.LoadMessagesAsync(item.Folder.Id);
        StatusText = $"{item.Name}: {MessageList.TotalCount} messages";

        // The cache shows at once; only the Inbox is synced in the background, so any
        // other folder would stay empty until "Sync" was pressed by hand.
        var skip = _skipNextFolderSync;
        _skipNextFolderSync = false;
        if (!skip && CurrentAccount is { } account) await SyncOpenFolderAsync(account.Id, item);
    }

    private async Task SyncOpenFolderAsync(Guid accountId, FolderItem item)
    {
        BeginSync();
        try
        {
            var added = await _sync.SyncFolderAsync(accountId, item.Folder.Id);
            if (SelectedFolder?.Folder.Id != item.Folder.Id) return;

            await MessageList.RefreshAsync();
            await RefreshBadgesAsync();
            if (added > 0) StatusText = $"{item.Name}: {added} new message(s)";
        }
        catch (Exception ex)
        {
            // Offline is fine: the cached mail is already on screen.
            Serilog.Log.Warning(ex, "Syncing the open folder failed");
            if (SelectedFolder?.Folder.Id == item.Folder.Id) StatusText = $"Showing saved mail: {ex.Message}";
        }
        finally
        {
            EndSync();
        }
    }

    /// <summary>Recounts the unread badges from the cache; the row stays selected.</summary>
    private async Task RefreshBadgesAsync()
    {
        try
        {
            foreach (var item in Folders.ToList())
                item.UnreadCount = await _messageStore.GetUnreadCountAsync(item.Folder.Id);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Refreshing unread badges failed");
        }
    }

    private void OnBackgroundSynced(Guid accountId, int added) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            if (CurrentAccount?.Id != accountId) return;
            await RefreshBadgesAsync();
            if (SelectedFolder?.Folder.Type == FolderType.Inbox) await MessageList.RefreshAsync();
            if (added > 0) StatusText = $"{added} new message(s)";
        });

    /// <summary>Applies the saved theme and starts background sync (start-up, after the database is ready).</summary>
    public async Task ApplyPreferencesAsync()
    {
        var general = await _settings.GetGeneralAsync();
        ApplyThemeName(general.Theme);
        IsDarkTheme = Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        _background.Start(TimeSpan.FromMinutes(general.SyncIntervalMinutes));
    }

    private int _activeSyncs;

    private void BeginSync()
    {
        _activeSyncs++;
        IsSyncing = true;
    }

    private void EndSync()
    {
        _activeSyncs--;
        IsSyncing = _activeSyncs > 0;
    }

    [RelayCommand]
    private async Task SelectMessageAsync(Message message)
    {
        await MessageReader.LoadMessageAsync(message);
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (CurrentAccount == null) return;

        BeginSync();
        StatusText = "Syncing...";

        try
        {
            var folder = SelectedFolder?.Folder;
            // Account sync refreshes the folder list and the Inbox;
            // any other open folder is synced on its own.
            var added = await _sync.SyncAccountAsync(CurrentAccount.Id);
            if (folder is { Type: not FolderType.Inbox })
                added += await _sync.SyncFolderAsync(CurrentAccount.Id, folder.Id);

            if (folder is null)
            {
                // A new account whose folders only just arrived.
                await LoadFoldersAsync();
            }
            else
            {
                await RefreshFolderListAsync(folder.Id);
                await MessageList.RefreshAsync();
            }
            StatusText = added > 0 ? $"{added} new message(s)" : "Up to date";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Sync failed");
            StatusText = $"Sync error: {ex.Message}";
        }
        finally
        {
            EndSync();
        }
    }

    /// <summary>
    /// Switches between the dark and the light Fluent theme.
    /// Before Wave 5 this flipped a property nothing was bound to — the button
    /// did nothing at all.
    /// </summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplyTheme(IsDarkTheme);
        _ = SaveThemeAsync(IsDarkTheme ? "dark" : "light");
    }

    private async Task SaveThemeAsync(string theme)
    {
        try
        {
            var general = await _settings.GetGeneralAsync();
            general.Theme = theme;
            await _settings.SaveGeneralAsync(general);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Saving the theme failed");
        }
    }

    /// <summary>Applies "dark", "light" or "system".</summary>
    internal static void ApplyThemeName(string? theme)
    {
        if (Avalonia.Application.Current is not { } app) return;
        app.RequestedThemeVariant = theme switch
        {
            "dark" => Avalonia.Styling.ThemeVariant.Dark,
            "light" => Avalonia.Styling.ThemeVariant.Light,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };
    }

    /// <summary>Applies the theme variant process-wide (Avalonia 12 API).</summary>
    internal static void ApplyTheme(bool dark)
    {
        if (Avalonia.Application.Current is { } app)
        {
            app.RequestedThemeVariant = dark
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light;
        }
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
    private bool _loadingAccounts;
    private bool _skipNextFolderSync;

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

    partial void OnCurrentAccountChanged(Account? value)
    {
        if (value == null) return;
        MessageList.CurrentAccountId = value.Id;
        if (!_loadingAccounts) _ = SwitchAccountAsync();
    }

    /// <summary>The sidebar switcher (F1.5): shows the other mailbox and pulls its new mail.</summary>
    private async Task SwitchAccountAsync()
    {
        MessageReader.Clear();
        _skipNextFolderSync = true;
        await LoadFoldersAsync();
        await SyncAsync();
    }

    /// <summary>Raised by Ctrl+K and "/": the window moves focus into the search box.</summary>
    public event Action? FocusSearchRequested;

    /// <summary>
    /// Keyboard shortcuts (ТЗ §12). Returns true when the key was used. Single letters
    /// only work outside text fields, where they would otherwise be typed.
    /// </summary>
    public bool HandleShortcut(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers, bool typing)
    {
        const Avalonia.Input.KeyModifiers ctrl = Avalonia.Input.KeyModifiers.Control;
        const Avalonia.Input.KeyModifiers shift = Avalonia.Input.KeyModifiers.Shift;

        if (modifiers == ctrl)
        {
            switch (key)
            {
                case Avalonia.Input.Key.N: return Run(ComposeNewCommand);
                case Avalonia.Input.Key.K: FocusSearchRequested?.Invoke(); return true;
                case Avalonia.Input.Key.OemComma: return Run(OpenSettingsCommand);
                case Avalonia.Input.Key.OemPeriod: return Run(MessageReader.SummarizeCommand);
                case >= Avalonia.Input.Key.D1 and <= Avalonia.Input.Key.D9:
                    var index = key - Avalonia.Input.Key.D1;
                    if (index < Accounts.Count) CurrentAccount = Accounts[index];
                    return true;
            }
            return false;
        }

        if (typing) return false;

        if (modifiers == shift)
        {
            return key switch
            {
                Avalonia.Input.Key.R => Run(MessageReader.ReplyAllCommand),
                Avalonia.Input.Key.D3 => Run(MessageList.DeleteCommand),
                _ => false,
            };
        }

        if (modifiers != Avalonia.Input.KeyModifiers.None) return false;

        switch (key)
        {
            case Avalonia.Input.Key.C: return Run(ComposeNewCommand);
            case Avalonia.Input.Key.Oem2: FocusSearchRequested?.Invoke(); return true;
            case Avalonia.Input.Key.J: MessageList.MoveSelection(1); return true;
            case Avalonia.Input.Key.K: MessageList.MoveSelection(-1); return true;
            case Avalonia.Input.Key.R: return Run(MessageReader.ReplyCommand);
            case Avalonia.Input.Key.A: return Run(MessageReader.ReplyAllCommand);
            case Avalonia.Input.Key.F: return Run(MessageReader.ForwardCommand);
            case Avalonia.Input.Key.U: return Run(MessageList.ToggleReadCommand);
            case Avalonia.Input.Key.S: return Run(MessageList.ToggleStarCommand);
            case Avalonia.Input.Key.E: return Run(MessageList.ArchiveCommand);
            case Avalonia.Input.Key.Delete: return Run(MessageList.DeleteCommand);
            default: return false;
        }
    }

    private static bool Run(System.Windows.Input.ICommand command)
    {
        if (!command.CanExecute(null)) return false;
        command.Execute(null);
        return true;
    }

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

    /// <summary>Opens the calendar window (F6.10).</summary>
    [RelayCommand]
    private void OpenCalendar()
    {
        try
        {
            var vm = _services.GetRequiredService<CalendarViewModel>();
            var window = new Views.CalendarWindow { DataContext = vm };
            ShowChild(window, modal: false);
            _ = vm.LoadMonthAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to open the calendar");
            StatusText = $"Could not open the calendar: {ex.Message}";
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
