using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the settings window.
///
/// Wave 5: the window is no longer a mock-up. Accounts, signatures and AI
/// provider settings are real — bound to <see cref="IAccountStore"/>,
/// <see cref="ISignatureStore"/> and <see cref="IAiSettingsStore"/>, with the
/// AI key going straight into the OS keychain.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IAccountStore _accountStore;
    private readonly ISignatureStore _signatureStore;
    private readonly IAiSettingsStore _aiSettingsStore;
    private readonly IKeychainService _keychain;
    private readonly IAiAssistant _ai;

    /// <summary>Providers the UI offers; "custom" needs a base URL.</summary>
    public static readonly IReadOnlyList<string> Providers = new[] { "openai", "anthropic", "openrouter", "custom" };

    public static readonly IReadOnlyList<string> Models = new[]
    {
        "gpt-4o-mini", "gpt-4o", "gpt-4.1-mini", "gpt-4.1",
        "claude-3-5-haiku-latest", "claude-3-5-sonnet-latest",
    };

    public SettingsViewModel(
        IAccountStore accountStore,
        ISignatureStore signatureStore,
        IAiSettingsStore aiSettingsStore,
        IKeychainService keychain,
        IAiAssistant ai)
    {
        _accountStore = accountStore;
        _signatureStore = signatureStore;
        _aiSettingsStore = aiSettingsStore;
        _keychain = keychain;
        _ai = ai;
    }

    // === Accounts ===

    [ObservableProperty]
    private ObservableCollection<Account> _accounts = new();

    [ObservableProperty]
    private Account? _selectedAccount;

    partial void OnSelectedAccountChanged(Account? value) => _ = LoadSignaturesAsync();

    // === Signatures ===

    [ObservableProperty]
    private ObservableCollection<Signature> _signatures = new();

    [ObservableProperty]
    private Signature? _selectedSignature;

    [ObservableProperty]
    private string _signatureName = "";

    [ObservableProperty]
    private string _signatureText = "";

    [ObservableProperty]
    private bool _signatureIsDefault;

    [ObservableProperty]
    private string _signatureStatus = "";

    // === AI ===

    [ObservableProperty]
    private ObservableCollection<string> _aiProviders = new(Providers);

    [ObservableProperty]
    private string _aiProvider = "openai";

    [ObservableProperty]
    private ObservableCollection<string> _aiModels = new(Models);

    [ObservableProperty]
    private string _aiModel = "gpt-4o-mini";

    [ObservableProperty]
    private string _aiBaseUrl = "";

    [ObservableProperty]
    private string _aiApiKey = "";

    [ObservableProperty]
    private bool _aiEnabled = true;

    [ObservableProperty]
    private bool _hasStoredApiKey;

    [ObservableProperty]
    private string _aiStatus = "";

    [ObservableProperty]
    private bool _isAiBusy;

    private Guid? _aiSettingsId;

    // === General ===

    public IReadOnlyList<string> Themes { get; } = new[] { "dark", "light", "system" };

    public IReadOnlyList<string> Languages { get; } = new[] { "en", "ru" };


    [ObservableProperty]
    private string _theme = "dark";

    [ObservableProperty]
    private string _language = "en";

    [ObservableProperty]
    private bool _showNotifications = true;

    [ObservableProperty]
    private string _generalStatus = "";

    /// <summary>Id of the AI settings row currently loaded (created on first save).</summary>
    public Guid AiSettingsId => _aiSettingsId ?? Guid.Empty;

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var accounts = await _accountStore.GetAllAccountsAsync();
            Accounts = new ObservableCollection<Account>(accounts);
            if (SelectedAccount is null && Accounts.Count > 0) SelectedAccount = Accounts[0];
            else await LoadSignaturesAsync();

            await LoadAiSettingsAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Settings load failed");
            GeneralStatus = $"Could not load settings: {ex.Message}";
        }
    }

    private async Task LoadAiSettingsAsync()
    {
        var settings = await _aiSettingsStore.GetDefaultAsync();
        if (settings is null)
        {
            AiStatus = "No AI provider yet — pick one, paste an API key and press Save.";
            HasStoredApiKey = false;
            return;
        }

        _aiSettingsId = settings.Id;
        AiProvider = string.IsNullOrWhiteSpace(settings.Provider) ? "openai" : settings.Provider;
        AiModel = string.IsNullOrWhiteSpace(settings.Model) ? "gpt-4o-mini" : settings.Model;
        AiBaseUrl = settings.BaseUrl ?? "";
        AiApiKey = ""; // never echo a secret back into the UI
        HasStoredApiKey = settings.ApiKeyRef is { Length: > 0 } keyRef && await _keychain.HasSecretAsync(keyRef);
        AiStatus = HasStoredApiKey
            ? $"Configured: {AiProvider} / {AiModel}."
            : "Provider saved, but no API key in the keychain yet.";
    }

    // === Commands ===

    [RelayCommand]
    private async Task SaveAiSettingsAsync()
    {
        if (IsAiBusy) return;
        IsAiBusy = true;
        try
        {
            var settings = new AiSettings
            {
                Id = _aiSettingsId ?? Guid.NewGuid(),
                Provider = AiProvider.Trim().ToLowerInvariant(),
                Model = AiModel.Trim(),
                BaseUrl = string.IsNullOrWhiteSpace(AiBaseUrl) ? null : AiBaseUrl.Trim(),
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
            };

            // The keychain holds the secret; the database only ever sees the reference.
            settings.ApiKeyRef = KeychainKeys.AiApiKey(settings.Id);
            if (!string.IsNullOrWhiteSpace(AiApiKey))
            {
                await _keychain.SetSecretAsync(settings.ApiKeyRef, AiApiKey.Trim());
                HasStoredApiKey = true;
                AiApiKey = ""; // do not keep the secret in the ViewModel
            }
            else if (!await _keychain.HasSecretAsync(settings.ApiKeyRef))
            {
                settings.ApiKeyRef = null;
            }

            if (settings.Provider == "custom" && string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                AiStatus = "A custom provider needs a base URL (e.g. https://my-host/v1).";
                return;
            }

            await _aiSettingsStore.SaveAsync(settings);
            _aiSettingsId = settings.Id;
            AiStatus = settings.ApiKeyRef is null
                ? "Saved, but no API key — AI stays off until you add one."
                : $"Saved: {settings.Provider} / {settings.Model}.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Saving AI settings failed");
            AiStatus = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsAiBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestAiConnectionAsync()
    {
        if (IsAiBusy) return;
        IsAiBusy = true;
        AiStatus = "Testing connection...";
        try
        {
            var result = await _ai.TestConnectionAsync();
            AiStatus = result.Message;
        }
        catch (AiNotConfiguredException ex)
        {
            AiStatus = ex.Message;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "AI test failed");
            AiStatus = $"Test failed: {ex.Message}";
        }
        finally
        {
            IsAiBusy = false;
        }
    }

    private async Task LoadSignaturesAsync()
    {
        if (SelectedAccount is null)
        {
            Signatures = new ObservableCollection<Signature>();
            return;
        }

        try
        {
            var list = await _signatureStore.GetSignaturesAsync(SelectedAccount.Id);
            Signatures = new ObservableCollection<Signature>(list);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Loading signatures failed");
            SignatureStatus = $"Could not load signatures: {ex.Message}";
        }
    }

    partial void OnSelectedSignatureChanged(Signature? value)
    {
        if (value is null) return;
        SignatureName = value.Name;
        SignatureText = value.ContentPlain ?? value.ContentHtml ?? "";
        SignatureIsDefault = value.IsDefault;
    }

    [RelayCommand]
    private void NewSignature()
    {
        SelectedSignature = null;
        SignatureName = "";
        SignatureText = "";
        SignatureIsDefault = Signatures.Count == 0;
        SignatureStatus = "New signature — edit and press Save.";
    }

    [RelayCommand]
    private async Task SaveSignatureAsync()
    {
        if (SelectedAccount is null)
        {
            SignatureStatus = "Add an account first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(SignatureName))
        {
            SignatureStatus = "Give the signature a name.";
            return;
        }

        try
        {
            var signature = SelectedSignature ?? new Signature { AccountId = SelectedAccount.Id };
            signature.Name = SignatureName.Trim();
            signature.ContentPlain = SignatureText;
            signature.IsDefault = SignatureIsDefault;
            signature.UpdatedAt = DateTime.UtcNow;

            if (signature.IsDefault)
            {
                // Only one default per account.
                foreach (var other in Signatures.Where(s => s.Id != signature.Id))
                {
                    if (!other.IsDefault) continue;
                    other.IsDefault = false;
                    await _signatureStore.UpdateSignatureAsync(other);
                }
            }

            if (SelectedSignature is null) await _signatureStore.SaveSignatureAsync(signature);
            else await _signatureStore.UpdateSignatureAsync(signature);

            await LoadSignaturesAsync();
            SelectedSignature = Signatures.FirstOrDefault(s => s.Id == signature.Id);
            SignatureStatus = $"Saved \"{signature.Name}\".";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Saving signature failed");
            SignatureStatus = $"Could not save: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteSignatureAsync(Signature? signature)
    {
        signature ??= SelectedSignature;
        if (signature is null) return;

        try
        {
            await _signatureStore.DeleteSignatureAsync(signature.Id);
            await LoadSignaturesAsync();
            SelectedSignature = null;
            SignatureStatus = "Deleted.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Deleting signature failed");
            SignatureStatus = $"Could not delete: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAccountAsync(Account? account)
    {
        account ??= SelectedAccount;
        if (account is null) return;

        try
        {
            await _accountStore.DeleteAccountAsync(account.Id);
            Accounts.Remove(account);
            SelectedAccount = Accounts.FirstOrDefault();
            GeneralStatus = $"Removed {account.Email}.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Deleting account failed");
            GeneralStatus = $"Could not remove account: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SaveGeneral()
    {
        // Theme/language/notifications are applied live; persistence of these
        // toggles lands with the settings-file work in v0.2 (see Техдолг).
        GeneralStatus = "Preferences applied.";
    }
}
