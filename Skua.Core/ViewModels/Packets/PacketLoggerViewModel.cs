using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using System.Collections.ObjectModel;
using Skua.Core.Utils;

namespace Skua.Core.ViewModels;

public partial class PacketLoggerViewModel : BotControlViewModelBase
{
    public PacketLoggerViewModel(IEnumerable<PacketLogFilterViewModel> filters, IFlashUtil flash, IFileDialogService fileDialog, IDispatcherService dispatcherService)
        : base("Packet Logger")
    {
        _flash = flash;
        _fileDialog = fileDialog;
        _dispatcherService = dispatcherService;
        _packetFilters = filters.ToList();
        _batchTimer = new System.Timers.Timer(500);
        _batchTimer.Elapsed += (s, e) => FlushPackets();
    }

    private readonly IFlashUtil _flash;
    private readonly IFileDialogService _fileDialog;
    private readonly IDispatcherService _dispatcherService;
    private readonly System.Timers.Timer _batchTimer;
    private readonly List<string> _packetQueue = new();

    [ObservableProperty]
    private RangedObservableCollection<string> _packetLogs = new();

    [ObservableProperty]
    private List<PacketLogFilterViewModel> _packetFilters;

    private bool _isReceivingPackets;

    public bool IsReceivingPackets
    {
        get => _isReceivingPackets;
        set
        {
            if (SetProperty(ref _isReceivingPackets, value))
                ToggleLogger();
        }
    }

    [RelayCommand]
    private void SavePacketLogs()
    {
        _fileDialog.SaveText(_packetLogs);
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _packetFilters.ForEach(f => f.IsChecked = false);
    }

    [RelayCommand]
    private void ClearPacketLogs()
    {
        PacketLogs.Clear();
    }

    private void ToggleLogger()
    {
        if (_isReceivingPackets)
        {
            _batchTimer.Start();
            _flash.FlashCall += LogPackets;
        }
        else
        {
            _batchTimer.Stop();
            _flash.FlashCall -= LogPackets;
            FlushPackets();
        }
    }

    private bool _filterEnabled
    {
        get
        {
            foreach (PacketLogFilterViewModel filter in _packetFilters)
            {
                if (!filter.IsChecked)
                    return true;
            }
            return false;
        }
    }

    private void LogPackets(string function, object[] args)
    {
        if (function != "packet")
            return;

        if (!_filterEnabled)
        {
            lock (_packetQueue)
            {
                _packetQueue.Add(args[0].ToString()!);
            }
            return;
        }

        string[] packet = args[0].ToString()!.Split('%', StringSplitOptions.RemoveEmptyEntries);
        foreach (PacketLogFilterViewModel filterVM in _packetFilters)
        {
            if (!filterVM.IsChecked && filterVM.Filter.Invoke(packet))
                return;
        }

        lock (_packetQueue)
        {
            _packetQueue.Add(args[0].ToString()!);
        }
    }

    private void FlushPackets()
    {
        List<string> toAdd;
        lock (_packetQueue)
        {
            if (_packetQueue.Count == 0) return;
            toAdd = _packetQueue.ToList();
            _packetQueue.Clear();
        }
        _dispatcherService.Invoke(() => 
        {
            PacketLogs.AddRange(toAdd);
            if (PacketLogs.Count > 1000)
                PacketLogs.RemoveRange(0, PacketLogs.Count - 1000);
        });
    }
}