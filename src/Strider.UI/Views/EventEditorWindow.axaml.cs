using Avalonia.Controls;
using Strider.Core.Domain;
using Strider.UI.ViewModels;

namespace Strider.UI.Views;

public partial class EventEditorWindow : Window
{
    public EventEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not EventEditorViewModel vm) return;
            vm.CloseRequested += result =>
            {
                // ShowDialog<CalendarEvent?> needs an explicit typed result.
                Close(result);
            };
        };
    }
}
