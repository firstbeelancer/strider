using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Strider.UI.ViewModels;

namespace Strider.UI.Views;

public partial class ComposeWindow : Window
{
    // Without InitializeComponent the compiled XAML is never applied and the
    // window renders empty (bug found 29.10.2026).
    public ComposeWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ComposeViewModel vm) return;
        vm.CloseRequested += Close;
        vm.PickFiles = PickFilesAsync;
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
