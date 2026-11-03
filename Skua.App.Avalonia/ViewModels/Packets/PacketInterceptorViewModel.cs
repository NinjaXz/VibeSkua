using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Servers;
using Skua.Core.Utils;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.ViewModels.Packets;

public partial class PacketInterceptorViewModel : BotControlViewModelBase
{
    private readonly ICaptureProxy _gameProxy;
    private readonly IScriptServers _server;
    private readonly IFileDialogService _fileDialog;
    private readonly InterceptorLogger _logger;
    private readonly List<PacketLogFilterViewModel> _allPacketFilters;

    [ObservableProperty]
    private Server? _selectedServer;

    [ObservableProperty]
    private RangedObservableCollection<InterceptedPacketViewModel> _packets = new();

    [ObservableProperty]
    private ObservableCollection<InterceptedPacketViewModel> _filteredPackets = new();

    [ObservableProperty]
    private string _packetSearchText = string.Empty;

    private bool _isLogging;

    public bool IsLogging
    {
        get => _isLogging;
        set
        {
            if (SetProperty(ref _isLogging, value))
            {
                lock (_gameProxy.Interceptors)
                {
                    if (value)
                        _gameProxy.Interceptors.Add(_logger);
                    else
                        _gameProxy.Interceptors.Remove(_logger);
                }
            }
        }
    }

    [ObservableProperty]
    private ObservableCollection<Server> _serverList = new();

    public bool Running => _gameProxy.Running;
    public IReadOnlyList<PacketLogFilterViewModel> PacketFilters => _allPacketFilters;

    // "shown / total" for the filter button
    public string ShownFiltersText => $"{_allPacketFilters.Count(f => f.IsChecked)} / {_allPacketFilters.Count}";

    public PacketInterceptorViewModel(IEnumerable<PacketLogFilterViewModel> filters, ICaptureProxy gameProxy, IScriptServers server, IFileDialogService fileDialog)
        : base("Packet Interceptor")
    {
        _gameProxy = gameProxy;
        _server = server;
        _fileDialog = fileDialog;
        _allPacketFilters = filters.ToList();

        foreach (var filter in _allPacketFilters)
        {
            filter.PropertyChanged += OnFilterPropertyChanged;
        }

        _logger = new InterceptorLogger(OnPacketIntercepted);
        IsLogging = true;

        LoadServersAsync();
    }

    [RelayCommand]
    private async Task RefreshServersAsync()
    {
        await LoadServersInternalAsync(forceUpdate: true);
    }

    private async void LoadServersAsync()
    {
        await LoadServersInternalAsync(forceUpdate: false);
    }

    private async Task LoadServersInternalAsync(bool forceUpdate)
    {
        List<Server>? servers = null;
        try
        {
            servers = await _server.GetServers(forceUpdate);
        }
        catch
        {
            // fallback
        }

        if (servers == null || servers.Count == 0)
        {
            servers = new List<Server>
            {
                new() { Name = "Artix", IP = "game.aq.com", Port = 5588 },
                new() { Name = "Galanoth", IP = "game.aq.com", Port = 5588 },
                new() { Name = "Safiria", IP = "game.aq.com", Port = 5588 },
                new() { Name = "Twilly", IP = "game.aq.com", Port = 5588 },
                new() { Name = "Twig", IP = "game.aq.com", Port = 5588 },
                new() { Name = "Yorumi", IP = "game.aq.com", Port = 5588 },
                new() { Name = "Yokai", IP = "game.aq.com", Port = 5588 },
                new() { Name = "Espada", IP = "game.aq.com", Port = 5588 },
                new() { Name = "Sir Ver", IP = "game.aq.com", Port = 5588 }
            };
        }

        Dispatcher.UIThread.Post(() =>
        {
            ServerList = new ObservableCollection<Server>(servers);
            if (SelectedServer == null && ServerList.Count > 0)
                SelectedServer = ServerList[0];
        });
    }

    private void OnPacketIntercepted(InterceptedPacketViewModel packet)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Packets.Add(packet);
            if (Packets.Count > 1000)
            {
                // FilteredPackets keeps Packets' order, so the oldest packet can only be its first item
                InterceptedPacketViewModel evicted = Packets[0];
                Packets.RemoveAt(0);
                if (FilteredPackets.Count > 0 && ReferenceEquals(FilteredPackets[0], evicted))
                    FilteredPackets.RemoveAt(0);
            }

            if (MatchesPacket(packet))
                FilteredPackets.Add(packet);
        });
    }

    partial void OnPacketSearchTextChanged(string value)
    {
        RefreshFilteredPackets();
    }

    private void OnFilterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PacketLogFilterViewModel.IsChecked))
        {
            OnPropertyChanged(nameof(ShownFiltersText));
            RefreshFilteredPackets();
        }
    }

    private bool MatchesPacket(InterceptedPacketViewModel pkt)
    {
        if (!string.IsNullOrWhiteSpace(PacketSearchText) &&
            !pkt.Packet.Contains(PacketSearchText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A packet can be in several categories (a ct with auras is Combat and Auras); any ticked one shows it
        string[] commands = pkt.Commands;
        foreach (PacketLogFilterViewModel filter in _allPacketFilters)
        {
            if (filter.IsChecked && filter.Filter.Invoke(commands))
                return true;
        }

        return false;
    }

    public void RefreshFilteredPackets()
    {
        FilteredPackets.Clear();
        foreach (var pkt in Packets)
        {
            if (MatchesPacket(pkt))
            {
                FilteredPackets.Add(pkt);
            }
        }
    }

    [RelayCommand]
    private void ClearPackets()
    {
        Packets.Clear();
        FilteredPackets.Clear();
    }

    // Every captured packet, filters and search ignored, one "[OUT] packet" line each; line breaks inside a
    // packet are written as \r and \n so each line stays one packet
    [RelayCommand]
    private void ExportPackets()
    {
        _fileDialog.SaveText(Packets.Select(p => $"[{p.Direction}] {p.Packet.Replace("\r", "\\r").Replace("\n", "\\n")}").ToList());
    }

    [RelayCommand]
    private void ClearFilters()
    {
        foreach (var filter in _allPacketFilters)
        {
            filter.IsChecked = false;
        }
        RefreshFilteredPackets();
    }

    [RelayCommand]
    private void SelectAllFilters()
    {
        foreach (var filter in _allPacketFilters)
        {
            filter.IsChecked = true;
        }
        RefreshFilteredPackets();
    }

    [RelayCommand]
    private void InvertFilters()
    {
        foreach (var filter in _allPacketFilters)
        {
            filter.IsChecked = !filter.IsChecked;
        }
        RefreshFilteredPackets();
    }

    [RelayCommand]
    private void ConnectInterceptor()
    {
        if (_gameProxy.Running)
        {
            _gameProxy.Stop();
            OnPropertyChanged(nameof(Running));
            return;
        }

        if (SelectedServer is null)
            return;

        IScriptOption options = Ioc.Default.GetRequiredService<IScriptOption>();
        bool relogin = options.AutoRelogin;
        options.AutoRelogin = false;

        try
        {
            IPAddress ip = IPAddress.TryParse(SelectedServer.IP, out IPAddress? address)
                ? address
                : Dns.GetHostAddresses(SelectedServer.IP).FirstOrDefault()
                    ?? throw new InvalidOperationException($"Could not resolve server address: {SelectedServer.IP}");

            int port = SelectedServer.Port != 0 ? SelectedServer.Port : 5588;
            _gameProxy.Destination = new IPEndPoint(ip, port);
            _gameProxy.Start();

            // Do not disconnect the active game session if the local proxy failed to bind.
            if (!_gameProxy.Running)
                return;

            _server.Logout();
            _server.Login();
            _server.ConnectIP("127.0.0.1", port);
            OnPropertyChanged(nameof(Running));
        }
        finally
        {
            options.AutoRelogin = relogin;
        }
    }

    protected override void OnActivated()
    {
        Messenger.Register<PacketInterceptorViewModel, PropertyChangedMessage<bool>>(this, RunningChanged);
        OnPropertyChanged(nameof(Running));
        if (ServerList.Count == 0)
            LoadServersAsync();
    }

    private void RunningChanged(PacketInterceptorViewModel recipient, PropertyChangedMessage<bool> message)
    {
        if (message.PropertyName == nameof(ICaptureProxy.Running))
            recipient.OnPropertyChanged(nameof(recipient.Running));
    }

    ~PacketInterceptorViewModel()
    {
        foreach (var filter in _allPacketFilters)
        {
            filter.PropertyChanged -= OnFilterPropertyChanged;
        }
        _logger?.Dispose();
    }
}

public class InterceptorLogger : IInterceptor, IDisposable
{
    public static readonly bool LogToFile = false;

    private readonly Action<InterceptedPacketViewModel> _addFunc;
    private readonly StreamWriter? _logWriter;
    private readonly object _logLock = new();

    public int Priority => int.MaxValue;

    public InterceptorLogger(Action<InterceptedPacketViewModel> addFunc)
    {
        _addFunc = addFunc;
        if (LogToFile)
        {
            string logPath = Path.Combine(ClientFileSources.SkuaDIR, "InterceptedLogs.txt");
            _logWriter = new StreamWriter(logPath, append: true) { AutoFlush = true };
        }
    }

    public void Intercept(MessageInfo message, bool outbound)
    {
        _addFunc(new(message.Content, message.Send ? outbound : null));

        if (LogToFile)
        {
            lock (_logLock)
            {
                string direction = message.Send ? (outbound ? "OUT" : "IN") : "UNKNOWN";
                _logWriter?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{direction}] {message.Content}");
            }
        }
    }

    public void Dispose()
    {
        if (_logWriter != null)
        {
            _logWriter?.Dispose();
        }
    }
}
