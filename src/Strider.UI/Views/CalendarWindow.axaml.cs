using Avalonia.Controls;
using Strider.Core.Domain;
using Strider.UI.ViewModels;

namespace Strider.UI.Views;

public partial class CalendarWindow : Window
{
    public CalendarWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireEditor();
    }

    /// <summary>
    /// The ViewModel never opens windows itself; it asks for an editor through a
    /// delegate. This keeps the calendar testable headlessly.
    /// </summary>
    private void WireEditor()
    {
        if (DataContext is not CalendarViewModel vm) return;

        // Load as soon as the window gets its ViewModel, whoever opened it.
        _ = vm.LoadMonthAsync();

        vm.EditEventInteraction = async draft =>
        {
            var editorVm = new EventEditorViewModel(draft);
            var editor = new EventEditorWindow { DataContext = editorVm };
            var result = await editor.ShowDialog<CalendarEvent?>(this);
            return result;
        };
    }
}
