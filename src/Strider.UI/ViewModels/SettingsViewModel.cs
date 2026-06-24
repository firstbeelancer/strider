using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the settings window.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IAccountStore _accountStore;
    // private readonly IKeychainService? _keychainService; // TODO: wire up when implemented

    [ObservableProperty]
    private ObservableCollection<Account> _accounts = new();

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private ObservableCollection<Signature> _signatures = new();

    [ObservableProperty]
    private string _aiProvider = "openai";

    [ObservableProperty]
    private string _aiModel = "gpt-4o-mini";

    [ObservableProperty]
    private string _aiApiKey = "";

    [ObservableProperty]
    private bool _aiEnabled = true;

    [ObservableProperty]
    private string _theme = "dark";

    [ObservableProperty]
    private string _language = "en";

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _showNotifications = true;

    public SettingsViewModel(IAccountStore accountStore)
    {
        _accountStore = accountStore;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var accounts = await _accountStore.GetAllAccountsAsync();
        Accounts = new ObservableCollection<Account>(accounts);
        if (Accounts.Count > 0)
            SelectedAccount = Accounts[0];
    }

    [RelayCommand]
    private void SaveGeneral()
    {
        // TODO: Save general settings to config file
    }

    [RelayCommand]
    private async Task DeleteAccountAsync(Account account)
    {
        await _accountStore.DeleteAccountAsync(account.Id);
        Accounts.Remove(account);
    }

    [RelayCommand]
    private void SaveAiSettings()
    {
        // TODO: Save AI settings, store API key in keychain
    }

    [RelayCommand]
    private void GeneratePgpKey()
    {
        // TODO: Open PGP key generation dialog
    }

    [RelayCommand]
    private void ImportPgpKey()
    {
        // TODO: Open file picker for PGP key import
    }
}
