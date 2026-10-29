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
    private readonly IAppSettingsStore _settings;
    private readonly IFontLibrary _fonts;
    private readonly IFlagRuleStore _rules;
    private readonly IFlagService _flagService;

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
        IAiAssistant ai,
        IAppSettingsStore settings,
        IFontLibrary fonts,
        IFlagRuleStore rules,
        IFlagService flagService)
    {
        _accountStore = accountStore;
        _signatureStore = signatureStore;
        _aiSettingsStore = aiSettingsStore;
        _keychain = keychain;
        _ai = ai;
        _settings = settings;
        _fonts = fonts;
        _rules = rules;
        _flagService = flagService;
    }

    /// <summary>Set by the window: opens a file picker for a font file.</summary>
    public Func<Task<string?>>? PickFontFile { get; set; }

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

    /// <summary>When true the signature is stored and sent as HTML.</summary>
    [ObservableProperty]
    private bool _signatureIsHtml;

    [ObservableProperty]
    private bool _signatureAttachAsFile;

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


    // === Fonts & default text style (Wave 6) ===

    [ObservableProperty]
    private ObservableCollection<string> _fontFamilies = new();

    [ObservableProperty]
    private ObservableCollection<CustomFont> _customFonts = new();

    [ObservableProperty]
    private string _textFontFamily = "Inter";

    [ObservableProperty]
    private ObservableCollection<int> _fontSizes = new(TextStyleSettings.Sizes);

    [ObservableProperty]
    private int _textFontSize = 14;

    [ObservableProperty]
    private string _textColor = "#1A1A1A";

    [ObservableProperty]
    private string _highlightColor = "";

    [ObservableProperty]
    private string _textStyleStatus = "";

    [ObservableProperty]
    private bool _isFontBusy;

    /// <summary>Colours offered in the editor palettes.</summary>
    public ObservableCollection<string> Palette { get; } = new(TextStyleSettings.Palette);

    /// <summary>Preview markup for the current default style (rendered by HtmlTextBlock).</summary>
    [ObservableProperty]
    private string _textStylePreview = "";

    private TextStyleSettings _textStyle = new();

    private void RefreshTextStylePreview()
    {
        var settings = new TextStyleSettings
        {
            FontFamily = TextFontFamily,
            FontSize = TextFontSize,
            TextColor = TextColor,
            HighlightColor = HighlightColor,
        };

        TextStylePreview =
            $"<div style=\"{settings.ToCss()}\">" +
            "The quick brown fox jumps over the lazy dog.<br>" +
            "<span style=\"color:#E5484D;font-weight:bold;\">Red and bold</span> · " +
            "<span style=\"background-color:#FFF3B0;\">highlighted</span> · " +
            "<i>italic</i> · <u>underlined</u>" +
            "</div>";
    }

    partial void OnTextFontFamilyChanged(string value) => RefreshTextStylePreview();
    partial void OnTextFontSizeChanged(int value) => RefreshTextStylePreview();
    partial void OnTextColorChanged(string value) => RefreshTextStylePreview();
    partial void OnHighlightColorChanged(string value) => RefreshTextStylePreview();

    private async Task LoadTextStyleAsync()
    {
        try
        {
            FontFamilies = new ObservableCollection<string>(_fonts.GetAvailableFamilies());
            CustomFonts = new ObservableCollection<CustomFont>(_fonts.GetCustomFonts());

            _textStyle = await _settings.GetTextStyleAsync();
            TextFontFamily = _textStyle.FontFamily;
            TextFontSize = _textStyle.FontSize;
            TextColor = _textStyle.TextColor;
            HighlightColor = _textStyle.HighlightColor;
            RefreshTextStylePreview();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Loading the text style failed");
            TextStyleStatus = $"Could not load the text settings: {ex.Message}";
        }
    }

    /// <summary>Picks the default text colour from the palette (Wave 6).</summary>
    [RelayCommand]
    private void PickTextColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex)) TextColor = hex;
    }

    /// <summary>Picks the default highlight colour; empty means "no highlight".</summary>
    [RelayCommand]
    private void PickHighlightColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex)) HighlightColor = hex;
    }

    [RelayCommand]
    private void ClearHighlightColor() => HighlightColor = "";

    [RelayCommand]
    private void PickRuleColor(FlagColor color) => RuleColor = color;

    [RelayCommand]
    private async Task SaveTextStyleAsync()
    {
        try
        {
            _textStyle = new TextStyleSettings
            {
                FontFamily = TextFontFamily,
                FontSize = Math.Clamp(TextFontSize, 8, 72),
                TextColor = string.IsNullOrWhiteSpace(TextColor) ? "#1A1A1A" : TextColor,
                HighlightColor = HighlightColor ?? "",
                RecentColors = _textStyle.RecentColors,
            };

            await _settings.SaveTextStyleAsync(_textStyle);
            TextStyleStatus = $"Saved: {_textStyle.FontFamily}, {_textStyle.FontSize}px. New messages use this style.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Saving the text style failed");
            TextStyleStatus = $"Could not save: {ex.Message}";
        }
    }

    /// <summary>Adds a .ttf/.otf from disk: it is copied into the app font folder and registered.</summary>
    [RelayCommand]
    private async Task AddFontAsync()
    {
        if (PickFontFile is null)
        {
            TextStyleStatus = "The font picker is not available.";
            return;
        }

        var path = await PickFontFile!();
        if (string.IsNullOrWhiteSpace(path)) return;

        IsFontBusy = true;
        try
        {
            var font = await _fonts.AddFontFileAsync(path);
            FontFamilies = new ObservableCollection<string>(_fonts.GetAvailableFamilies());
            CustomFonts = new ObservableCollection<CustomFont>(_fonts.GetCustomFonts());
            TextFontFamily = font.Family;
            TextStyleStatus = $"Added \"{font.Family}\". Pick it as the default and press Save.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Adding a font failed");
            TextStyleStatus = $"Could not add the font: {ex.Message}";
        }
        finally
        {
            IsFontBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveFontAsync(CustomFont? font)
    {
        if (font is null) return;

        try
        {
            await _fonts.RemoveAsync(font.Id);
            FontFamilies = new ObservableCollection<string>(_fonts.GetAvailableFamilies());
            CustomFonts = new ObservableCollection<CustomFont>(_fonts.GetCustomFonts());
            TextStyleStatus = $"Removed \"{font.Family}\".";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Removing a font failed");
            TextStyleStatus = $"Could not remove the font: {ex.Message}";
        }
    }

    // === Coloured flag rules (Wave 6) ===

    [ObservableProperty]
    private ObservableCollection<FlagRule> _flagRules = new();

    [ObservableProperty]
    private FlagRule? _selectedRule;

    [ObservableProperty]
    private string _ruleName = "";

    [ObservableProperty]
    private ObservableCollection<FlagRuleField> _ruleFields = new(Enum.GetValues<FlagRuleField>());

    [ObservableProperty]
    private FlagRuleField _ruleField = FlagRuleField.Sender;

    [ObservableProperty]
    private ObservableCollection<FlagRuleOperator> _ruleOperators = new(Enum.GetValues<FlagRuleOperator>());

    [ObservableProperty]
    private FlagRuleOperator _ruleOperator = FlagRuleOperator.Contains;

    [ObservableProperty]
    private string _rulePattern = "";

    [ObservableProperty]
    private ObservableCollection<FlagColor> _ruleColors = new(FlagColors.All);

    [ObservableProperty]
    private FlagColor _ruleColor = FlagColor.Blue;

    [ObservableProperty]
    private bool _ruleIsEnabled = true;

    [ObservableProperty]
    private string _ruleStatus = "";

    [ObservableProperty]
    private bool _isRuleBusy;

    partial void OnSelectedRuleChanged(FlagRule? value)
    {
        if (value is null) return;
        RuleName = value.Name;
        RuleField = value.Field;
        RuleOperator = value.Operator;
        RulePattern = value.Pattern;
        RuleColor = value.Color;
        RuleIsEnabled = value.IsEnabled;
    }

    private async Task LoadFlagRulesAsync()
    {
        try
        {
            var rules = await _rules.GetAllAsync();
            FlagRules = new ObservableCollection<FlagRule>(rules);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Loading flag rules failed");
            RuleStatus = $"Could not load the rules: {ex.Message}";
        }
    }

    [RelayCommand]
    private void NewRule()
    {
        SelectedRule = null;
        RuleName = "";
        RuleField = FlagRuleField.Sender;
        RuleOperator = FlagRuleOperator.Contains;
        RulePattern = "";
        RuleColor = FlagColor.Blue;
        RuleIsEnabled = true;
        RuleStatus = "New rule — fill the pattern and press Save.";
    }

    [RelayCommand]
    private async Task SaveRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(RulePattern))
        {
            RuleStatus = "The pattern cannot be empty.";
            return;
        }

        IsRuleBusy = true;
        try
        {
            var rule = SelectedRule ?? new FlagRule { Priority = FlagRules.Count };
            rule.Name = string.IsNullOrWhiteSpace(RuleName) ? RulePattern : RuleName.Trim();
            rule.Field = RuleField;
            rule.Operator = RuleOperator;
            rule.Pattern = RulePattern.Trim();
            rule.Color = RuleColor;
            rule.IsEnabled = RuleIsEnabled;

            await _rules.SaveAsync(rule);
            await LoadFlagRulesAsync();
            SelectedRule = FlagRules.FirstOrDefault(r => r.Id == rule.Id);
            RuleStatus = $"Saved. It applies to new mail during sync; press \"Apply now\" for existing mail.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Saving a flag rule failed");
            RuleStatus = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsRuleBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(FlagRule? rule)
    {
        rule ??= SelectedRule;
        if (rule is null) return;

        try
        {
            await _rules.DeleteAsync(rule.Id);
            await LoadFlagRulesAsync();
            SelectedRule = null;
            RuleStatus = $"Deleted \"{rule.Name}\".";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Deleting a flag rule failed");
            RuleStatus = $"Could not delete: {ex.Message}";
        }
    }

    /// <summary>Moves a rule up/down — the first matching rule wins, so order matters.</summary>
    [RelayCommand]
    private async Task MoveRuleAsync(string? direction)
    {
        if (SelectedRule is null) return;

        var list = FlagRules.ToList();
        var index = list.FindIndex(r => r.Id == SelectedRule.Id);
        if (index < 0) return;

        var target = direction == "up" ? index - 1 : index + 1;
        if (target < 0 || target >= list.Count) return;

        (list[index], list[target]) = (list[target], list[index]);

        try
        {
            await _rules.ReorderAsync(list.Select(r => r.Id).ToList());
            await LoadFlagRulesAsync();
            SelectedRule = FlagRules.FirstOrDefault(r => r.Id == list[target].Id);
            RuleStatus = "Order saved.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Reordering flag rules failed");
            RuleStatus = $"Could not reorder: {ex.Message}";
        }
    }

    /// <summary>Re-colours already cached mail with the current rules.</summary>
    [RelayCommand]
    private async Task ApplyRulesNowAsync()
    {
        if (SelectedAccount is null)
        {
            RuleStatus = "Select an account first.";
            return;
        }

        IsRuleBusy = true;
        try
        {
            var changed = await _flagService.ApplyRulesToAccountAsync(SelectedAccount.Id);
            RuleStatus = changed == 0
                ? "Nothing to change — the cached mail already matches the rules."
                : $"Recoloured {changed} message(s).";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Applying flag rules failed");
            RuleStatus = $"Could not apply the rules: {ex.Message}";
        }
        finally
        {
            IsRuleBusy = false;
        }
    }

    private async Task LoadFlagsAndFontsAsync()
    {
        await LoadFlagRulesAsync();
        await LoadTextStyleAsync();
    }

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
            await LoadFlagsAndFontsAsync();
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

    /// <summary>
    /// Rendered HTML of the signature being edited, so what you see in Settings is
    /// exactly what the composer inserts (Wave 6).
    /// </summary>
    public string SignaturePreviewHtml
    {
        get
        {
            if (!SignatureIsHtml || string.IsNullOrWhiteSpace(SignatureText)) return "";
            return SignatureText;
        }
    }

    partial void OnSignatureTextChanged(string value) => OnPropertyChanged(nameof(SignaturePreviewHtml));

    partial void OnSignatureIsHtmlChanged(bool value) => OnPropertyChanged(nameof(SignaturePreviewHtml));

    partial void OnSelectedSignatureChanged(Signature? value)
    {
        if (value is null) return;
        SignatureName = value.Name;
        SignatureIsHtml = !string.IsNullOrWhiteSpace(value.ContentHtml);
        SignatureText = SignatureIsHtml ? value.ContentHtml! : value.ContentPlain ?? "";
        SignatureAttachAsFile = value.AttachAsHtmlFile;
        SignatureIsDefault = value.IsDefault;
    }

    [RelayCommand]
    private void NewSignature()
    {
        SelectedSignature = null;
        SignatureName = "";
        SignatureText = "";
        SignatureIsHtml = false;
        SignatureAttachAsFile = false;
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

            // HTML mode keeps markup as-is; plain mode stores text and clears the HTML field.
            signature.ContentHtml = SignatureIsHtml ? SignatureText : null;
            signature.ContentPlain = SignatureIsHtml
                ? Strider.UI.Rendering.HtmlInlinesBuilder.ToPlainText(SignatureText)
                : SignatureText;
            signature.AttachAsHtmlFile = SignatureAttachAsFile;
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
