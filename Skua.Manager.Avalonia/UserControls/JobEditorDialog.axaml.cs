using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Skua.Manager.Avalonia.UserControls;

public partial class JobEditorDialog : UserControl
{
    public JobEditorDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
