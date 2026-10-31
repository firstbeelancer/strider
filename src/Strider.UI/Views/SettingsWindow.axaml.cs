using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Strider.UI.ViewModels;

namespace Strider.UI.Views;

public partial class SettingsWindow : Window
{
    // Without this call the compiled XAML is never applied and the
    // window/control renders empty (bug found 29.10.2026).
    public SettingsWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not SettingsViewModel vm) return;

        // "Add font..." in the Fonts & Text tab (Wave 6): the ViewModel stays
        // free of Avalonia's storage API, the window supplies the picker.
        vm.PickFontFile = PickFontAsync;
        vm.PickImageFile = PickImageAsync;
    }

    private async Task<(string Name, byte[] Data)?> PickImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add logo",
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
        });

        var file = files.FirstOrDefault();
        if (file is null) return null;

        await using var stream = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return (file.Name, buffer.ToArray());
    }

    private async Task<string?> PickFontAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add font",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Fonts")
                {
                    Patterns = new[] { "*.ttf", "*.otf", "*.ttc", "*.otc" },
                    MimeTypes = new[] { "font/ttf", "font/otf", "font/collection" },
                },
            },
        });

        var file = files.FirstOrDefault();
        if (file is null) return null;

        // TryGetLocalPath is Windows/Linux only; on a sandboxed store path we
        // fall back to copying the file into the app font folder first.
        return file.TryGetLocalPath();
    }
}
