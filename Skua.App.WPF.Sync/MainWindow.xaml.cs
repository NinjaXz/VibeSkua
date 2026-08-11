using Skua.WPF;

namespace Skua.App.WPF.Sync;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public sealed partial class MainWindow : CustomWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Title = global::Skua.AppInfo.Title;
        TitleText = global::Skua.AppInfo.Title;
    }
}