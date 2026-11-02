using Avalonia.Controls;
using Avalonia.Interactivity;
using Skua.App.Avalonia.UserControls;

namespace Skua.App.Avalonia.Views;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
    }

    // Copy lives on the tab row; the open tab knows which lines are selected.
    private async void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (TabContent.Presenter?.Child is LogTabUserControl tab)
            await tab.CopySelectedOrAllAsync();
    }

    private void DeselectButton_Click(object? sender, RoutedEventArgs e)
    {
        if (TabContent.Presenter?.Child is LogTabUserControl tab)
            tab.DeselectAll();
    }
}
