using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Utils;

namespace Skua.Core.ViewModels;

public partial class LogTabViewModel : ObservableRecipient
{
    public LogTabViewModel(string title, ILogService logService, IDispatcherService dispatcherService, LogType logType)
    {
        Messenger.Register<LogTabViewModel, AddLogMessage>(this, AddLog);

        Title = title;
        _logService = logService;
        _dispatcherService = dispatcherService;
        _logType = logType;

        _batchTimer = new System.Timers.Timer(500);
        _batchTimer.Elapsed += (s, e) => FlushLogs();
        _batchTimer.Start();
    }

    private readonly ILogService _logService;
    private readonly IDispatcherService _dispatcherService;
    private readonly LogType _logType;
    private readonly System.Timers.Timer _batchTimer;
    private readonly List<string> _logQueue = new();

    public string Title { get; }
    public RangedObservableCollection<string> Logs { get; } = new();

    [RelayCommand]
    private void SaveLog()
    {
        Ioc.Default.GetRequiredService<IFileDialogService>().SaveText(LogsToString(Logs));
    }

    [RelayCommand]
    private void ClearLog()
    {
        _logService.ClearLog(_logType);
        Logs.Clear();
    }

    [RelayCommand]
    private void CopyLog()
    {
        Ioc.Default.GetRequiredService<IClipboardService>().SetText(LogsToString(Logs));
    }

    private void AddLog(LogTabViewModel recipient, AddLogMessage message)
    {
        if (message.LogType != recipient._logType)
            return;

        lock (recipient._logQueue)
        {
            recipient._logQueue.Add(message.Text);
        }
    }

    private void FlushLogs()
    {
        List<string> toAdd;
        lock (_logQueue)
        {
            if (_logQueue.Count == 0) return;
            toAdd = _logQueue.ToList();
            _logQueue.Clear();
        }

        _dispatcherService.Invoke(() =>
        {
            Logs.AddRange(toAdd);
            if (Logs.Count > 1000)
                Logs.RemoveRange(0, Logs.Count - 1000);
        });
    }

    private static string LogsToString(IEnumerable<string> logs)
    {
        return string.Join(Environment.NewLine, logs);
    }
}