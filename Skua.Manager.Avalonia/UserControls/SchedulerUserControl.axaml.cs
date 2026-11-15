using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Skua.Manager.Avalonia.UserControls;

public partial class SchedulerUserControl : UserControl
{
    public SchedulerUserControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
