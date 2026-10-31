using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Strider.UI.ViewModels;

namespace Strider.UI.Views;

public partial class MainWindow : Window
{
    // Without InitializeComponent the compiled XAML is never applied and the
    // window renders empty (bug found 29.10.2026).
    public MainWindow()
    {
        InitializeComponent();
        // Tunnel: the list would otherwise use the arrow-free letters for itself first.
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var typing = FocusManager?.GetFocusedElement() is TextBox;
        if (vm.HandleShortcut(e.Key, e.KeyModifiers, typing)) e.Handled = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainWindowViewModel vm) return;
        vm.MessageReader.PickSaveFile = PickSaveFileAsync;
        vm.FocusSearchRequested += () => this.FindControl<TextBox>("SearchBox")?.Focus();
    }

    private async Task<Stream?> PickSaveFileAsync(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save attachment",
            SuggestedFileName = suggestedName,
        });
        return file == null ? null : await file.OpenWriteAsync();
    }
}
