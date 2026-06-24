using CommunityToolkit.Mvvm.Messaging;
using System;

namespace Skua.WPF;

/// <summary>
/// Interaction logic for HostWindow.xaml
/// </summary>
public partial class HostWindow : CustomWindow
{
    public HostWindow()
    {
        InitializeComponent();
        Closed += HostWindow_Closed;
    }

    private void HostWindow_Closed(object? sender, System.EventArgs e)
    {
        Closed -= HostWindow_Closed;
        if (DataContext is not null)
        {
            StrongReferenceMessenger.Default.UnregisterAll(DataContext);
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        DataContext = null;
    }
}
