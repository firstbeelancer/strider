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
    private readonly IMailSyncService? _sync;

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

    /// <summary>
    /// The signature is never part of the edited text: it is added once when the
    /// message is built, so changing the selection here just changes what is sent.
    /// </summary>
    [ObservableProperty] private bool _includeSignature = true;

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
        IFontLibrary fonts,
        IMailSyncService? sync = null)
    {
        _messageStore = messageStore;
        _accountStore = accountStore;
        _actions = actions;
        _ai = ai;
        _signatureStore = signatureStore;
        _settings = settings;
        _fonts = fonts;
        _sync = sync;
        MarkClean();
    }

    // === Draft safety (F4.5, F4.7, NFR 6.3) ===

    /// <summary>How long a pressed Send can still be undone (read from the General settings).</summary>
    public TimeSpan UndoSendDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>True during the undo window: the message is not on its way yet.</summary>
    [ObservableProperty]
    private bool _isSendPending;

    private CancellationTokenSource? _undoSend;
    private string _savedState = "";
    private bool _discardArmed;
    private bool _finished;

    private string DraftState() => string.Join('\u0001', To, Cc, Bcc, Subject, _bodyText);

    private void MarkClean() => _savedState = DraftState();

    /// <summary>True when there is something a draft save would not lose.</summary>
    public bool IsDirty => DraftState() != _savedState;

    /// <summary>The window was closed with unsaved text and not by Send or an explicit discard.</summary>
    public bool ShouldSaveOnClose => IsDirty && !_finished && !IsSendPending;

    /// <summary>The 30-second autosave tick: quiet unless there is news to report.</summary>
    public async Task AutoSaveAsync()
    {
        if (!IsDirty || IsSending || SelectedAccount == null) return;
        await SaveDraftAsync();
    }

    [RelayCommand]
    private void UndoSend() => _undoSend?.Cancel();

    /// <summary>Composer shortcuts (ТЗ §12.4). Returns true when the key was used.</summary>
    public bool HandleShortcut(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        const Avalonia.Input.KeyModifiers ctrl = Avalonia.Input.KeyModifiers.Control;

        if (modifiers == Avalonia.Input.KeyModifiers.None && key == Avalonia.Input.Key.Escape)
            return Run(CancelCommand);
        if (modifiers == (ctrl | Avalonia.Input.KeyModifiers.Shift) && key == Avalonia.Input.Key.A)
            return Run(AddAttachmentCommand);
        if (modifiers != ctrl) return false;

        return key switch
        {
            Avalonia.Input.Key.Enter => Run(SendCommand),
            Avalonia.Input.Key.S => Run(SaveDraftCommand),
            Avalonia.Input.Key.B => Run(BoldCommand),
            Avalonia.Input.Key.I => Run(ItalicCommand),
            Avalonia.Input.Key.U => Run(UnderlineCommand),
            _ => false,
        };
    }

    private static bool Run(System.Windows.Input.ICommand command)
    {
        if (!command.CanExecute(null)) return false;
        command.Execute(null);
        return true;
    }

    // === Any colour (F4.12) ===

    /// <summary>Text of the #RRGGBB box next to the palettes.</summary>
    [ObservableProperty] private string _customColor = "";

    private static readonly System.Text.RegularExpressions.Regex HexColor = new("^#[0-9a-fA-F]{6}$");

    [RelayCommand]
    private Task ApplyCustomColorAsync() => UseCustomAsync(ApplyColor);

    [RelayCommand]
    private Task ApplyCustomHighlightAsync() => UseCustomAsync(ApplyHighlight);

    private async Task UseCustomAsync(Action<string?> apply)
    {
        var hex = CustomColor.Trim();
        if (hex.Length == 6 && !hex.StartsWith('#')) hex = "#" + hex;
        if (!HexColor.IsMatch(hex))
        {
            StatusMessage = "Enter a colour as #RRGGBB.";
            return;
        }

        apply(hex);
        await RememberColorAsync(hex);
    }

    /// <summary>A custom colour goes to the front of the palette, here and in the next message.</summary>
    private async Task RememberColorAsync(string hex)
    {
        if (!Palette.Contains(hex, StringComparer.OrdinalIgnoreCase)) Palette.Insert(0, hex);
        try
        {
            var saved = await _settings.GetTextStyleAsync();
            saved.RecentColors.RemoveAll(c => c.Equals(hex, StringComparison.OrdinalIgnoreCase));
            saved.RecentColors.Insert(0, hex);
            if (saved.RecentColors.Count > 8) saved.RecentColors.RemoveRange(8, saved.RecentColors.Count - 8);
            await _settings.SaveTextStyleAsync(saved);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Saving the recent colours failed");
        }
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
            Palette = new ObservableCollection<string>(TextStyle.RecentColors
                .Concat(TextStyleSettings.Palette).Distinct(StringComparer.OrdinalIgnoreCase));
            UndoSendDelay = TimeSpan.FromSeconds((await _settings.GetGeneralAsync()).UndoSendSeconds);
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
        MarkClean();
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

        var body = await LoadBodyAsync(original);
        var originalText = !string.IsNullOrWhiteSpace(body?.TextPlain)
            ? body!.TextPlain
            : Strider.Infrastructure.Security.HtmlText.ToPlainText(body?.TextHtml);

        if (mode == ComposeMode.EditDraft)
        {
            // The saved text already holds the signature, and saving replaces this very draft.
            To = AddressList.ToInput(AddressList.FromJson(original.ToAddresses));
            Cc = AddressList.ToInput(AddressList.FromJson(original.CcAddresses));
            ShowCcBcc = !string.IsNullOrEmpty(Cc);
            Subject = original.Subject;
            BodyPlain = originalText ?? "";
            IncludeSignature = false;
            _draftUid = original.MessageUid;
            _inReplyTo = ReplyBuilder.StripBrackets(original.InReplyTo);
            _references = AddressList.FromJson(original.References);
            WindowTitle = "Edit draft";
            RefreshPreview();
            MarkClean();
            return;
        }

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
            await AddForwardedAttachmentsAsync(original);
        }
        else
        {
            Subject = ReplyBuilder.ReplySubject(original.Subject);
            BodyPlain = "\n\n" + ReplyBuilder.QuoteHeader(original, forward: false) + ReplyBuilder.QuotePlain(originalText);
            _inReplyTo = ReplyBuilder.StripBrackets(original.MessageId);
            _references = ReplyBuilder.References(original);
            WindowTitle = "Reply";
        }

        RefreshPreview();
        MarkClean();
    }

    /// <summary>Downloads the body if the original was never opened (a shortcut reply, say).</summary>
    private async Task<MessageBody?> LoadBodyAsync(Message original)
    {
        var cached = await _messageStore.GetBodyAsync(original.Id);
        if (cached != null || _sync == null) return cached;
        try
        {
            return await _sync.EnsureBodyAsync(original);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not load the original message");
            StatusMessage = "The original message could not be loaded, so it is not quoted.";
            return null;
        }
    }

    /// <summary>A forward carries the attachments of the original, within the size limit.</summary>
    private async Task AddForwardedAttachmentsAsync(Message original)
    {
        if (_sync == null || !original.HasAttachments) return;
        try
        {
            foreach (var attachment in await _sync.EnsureAttachmentsAsync(original))
            {
                var data = await _sync.DownloadAttachmentAsync(original, attachment);
                if (Attachments.Sum(a => (long)a.Content.Length) + data.Length > MaxTotalAttachmentBytes)
                {
                    StatusMessage = $"{attachment.Filename} not attached: attachments would exceed 25 MB";
                    continue;
                }
                Attachments.Add(new AttachmentData
                {
                    Filename = attachment.Filename ?? "attachment",
                    ContentType = attachment.ContentType ?? "application/octet-stream",
                    Content = data,
                });
            }
            OnPropertyChanged(nameof(AttachmentsSummary));
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not load the attachments to forward");
            StatusMessage = $"Attachments not forwarded: {ex.Message}";
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
            SelectedSignature = list.FirstOrDefault(s => s.IsDefault) ?? list.FirstOrDefault();
            SignatureAsAttachment = SelectedSignature?.AttachAsHtmlFile ?? false;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not load signatures");
        }
    }

    partial void OnSelectedSignatureChanged(Signature? value)
    {
        SignatureAsAttachment = value?.AttachAsHtmlFile ?? false;
        RefreshPreview();
    }

    partial void OnSignatureAsAttachmentChanged(bool value) => RefreshPreview();

    partial void OnIncludeSignatureChanged(bool value) => RefreshPreview();

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
            if (!IncludeSignature || SignatureAsAttachment || SelectedSignature is null) return "";
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

        if (IncludeSignature && !SignatureAsAttachment && SelectedSignature is not null)
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
        if (!IncludeSignature || SignatureAsAttachment || SelectedSignature is null) return _bodyText;

        var signature = !string.IsNullOrWhiteSpace(SelectedSignature.ContentPlain)
            ? SelectedSignature.ContentPlain
            : Strider.UI.Rendering.HtmlInlinesBuilder.ToPlainText(SelectedSignature.ContentHtml);
        if (string.IsNullOrWhiteSpace(signature)) return _bodyText;

        return _bodyText.TrimEnd() + "\n\n-- \n" + signature;
    }

    /// <summary>Builds the .html attachment when the user asked for one.</summary>
    private List<AttachmentData> BuildAttachments()
    {
        var list = Attachments.ToList();

        if (IncludeSignature && SignatureAsAttachment && SelectedSignature is not null)
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

        var accountId = SelectedAccount.Id;
        IsSending = true;
        try
        {
            // Built now: what was on screen when Send was pressed is what goes out,
            // even if the window is closed during the undo window.
            var message = BuildMessage(to, cc, bcc);
            if (!await WaitOutUndoWindowAsync()) return;

            StatusMessage = "Sending...";
            await _actions.SendAsync(accountId, message);
            if (_draftUid is { } uid)
            {
                try { await _actions.DeleteDraftAsync(accountId, uid); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Sent, but removing the draft failed"); }
            }
            _finished = true;
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

    /// <summary>Holds the message back for the undo window (F4.7). False when the user took it back.</summary>
    private async Task<bool> WaitOutUndoWindowAsync()
    {
        if (UndoSendDelay <= TimeSpan.Zero) return true;

        using var undo = new CancellationTokenSource();
        _undoSend = undo;
        IsSendPending = true;
        StatusMessage = $"Sending in {UndoSendDelay.TotalSeconds:0} s. Press Undo to keep editing.";
        try
        {
            await Task.Delay(UndoSendDelay, undo.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Sending cancelled.";
            return false;
        }
        finally
        {
            IsSendPending = false;
            _undoSend = null;
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
            MarkClean();
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
            // Keep the quote that LoadFromAsync prepared; only the part above it is replaced.
            var quoteIndex = _bodyText.IndexOf("---------- ", StringComparison.Ordinal);
            var quote = quoteIndex >= 0 ? _bodyText[quoteIndex..] : "";
            SetBodyText(result.Text.TrimEnd() + "\n\n" + quote);
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

    /// <summary>
    /// Takes back a pending send; otherwise closes. Unsaved text needs a second press:
    /// one stray Esc must not throw a letter away (NFR 6.3).
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        if (IsSendPending)
        {
            _undoSend?.Cancel();
            return;
        }

        if (IsDirty && !_discardArmed)
        {
            _discardArmed = true;
            StatusMessage = "Unsaved changes. Cancel again to discard them, or Save draft.";
            return;
        }

        _finished = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void RemoveAttachment(AttachmentData attachment)
    {
        Attachments.Remove(attachment);
        OnPropertyChanged(nameof(AttachmentsSummary));
    }
}
