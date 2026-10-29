using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Core.Text;

namespace Strider.UI.ViewModels;

/// <summary>
/// ViewModel for the compose window.
///
/// Wave 6: the body is a <see cref="RichTextDocument"/> — plain text stays the
/// thing you type in, formatting is applied to the selection (colour, highlight,
/// font, size, bold/italic/underline) and the result is sent as HTML with a
/// text/plain fallback. Signatures can be HTML and can also go out as a .html
/// attachment.
/// </summary>
public partial class ComposeViewModel : ObservableObject
{
    private readonly IMessageStore _messageStore;
    private readonly IAccountStore _accountStore;
    private readonly IMailActionService _actions;
    private readonly IAiAssistant _ai;
    private readonly ISignatureStore _signatureStore;
    private readonly IAppSettingsStore _settings;
    private readonly IFontLibrary _fonts;

    /// <summary>Raised after a successful send or on Cancel; the window closes itself.</summary>
    public event Action? CloseRequested;

    // === Envelope ===

    [ObservableProperty] private string _to = "";
    [ObservableProperty] private string _cc = "";
    [ObservableProperty] private string _bcc = "";
    [ObservableProperty] private bool _showCcBcc;
    [ObservableProperty] private string _subject = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewHtml))]
    private string _bodyPlain = "";

    /// <summary>Plain text without the signature — the part the user edits.</summary>
    private string _bodyText = "";

    [ObservableProperty] private ObservableCollection<AttachmentData> _attachments = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isSending;

    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private Account? _selectedAccount;

    [ObservableProperty] private ObservableCollection<Account> _accounts = new();
    [ObservableProperty] private ComposeMode _mode = ComposeMode.New;
    [ObservableProperty] private string _windowTitle = "New Message";

    // === Rich text state ===

    private readonly RichTextDocument _document = new();

    /// <summary>Selection in the body, pushed by the view on every change.</summary>
    [ObservableProperty] private int _selectionStart;
    [ObservableProperty] private int _selectionLength;

    [ObservableProperty] private bool _showPreview;

    [ObservableProperty] private bool _showColorPicker;

    /// <summary>Default style for the whole message (font, size, colour, highlight).</summary>
    [ObservableProperty] private TextStyleSettings _textStyle = new();

    [ObservableProperty] private ObservableCollection<string> _fontFamilies = new();
    [ObservableProperty] private ObservableCollection<int> _fontSizes = new(TextStyleSettings.Sizes);
    [ObservableProperty] private ObservableCollection<string> _palette = new(TextStyleSettings.Palette);

    /// <summary>Colours for the highlighter (pastel set, readable under black text).</summary>
    public ObservableCollection<string> Highlights { get; } = new(new[]
    {
        "#FFF3B0", "#D7F5DD", "#DCE8FF", "#FBD9D9", "#EADCFB", "#E6E6E6",
    });

    [ObservableProperty] private string _activeColor = "#E5484D";
    [ObservableProperty] private string _activeHighlight = "#FFF3B0";

    /// <summary>Font picked in the toolbar; applies to the selection or to the default.</summary>
    [ObservableProperty] private string? _selectedFontFamily;

    /// <summary>Size picked in the toolbar.</summary>
    [ObservableProperty] private int _selectedFontSize = 14;

    // True while the saved defaults are being loaded, so the toolbar does not
    // fire "style applied" side effects on startup.
    private bool _loadingStyle;

    /// <summary>Live HTML preview of the body, including the signature.</summary>
    public string PreviewHtml => BuildBodyHtml();

    // === Signatures ===

    [ObservableProperty] private ObservableCollection<Signature> _signatures = new();
    [ObservableProperty] private Signature? _selectedSignature;

    /// <summary>When true the signature goes out as signature.html instead of inline.</summary>
    [ObservableProperty] private bool _signatureAsAttachment;

    [ObservableProperty] private string _signatureStatus = "";

    /// <summary>Set by the window: opens the OS file picker, returns chosen files.</summary>
    public Func<Task<IReadOnlyList<(string Name, byte[] Data)>>>? PickFiles { get; set; }

    [ObservableProperty] private string _aiStatus = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AiDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(AiImproveCommand))]
    private bool _isAiBusy;

    /// <summary>The message being replied to / forwarded — the AI draft needs it.</summary>
    private Message? _original;

    /// <summary>Most providers reject messages above ~25 MB.</summary>
    public const long MaxTotalAttachmentBytes = 25L * 1024 * 1024;

    // UID of the last saved draft on the server, so a re-save replaces it and Send removes it.
    private int? _draftUid;

    // Threading headers for replies.
    private string? _inReplyTo;
    private List<string> _references = new();

    public ComposeViewModel(
        IMessageStore messageStore,
        IAccountStore accountStore,
        IMailActionService actions,
        IAiAssistant ai,
        ISignatureStore signatureStore,
        IAppSettingsStore settings,
        IFontLibrary fonts)
    {
        _messageStore = messageStore;
        _accountStore = accountStore;
        _actions = actions;
        _ai = ai;
        _signatureStore = signatureStore;
        _settings = settings;
        _fonts = fonts;
    }

    [RelayCommand]
    public async Task LoadAccountsAsync()
    {
        var accounts = await _accountStore.GetAllAccountsAsync();
        Accounts = new ObservableCollection<Account>(accounts);
        SelectedAccount ??= Accounts.FirstOrDefault();

        try
        {
            _loadingStyle = true;
            TextStyle = await _settings.GetTextStyleAsync();
            FontFamilies = new ObservableCollection<string>(_fonts.GetAvailableFamilies());
            if (!FontFamilies.Contains(TextStyle.FontFamily)) FontFamilies.Insert(0, TextStyle.FontFamily);
            SelectedFontFamily = TextStyle.FontFamily;
            SelectedFontSize = TextStyle.FontSize;
            ActiveColor = TextStyle.TextColor;
            if (!string.IsNullOrWhiteSpace(TextStyle.HighlightColor)) ActiveHighlight = TextStyle.HighlightColor;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not load the default text style");
        }
        finally
        {
            _loadingStyle = false;
        }

        await LoadSignaturesAsync();
        RefreshPreview();
    }

    partial void OnSelectedAccountChanged(Account? value) => _ = LoadSignaturesAsync();

    partial void OnBodyPlainChanged(string value)
    {
        // The text box is the source of truth; the document follows it, keeping
        // the formatting of the parts that did not change.
        if (_syncingText) return;
        _bodyText = value;
        _document.SetText(value);
        RefreshPreview();
    }

    private bool _syncingText;

    /// <summary>Prepares a reply, reply-all or forward of <paramref name="original"/>.</summary>
    public async Task LoadFromAsync(Message original, ComposeMode mode)
    {
        _original = original;
        await LoadAccountsAsync();
        // Answer from the account the mail arrived to.
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == original.AccountId) ?? SelectedAccount;
        Mode = mode;

        var body = await _messageStore.GetBodyAsync(original.Id);
        var originalText = body?.TextPlain
                           ?? Strider.Infrastructure.Security.HtmlText.ToPlainText(body?.TextHtml);

        switch (mode)
        {
            case ComposeMode.Reply:
                To = AddressList.ToInput(ReplyBuilder.ReplyTo(original));
                break;
            case ComposeMode.ReplyAll:
                var (to, cc) = ReplyBuilder.ReplyAll(original, SelectedAccount?.Email ?? "");
                To = AddressList.ToInput(to);
                Cc = AddressList.ToInput(cc);
                ShowCcBcc = cc.Count > 0;
                break;
        }

        if (mode == ComposeMode.Forward)
        {
            Subject = ReplyBuilder.ForwardSubject(original.Subject);
            BodyPlain = "\n\n" + ReplyBuilder.QuoteHeader(original, forward: true) + "\n" + originalText;
            WindowTitle = "Forward";
        }
        else
        {
            Subject = ReplyBuilder.ReplySubject(original.Subject);
            BodyPlain = "\n\n" + ReplyBuilder.QuoteHeader(original, forward: false) + ReplyBuilder.QuotePlain(originalText);
            _inReplyTo = ReplyBuilder.StripBrackets(original.MessageId);
            _references = ReplyBuilder.References(original);
            WindowTitle = "Reply";
        }

        await AppendDefaultSignatureIfEmptyAsync();
        RefreshPreview();
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
            SelectedSignature = list.FirstOrDefault(s => s.IsDefault) ?? list.FirstOrDefault();
            SignatureAsAttachment = SelectedSignature?.AttachAsHtmlFile ?? false;

            if (Mode == ComposeMode.New && SelectedSignature is { } signature && string.IsNullOrEmpty(_bodyText))
            {
                AppendSignature(signature);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not load signatures");
        }
    }

    private async Task AppendDefaultSignatureIfEmptyAsync()
    {
        if (!string.IsNullOrEmpty(_bodyText)) return;
        if (SelectedSignature is null && SelectedAccount is not null)
        {
            try
            {
                var list = await _signatureStore.GetSignaturesAsync(SelectedAccount.Id);
                SelectedSignature = list.FirstOrDefault(s => s.IsDefault) ?? list.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Could not load signatures");
            }
        }

        if (SelectedSignature is { } signature) AppendSignature(signature);
    }

    partial void OnSelectedSignatureChanged(Signature? value)
    {
        SignatureAsAttachment = value?.AttachAsHtmlFile ?? false;
        RefreshPreview();
    }

    partial void OnSignatureAsAttachmentChanged(bool value) => RefreshPreview();

    partial void OnActiveColorChanged(string value) => RefreshPreview();

    partial void OnSelectedFontFamilyChanged(string? value)
    {
        if (_loadingStyle || string.IsNullOrWhiteSpace(value)) return;
        ApplyFontFamily(value);
    }

    partial void OnSelectedFontSizeChanged(int value)
    {
        if (_loadingStyle || value <= 0) return;
        ApplyFontSize(value);
    }

    /// <summary>Appends the signature text to the body (never overwrites typed text).</summary>
    private void AppendSignature(Signature signature)
    {
        var text = signature.ContentPlain
                   ?? Strider.UI.Rendering.HtmlInlinesBuilder.ToPlainText(signature.ContentHtml);
        if (string.IsNullOrWhiteSpace(text)) return;

        SetBodyText(string.IsNullOrWhiteSpace(_bodyText)
            ? "\n\n-- \n" + text
            : _bodyText.TrimEnd() + "\n\n-- \n" + text);
        SignatureStatus = $"Signature \"{signature.Name}\" inserted.";
    }

    [RelayCommand]
    private void InsertSignature()
    {
        if (SelectedSignature is null)
        {
            SignatureStatus = "This account has no signatures yet — add one in Settings.";
            return;
        }

        AppendSignature(SelectedSignature);
        RefreshPreview();
    }

    // === Formatting commands (operate on the current selection) ===

    private bool HasSelection => SelectionLength > 0;

    [RelayCommand]
    private void Bold()
    {
        if (!HasSelection) { StatusMessage = "Select the words to make bold first."; return; }
        if (_document.HasInRange(SelectionStart, SelectionLength, s => s.Bold))
            _document.Clear(SelectionStart, SelectionLength, s => s.WithoutBold());
        else
            _document.Apply(SelectionStart, SelectionLength, new Core.Text.TextStyle { Bold = true });
        RefreshPreview();
    }

    [RelayCommand]
    private void Italic()
    {
        if (!HasSelection) { StatusMessage = "Select the words to italicise first."; return; }
        if (_document.HasInRange(SelectionStart, SelectionLength, s => s.Italic))
            _document.Clear(SelectionStart, SelectionLength, s => s.WithoutItalic());
        else
            _document.Apply(SelectionStart, SelectionLength, new Core.Text.TextStyle { Italic = true });
        RefreshPreview();
    }

    [RelayCommand]
    private void Underline()
    {
        if (!HasSelection) { StatusMessage = "Select the words to underline first."; return; }
        if (_document.HasInRange(SelectionStart, SelectionLength, s => s.Underline))
            _document.Clear(SelectionStart, SelectionLength, s => s.WithoutUnderline());
        else
            _document.Apply(SelectionStart, SelectionLength, new Core.Text.TextStyle { Underline = true });
        RefreshPreview();
    }

    /// <summary>Colours the selected words (the command parameter is the hex colour).</summary>
    [RelayCommand]
    private void ApplyColor(string? color)
    {
        var hex = string.IsNullOrWhiteSpace(color) ? ActiveColor : color;
        ActiveColor = hex;

        if (!HasSelection)
        {
            TextStyle = TextStyle.Clone();
            TextStyle.TextColor = hex;
            StatusMessage = $"Default text colour set to {hex}.";
            RefreshPreview();
            return;
        }

        _document.Apply(SelectionStart, SelectionLength, new Core.Text.TextStyle { Color = hex });
        StatusMessage = $"Coloured {SelectionLength} character(s).";
        RefreshPreview();
    }

    /// <summary>Highlights the selected words.</summary>
    [RelayCommand]
    private void ApplyHighlight(string? color)
    {
        var hex = string.IsNullOrWhiteSpace(color) ? ActiveHighlight : color;
        ActiveHighlight = hex;

        if (!HasSelection)
        {
            TextStyle = TextStyle.Clone();
            TextStyle.HighlightColor = hex;
            StatusMessage = $"Default highlight set to {hex}.";
            RefreshPreview();
            return;
        }

        _document.Apply(SelectionStart, SelectionLength, new Core.Text.TextStyle { Highlight = hex });
        StatusMessage = $"Highlighted {SelectionLength} character(s).";
        RefreshPreview();
    }

    [RelayCommand]
    private void ApplyFontFamily(string? family)
    {
        if (string.IsNullOrWhiteSpace(family)) return;

        if (!HasSelection)
        {
            TextStyle = TextStyle.Clone();
            TextStyle.FontFamily = family;
            StatusMessage = $"Default font set to {family}.";
            RefreshPreview();
            return;
        }

        _document.Apply(SelectionStart, SelectionLength, new Core.Text.TextStyle { FontFamily = family });
        StatusMessage = $"Font \"{family}\" applied.";
        RefreshPreview();
    }

    [RelayCommand]
    private void ApplyFontSize(int size)
    {
        if (size <= 0) return;

        if (!HasSelection)
        {
            TextStyle = TextStyle.Clone();
            TextStyle.FontSize = size;
            StatusMessage = $"Default size set to {size}px.";
            RefreshPreview();
            return;
        }

        _document.Apply(SelectionStart, SelectionLength, new Core.Text.TextStyle { FontSize = size });
        RefreshPreview();
    }

    /// <summary>Removes every formatting attribute from the selected words.</summary>
    [RelayCommand]
    private void ClearFormatting()
    {
        if (!HasSelection) { _document.ClearAll(); RefreshPreview(); return; }

        _document.Clear(SelectionStart, SelectionLength, _ => Core.Text.TextStyle.Default);
        RefreshPreview();
        StatusMessage = "Formatting cleared.";
    }

    [RelayCommand]
    private void TogglePreview()
    {
        ShowPreview = !ShowPreview;
        RefreshPreview();
    }

    // === Body assembly ===

    private void SetBodyText(string text)
    {
        _syncingText = true;
        BodyPlain = text;
        _syncingText = false;
        _bodyText = text;
        _document.SetText(text);
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        OnPropertyChanged(nameof(PreviewHtml));
        OnPropertyChanged(nameof(SignaturePreviewHtml));
    }

    /// <summary>HTML of the inline signature (empty when it goes out as a file).</summary>
    public string SignaturePreviewHtml
    {
        get
        {
            if (SignatureAsAttachment || SelectedSignature is null) return "";
            return SignatureHtml(SelectedSignature);
        }
    }

    private static string SignatureHtml(Signature signature)
    {
        if (!string.IsNullOrWhiteSpace(signature.ContentHtml)) return signature.ContentHtml!;
        var plain = signature.ContentPlain ?? "";
        if (string.IsNullOrWhiteSpace(plain)) return "";
        return "<div style=\"color:#666;font-size:13px;\">" +
               WebUtility.HtmlEncode(plain).Replace("\r\n", "\n").Replace("\n", "<br>") +
               "</div>";
    }

    /// <summary>
    /// The HTML that goes on the wire: formatted body + inline signature.
    /// Read by Send/SaveDraft and shown in the preview pane.
    /// </summary>
    public string BuildBodyHtml()
    {
        _document.DefaultStyle = new Core.Text.TextStyle
        {
            FontFamily = TextStyle.FontFamily,
            FontSize = TextStyle.FontSize,
            Color = TextStyle.TextColor,
        };

        var html = _document.ToHtml(TextStyle.ToCss());

        if (!SignatureAsAttachment && SelectedSignature is not null)
        {
            var signature = SignatureHtml(SelectedSignature);
            if (!string.IsNullOrWhiteSpace(signature))
                html += "<br>" + signature;
        }

        return html;
    }

    /// <summary>The text/plain alternative: body plus signature as text.</summary>
    private string BuildBodyPlain()
    {
        if (SignatureAsAttachment || SelectedSignature is null) return _bodyText;

        var signature = SelectedSignature.ContentPlain
                        ?? Strider.UI.Rendering.HtmlInlinesBuilder.ToPlainText(SelectedSignature.ContentHtml);
        if (string.IsNullOrWhiteSpace(signature)) return _bodyText;

        return _bodyText.Contains(signature, StringComparison.Ordinal)
            ? _bodyText
            : _bodyText.TrimEnd() + "\n\n-- \n" + signature;
    }

    /// <summary>Builds the .html attachment when the user asked for one.</summary>
    private List<AttachmentData> BuildAttachments()
    {
        var list = Attachments.ToList();

        if (SignatureAsAttachment && SelectedSignature is not null)
        {
            var html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>" +
                       WebUtility.HtmlEncode(SelectedSignature.Name) +
                       "</title></head><body>" + SignatureHtml(SelectedSignature) + "</body></html>";
            list.Add(new AttachmentData
            {
                Filename = "signature.html",
                ContentType = "text/html",
                Content = System.Text.Encoding.UTF8.GetBytes(html),
            });
        }

        return list;
    }

    private bool CanSend() => !IsSending && SelectedAccount != null;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var to = AddressList.ParseInput(To);
        var cc = AddressList.ParseInput(Cc);
        var bcc = AddressList.ParseInput(Bcc);

        if (to.Count + cc.Count + bcc.Count == 0)
        {
            StatusMessage = "Add at least one recipient";
            return;
        }
        if (SelectedAccount == null)
        {
            StatusMessage = "No account selected";
            return;
        }

        IsSending = true;
        StatusMessage = "Sending...";
        try
        {
            var message = BuildMessage(to, cc, bcc);
            await _actions.SendAsync(SelectedAccount.Id, message);
            if (_draftUid is { } uid)
            {
                try { await _actions.DeleteDraftAsync(SelectedAccount.Id, uid); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Sent, but removing the draft failed"); }
            }
            StatusMessage = "Sent";
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Send failed");
            StatusMessage = $"Send failed: {ex.Message}";
        }
        finally
        {
            IsSending = false;
        }
    }

    private OutgoingMessage BuildMessage(List<string> to, List<string> cc, List<string> bcc) => new()
    {
        From = SelectedAccount?.Email ?? "",
        FromName = SelectedAccount?.DisplayName,
        To = to,
        Cc = cc,
        Bcc = bcc,
        Subject = Subject,
        BodyPlain = BuildBodyPlain(),
        BodyHtml = BuildBodyHtml(),
        InReplyTo = _inReplyTo,
        References = _references,
        Attachments = BuildAttachments(),
    };

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SaveDraftAsync()
    {
        if (SelectedAccount == null) return;
        IsSending = true;
        try
        {
            _draftUid = await _actions.SaveDraftAsync(SelectedAccount.Id,
                BuildMessage(AddressList.ParseInput(To), AddressList.ParseInput(Cc), AddressList.ParseInput(Bcc)),
                _draftUid);
            StatusMessage = $"Draft saved {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Saving draft failed");
            StatusMessage = $"Draft not saved: {ex.Message}";
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private async Task AddAttachmentAsync()
    {
        if (PickFiles == null) return;
        try
        {
            var files = await PickFiles();
            foreach (var (name, data) in files)
            {
                var total = Attachments.Sum(a => (long)a.Content.Length) + data.Length;
                if (total > MaxTotalAttachmentBytes)
                {
                    StatusMessage = $"{name} not added: attachments would exceed 25 MB";
                    continue;
                }
                Attachments.Add(new AttachmentData
                {
                    Filename = name,
                    ContentType = MimeKit.MimeTypes.GetMimeType(name),
                    Content = data,
                });
            }
            OnPropertyChanged(nameof(AttachmentsSummary));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Attach failed");
            StatusMessage = $"Could not attach: {ex.Message}";
        }
    }

    public string AttachmentsSummary => Attachments.Count == 0
        ? ""
        : $"{Attachments.Count} file(s), {Attachments.Sum(a => (long)a.Content.Length) / 1024.0:0} KB";

    [RelayCommand]
    private void ToggleCcBcc() => ShowCcBcc = !ShowCcBcc;

    [RelayCommand]
    private void ToggleColorPicker() => ShowColorPicker = !ShowColorPicker;

    private bool CanUseAi() => !IsAiBusy;

    /// <summary>
    /// Drafts the reply with AI (F5.5). Needs an original message: for a brand-new
    /// mail there is nothing to answer.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseAi))]
    private async Task AiDraftAsync()
    {
        if (IsAiBusy) return;
        if (_original is null)
        {
            AiStatus = "Open a message first - AI drafts replies, not cold emails.";
            return;
        }

        IsAiBusy = true;
        AiStatus = "Drafting...";
        try
        {
            var result = await _ai.DraftReplyAsync(_original);
            var quoted = _bodyText; // keep the quote header that LoadFromAsync prepared
            var quoteIndex = quoted.IndexOf("\n\nOn ", StringComparison.Ordinal);
            var header = quoteIndex > 0 ? quoted[..quoteIndex] : "";
            var quote = quoteIndex > 0 ? quoted[quoteIndex..] : "";
            SetBodyText(header + result.Text + "\n" + quote);
            AiStatus = $"Draft ready - {result.UsageLabel}. Review before sending.";
        }
        catch (AiNotConfiguredException ex)
        {
            AiStatus = ex.Message;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "AI draft failed");
            AiStatus = $"AI draft failed: {ex.Message}";
        }
        finally
        {
            IsAiBusy = false;
        }
    }

    /// <summary>Grammar/clarity pass over the current body.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAi))]
    private async Task AiImproveAsync()
    {
        if (IsAiBusy) return;
        if (string.IsNullOrWhiteSpace(_bodyText))
        {
            AiStatus = "Nothing to improve yet.";
            return;
        }

        IsAiBusy = true;
        AiStatus = "Polishing...";
        try
        {
            var result = await _ai.ImproveWritingAsync(_bodyText);
            _document.ClearAll();
            SetBodyText(result.Text);
            AiStatus = $"Improved - {result.UsageLabel}";
        }
        catch (AiNotConfiguredException ex)
        {
            AiStatus = ex.Message;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "AI improve failed");
            AiStatus = $"AI failed: {ex.Message}";
        }
        finally
        {
            IsAiBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    [RelayCommand]
    private void RemoveAttachment(AttachmentData attachment)
    {
        Attachments.Remove(attachment);
        OnPropertyChanged(nameof(AttachmentsSummary));
    }
}
