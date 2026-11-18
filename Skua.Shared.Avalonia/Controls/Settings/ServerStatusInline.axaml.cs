using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Skua.Core.Models.Servers;
using System.ComponentModel;

namespace Skua.Shared.Avalonia.Controls.Settings;

public partial class ServerStatusInline : UserControl
{
    public static readonly StyledProperty<Server?> ServerProperty =
        AvaloniaProperty.Register<ServerStatusInline, Server?>(nameof(Server));

    // The server whose PropertyChanged we listen to. Only while on screen, so a long-lived cached Server never
    // keeps a discarded row alive.
    private Server? _observedServer;
    private bool _isAttached;

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
        {
            if (_isAttached)
                ObserveServer(Server);
            UpdateLatencyClass();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        ObserveServer(Server);
        UpdateLatencyClass();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        ObserveServer(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void ObserveServer(Server? server)
    {
        if (ReferenceEquals(_observedServer, server))
            return;

        if (_observedServer is not null)
            _observedServer.PropertyChanged -= Server_PropertyChanged;
        _observedServer = server;
        if (_observedServer is not null)
            _observedServer.PropertyChanged += Server_PropertyChanged;
    }

    private void Server_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Skua.Core.Models.Servers.Server.LatencyStatus))
            return;

        // Latency is measured off the UI thread.
        if (Dispatcher.UIThread.CheckAccess())
            UpdateLatencyClass();
        else
            Dispatcher.UIThread.Post(UpdateLatencyClass);
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
