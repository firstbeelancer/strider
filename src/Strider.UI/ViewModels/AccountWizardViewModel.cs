using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Infrastructure.Mail;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the Account Wizard dialog.
/// Guides user through adding a new email account.
/// </summary>
public partial class AccountWizardViewModel : ObservableObject
{
    private readonly IAccountStore _accountStore;
    private readonly IImapGatewayFactory _imapGatewayFactory;
    private readonly ISmtpGateway _smtpGateway;
    private readonly IKeychainService _keychainService;

    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _imapHost = "";

    [ObservableProperty]
    private int _imapPort = 993;

    [ObservableProperty]
    private bool _imapUseSsl = true;

    [ObservableProperty]
    private string _smtpHost = "";

    [ObservableProperty]
    private int _smtpPort = 587;

    [ObservableProperty]
    private bool _smtpUseSsl = true;

    [ObservableProperty]
    private bool _isAutoDetecting;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private int _currentStep; // 0: email, 1: server settings, 2: testing

    // Known provider settings
    private static readonly Dictionary<string, (string imap, int imapPort, string smtp, int smtpPort)> KnownProviders = new()
    {
        ["gmail.com"] = ("imap.gmail.com", 993, "smtp.gmail.com", 587),
        ["outlook.com"] = ("outlook.office365.com", 993, "smtp.office365.com", 587),
        ["hotmail.com"] = ("outlook.office365.com", 993, "smtp.office365.com", 587),
        ["yahoo.com"] = ("imap.mail.yahoo.com", 993, "smtp.mail.yahoo.com", 587),
        ["yandex.ru"] = ("imap.yandex.ru", 993, "smtp.yandex.ru", 587),
        ["mail.ru"] = ("imap.mail.ru", 993, "smtp.mail.ru", 587),
        ["icloud.com"] = ("imap.mail.me.com", 993, "smtp.mail.me.com", 587),
        ["rambler.ru"] = ("imap.rambler.ru", 993, "smtp.rambler.ru", 587),
    };

    public AccountWizardViewModel(
        IAccountStore accountStore,
        IImapGatewayFactory imapGatewayFactory,
        ISmtpGateway smtpGateway,
        IKeychainService keychainService)
    {
        _accountStore = accountStore;
        _imapGatewayFactory = imapGatewayFactory;
        _smtpGateway = smtpGateway;
        _keychainService = keychainService;
    }

    [RelayCommand]
    private void AutoDetectSettings()
    {
        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@'))
        {
            StatusMessage = "Enter a valid email address";
            IsSuccess = false;
            return;
        }

        var domain = Email.Split('@')[1].ToLowerInvariant();

        if (KnownProviders.TryGetValue(domain, out var settings))
        {
            ImapHost = settings.imap;
            ImapPort = settings.imapPort;
            SmtpHost = settings.smtp;
            SmtpPort = settings.smtpPort;
            StatusMessage = $"Auto-detected settings for {domain}";
            IsSuccess = true;
        }
        else
        {
            // Try common patterns
            ImapHost = $"imap.{domain}";
            ImapPort = 993;
            SmtpHost = $"smtp.{domain}";
            SmtpPort = 587;
            StatusMessage = $"Using common patterns for {domain}. Verify settings.";
            IsSuccess = true;
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = Email.Split('@')[0];
        }

        CurrentStep = 1;
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            StatusMessage = "Email and password are required";
            IsSuccess = false;
            return;
        }

        IsTesting = true;
        StatusMessage = "Testing IMAP connection...";

        try
        {
            // For test purposes we use a temporary GUID. The real account ID
            // will be assigned when SaveAccountAsync is called. After test,
            // we release the temporary gateway so it doesn't linger.
            var tempAccountId = Guid.NewGuid();
            var account = new Account
            {
                Id = tempAccountId,
                Email = Email,
                DisplayName = DisplayName,
                ImapHost = ImapHost,
                ImapPort = ImapPort,
                ImapUseSsl = ImapUseSsl,
                SmtpHost = SmtpHost,
                SmtpPort = SmtpPort,
                SmtpUseSsl = SmtpUseSsl,
            };

            // Persist the password to keychain under the temp id so the gateway
            // (which reads from keychain) can authenticate. We'll move it to the
            // real id in SaveAccountAsync.
            await _keychainService.SetSecretAsync(
                KeychainKeys.Password(tempAccountId), Password);

            try
            {
                // Test IMAP via per-account gateway from factory
                var imap = _imapGatewayFactory.ForAccount(tempAccountId);
                await imap.ConnectAsync(account);
                var folders = await imap.GetFoldersAsync(account);
                await imap.DisconnectAsync();

                StatusMessage = $"IMAP OK. Found {folders.Count} folders. Testing SMTP...";

                // Test SMTP
                await _smtpGateway.ConnectAsync(account);
                await _smtpGateway.DisconnectAsync();
            }
            finally
            {
                // Always clean up temp gateway + temp keychain entry
                _imapGatewayFactory.Release(tempAccountId);
                await _keychainService.DeleteSecretAsync(KeychainKeys.Password(tempAccountId));
            }

            StatusMessage = "Connection successful!";
            IsSuccess = true;
            CurrentStep = 2;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            IsSuccess = false;
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task SaveAccountAsync()
    {
        try
        {
            var account = new Account
            {
                Email = Email,
                DisplayName = DisplayName,
                ImapHost = ImapHost,
                ImapPort = ImapPort,
                ImapUseSsl = ImapUseSsl,
                SmtpHost = SmtpHost,
                SmtpPort = SmtpPort,
                SmtpUseSsl = SmtpUseSsl,
            };

            // Save account to database
            await _accountStore.SaveAccountAsync(account);

            // Save password to keychain under canonical key
            await _keychainService.SetSecretAsync(
                KeychainKeys.Password(account.Id), Password);

            // F-023: Fetch real folders from IMAP server instead of hardcoding
            // INBOX/Sent/Drafts/Trash. Different providers use different folder
            // names (e.g., Gmail: [Gmail]/Sent Mail, Yahoo: Sent, Outlook: Sent).
            var fetchedFolders = await FetchRealFoldersFromImapAsync(account);
            await _accountStore.SaveFoldersAsync(fetchedFolders);

            StatusMessage = $"Account saved successfully! Loaded {fetchedFolders.Count} folders.";
            IsSuccess = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save: {ex.Message}";
            IsSuccess = false;
        }
    }

    /// <summary>
    /// Connects to the IMAP server and fetches the real folder list.
    /// Maps well-known folder names (INBOX, Sent, Drafts, Trash, Junk, Archive)
    /// to FolderType enum; everything else becomes FolderType.Custom.
    /// Falls back to a minimal hardcoded set if IMAP fetch fails (offline, etc.).
    /// </summary>
    private async Task<IReadOnlyList<Folder>> FetchRealFoldersFromImapAsync(Account account)
    {
        try
        {
            // Use the per-account gateway factory to get a fresh IMAP connection
            var imap = _imapGatewayFactory.ForAccount(account.Id);
            try
            {
                await imap.ConnectAsync(account);
                var remoteFolders = await imap.GetFoldersAsync(account);
                await imap.DisconnectAsync();

                // Map remote folder names to FolderType
                var folders = new List<Folder>();
                foreach (var rf in remoteFolders)
                {
                    rf.AccountId = account.Id;
                    rf.Type = ClassifyFolder(rf.RemoteName);
                    folders.Add(rf);
                }
                return folders;
            }
            finally
            {
                _imapGatewayFactory.Release(account.Id);
            }
        }
        catch
        {
            // Fallback: couldn't fetch from IMAP (offline, auth issue, etc.)
            // Create a minimal default set so the user sees something.
            return new[]
            {
                new Folder { AccountId = account.Id, RemoteName = "INBOX", Type = FolderType.Inbox },
                new Folder { AccountId = account.Id, RemoteName = "Sent", Type = FolderType.Sent },
                new Folder { AccountId = account.Id, RemoteName = "Drafts", Type = FolderType.Drafts },
                new Folder { AccountId = account.Id, RemoteName = "Trash", Type = FolderType.Trash },
            };
        }
    }

    /// <summary>
    /// Maps a remote folder name (case-insensitive) to a FolderType.
    /// Handles common variations across providers (Gmail, Outlook, Yahoo, etc.).
    /// </summary>
    private static FolderType ClassifyFolder(string remoteName)
    {
        if (string.IsNullOrEmpty(remoteName)) return FolderType.Custom;
        var name = remoteName.ToLowerInvariant().Trim('[', ']', ' ');

        if (name == "inbox") return FolderType.Inbox;

        // Sent — many variants across providers
        if (name == "sent" || name == "sent mail" || name == "sent items" ||
            name == "sentmail" || name.Contains("gmail/sent") || name == "&bcc;sented")
            return FolderType.Sent;

        // Drafts
        if (name == "drafts" || name == "draft" || name.Contains("gmail/drafts"))
            return FolderType.Drafts;

        // Trash / Deleted
        if (name == "trash" || name == "deleted" || name == "deleted items" ||
            name == "bin" || name.Contains("gmail/trash") || name == "&bcm-abpf-")
            return FolderType.Trash;

        // Spam / Junk
        if (name == "spam" || name == "junk" || name == "junk email" ||
            name == "bulk mail" || name.Contains("gmail/spam"))
            return FolderType.Spam;

        // Archive
        if (name == "archive" || name == "all mail" || name == "allmail" ||
            name.Contains("gmail/all mail"))
            return FolderType.Archive;

        return FolderType.Custom;
    }

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep == 0)
        {
            AutoDetectSettings();
        }
        else if (CurrentStep < 2)
        {
            CurrentStep++;
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
        }
    }
}
