using Avalonia.Controls;

namespace Strider.UI.Views;

public partial class SettingsWindow : Window
{
    // Without this call the compiled XAML is never applied and the
    // window/control renders empty (bug found 29.10.2026).
    public SettingsWindow()
    {
        InitializeComponent();
    }
}
