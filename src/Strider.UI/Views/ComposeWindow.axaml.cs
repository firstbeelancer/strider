using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Strider.UI.ViewModels;

namespace Strider.UI.Views;

public partial class ComposeWindow : Window
{
    // Without InitializeComponent the compiled XAML is never applied and the
    // window renders empty (bug found 29.10.2026).
    public ComposeWindow()
    {
        InitializeComponent();
        WatchSelection();
        // Tunnel: the body text box would otherwise take Ctrl+Enter as a line break.
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);

        _autoSave.Tick += async (_, _) =>
        {
            if (DataContext is ComposeViewModel vm) await vm.AutoSaveAsync();
        };
        Opened += (_, _) => _autoSave.Start();
        Closed += (_, _) => _autoSave.Stop();
    }

    /// <summary>F4.5: the draft is saved every 30 seconds while there are changes.</summary>
    private readonly DispatcherTimer _autoSave = new() { Interval = TimeSpan.FromSeconds(30) };

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is ComposeViewModel vm && vm.HandleShortcut(e.Key, e.KeyModifiers)) e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        // Closed with the X: the text is saved, never silently dropped (NFR 6.3).
        if (DataContext is ComposeViewModel { ShouldSaveOnClose: true } vm)
            _ = vm.SaveDraftCommand.ExecuteAsync(null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ComposeViewModel vm) return;
        vm.CloseRequested += Close;
        vm.PickFiles = PickFilesAsync;
    }

    /// <summary>
    /// The text box cannot bind its selection to the ViewModel (Avalonia exposes
    /// no two-way binding that survives caret moves), so the formatting commands
    /// are fed from here: every caret/selection change updates
    /// <see cref="ComposeViewModel.SelectionStart"/> and
    /// <see cref="ComposeViewModel.SelectionLength"/>.
    /// </summary>
    private void WatchSelection()
    {
        var editor = this.FindControl<TextBox>("BodyEditor");
        if (editor is null) return;

        editor.PropertyChanged += (_, e) =>
        {
            if (DataContext is not ComposeViewModel vm) return;

            if (e.Property == TextBox.SelectionStartProperty ||
                e.Property == TextBox.SelectionEndProperty)
            {
                vm.SelectionStart = editor.SelectionStart;
                vm.SelectionLength = Math.Max(0, editor.SelectionEnd - editor.SelectionStart);
            }
            else if (e.Property == TextBox.CaretIndexProperty && vm.SelectionLength == 0)
            {
                // Caret moves without a selection (arrow keys) still move the start.
                vm.SelectionStart = editor.CaretIndex;
            }
        };
    }

    private async Task<IReadOnlyList<(string Name, byte[] Data)>> PickFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Attach files",
            AllowMultiple = true,
        });
        var result = new List<(string, byte[])>();
        foreach (var f in files)
        {
            await using var s = await f.OpenReadAsync();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            result.Add((f.Name, ms.ToArray()));
        }
        return result;
    }
}
