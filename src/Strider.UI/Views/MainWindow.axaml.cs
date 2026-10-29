using Avalonia.Controls;
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
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
            vm.MessageReader.PickSaveFile = PickSaveFileAsync;
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
