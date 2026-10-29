using Avalonia.Controls;
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
        if (DataContext is ComposeViewModel vm)
            vm.CloseRequested += Close;
    }
}
