using System.Windows.Controls;
using System.Windows.Input;

namespace Skua.WPF.Views;

/// <summary>
/// Interaction logic for HotKeysView.xaml
/// </summary>
public partial class HotKeysView : UserControl
{
    public HotKeysView()
    {
        InitializeComponent();
    }

    private void ComboBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ComboBox cb && e.OriginalSource is TextBox)
        {
            cb.IsDropDownOpen = true;
        }
    }

    private void ComboBox_KeyUp(object sender, KeyEventArgs e)
    {
        if (sender is ComboBox cb && !cb.IsDropDownOpen)
        {
            cb.IsDropDownOpen = true;
        }
    }
}