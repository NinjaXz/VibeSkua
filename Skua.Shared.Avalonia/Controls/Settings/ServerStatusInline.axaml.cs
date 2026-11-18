using Avalonia;
using Avalonia.Controls;
using Skua.Core.Models.Servers;

namespace Skua.Shared.Avalonia.Controls.Settings;

public partial class ServerStatusInline : UserControl
{
    public static readonly StyledProperty<Server?> ServerProperty =
        AvaloniaProperty.Register<ServerStatusInline, Server?>(nameof(Server));

    public ServerStatusInline()
    {
        InitializeComponent();
    }

    public Server? Server
    {
        get => GetValue(ServerProperty);
        set => SetValue(ServerProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ServerProperty)
            UpdateLatencyClass();
    }

    private void UpdateLatencyClass()
    {
        if (NameText is null)
            return;

        ServerLatencyStatus status = Server?.LatencyStatus ?? ServerLatencyStatus.Unknown;
        NameText.Classes.Set("good", status == ServerLatencyStatus.Good);
        NameText.Classes.Set("near", status == ServerLatencyStatus.NearThreshold);
        NameText.Classes.Set("high", status == ServerLatencyStatus.High);
        NameText.Classes.Set("unavailable", status == ServerLatencyStatus.Unavailable);
        ToolTip.SetTip(NameText, status switch
        {
            ServerLatencyStatus.Good => "Latency: good",
            ServerLatencyStatus.NearThreshold => "Latency: close to your limit",
            ServerLatencyStatus.High => "Latency: above your limit",
            ServerLatencyStatus.Unavailable => "Latency: no response",
            _ => "Latency: not measured yet"
        });
    }
}
