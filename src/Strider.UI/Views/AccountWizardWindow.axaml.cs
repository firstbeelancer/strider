using Avalonia.Controls;
using Strider.UI.ViewModels;

namespace Strider.UI.Views;

public partial class AccountWizardWindow : Window
{
    // Without InitializeComponent the compiled XAML is never applied and the
    // window renders empty (bug found 29.10.2026).
    public AccountWizardWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is AccountWizardViewModel vm)
            vm.CloseRequested += Close;
    }
}
