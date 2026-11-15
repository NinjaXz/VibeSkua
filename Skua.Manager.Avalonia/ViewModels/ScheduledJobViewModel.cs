using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Manager.Avalonia.Services;

namespace Skua.Manager.Avalonia.ViewModels;

public partial class ScheduledJobViewModel : ObservableObject
{
    private readonly ISchedulerService _schedulerService;
    private readonly ISettingsService? _settingsService;

    public ScheduledJob Job { get; }

    public ScheduledJobViewModel(ScheduledJob job, ISchedulerService schedulerService, ISettingsService? settingsService = null)
    {
        Job = job;
        _schedulerService = schedulerService;
        _settingsService = settingsService ?? Ioc.Default.GetService<ISettingsService>();
        _isEnabled = job.IsEnabled;
        _name = job.Name;
        _description = job.Description;

        History = new ObservableCollection<ScheduleExecutionItemViewModel>(
            (job.History ?? new()).Select(r => new ScheduleExecutionItemViewModel(r, _settingsService, SelectExecution))
        );
        LiveLogs = new ObservableCollection<string>();
        StructuredLogs = new ObservableCollection<ScheduleLogEntry>();

        RefreshDisplayStrings(true);
    }

    [ObservableProperty]
    private string _name;

    partial void OnNameChanged(string value)
    {
        Job.Name = value;
        _schedulerService.SaveJobs();
    }

    [ObservableProperty]
    private string? _description;

    partial void OnDescriptionChanged(string? value)
    {
        Job.Description = value;
        _schedulerService.SaveJobs();
    }

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        Job.IsEnabled = value;
        RefreshDisplayStrings();
        _schedulerService.SaveJobs();
    }

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpandIconKind));
    }

    public string ExpandIconKind => IsExpanded ? "ChevronUp" : "ChevronDown";

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    private string _statusBrush = "#9E9E9E";

    [ObservableProperty]
    private string _nextRunCountdownText = string.Empty;

    [ObservableProperty]
    private string _nextRunFormatted = string.Empty;

    [ObservableProperty]
    private string _targetDisplayText = string.Empty;

    [ObservableProperty]
    private string _targetTooltipText = string.Empty;

    [ObservableProperty]
    private string _pipelineDisplayText = string.Empty;

    [ObservableProperty]
    private string _scriptsTooltipText = string.Empty;

    [ObservableProperty]
    private string _triggerDisplayText = string.Empty;

    [ObservableProperty]
    private string _triggerTooltipText = string.Empty;

    [ObservableProperty]
    private string _lastRunSummary = "Last Run: Never";

    [ObservableProperty]
    private string _lastRunDisplayText = "Last Run: Never";

    [ObservableProperty]
    private string _lastRunTooltipText = "This task has not been executed yet.";

    public ObservableCollection<ScheduleExecutionItemViewModel> History { get; }
    public ObservableCollection<string> LiveLogs { get; }
    public ObservableCollection<ScheduleLogEntry> StructuredLogs { get; }

    [ObservableProperty]
    private ScheduleExecutionItemViewModel? _selectedExecution;

    partial void OnSelectedExecutionChanged(ScheduleExecutionItemViewModel? value)
    {
        foreach (var item in History)
            item.IsSelected = (item == value);

        OnPropertyChanged(nameof(IsViewingHistoricalRun));
        OnPropertyChanged(nameof(ActiveLogs));
        OnPropertyChanged(nameof(ActiveLogsCountText));
        OnPropertyChanged(nameof(ActiveConsoleTitle));
        OnPropertyChanged(nameof(HasActiveLogs));
    }

    public bool IsViewingHistoricalRun => SelectedExecution != null;

    public ObservableCollection<ScheduleLogEntry> ActiveLogs => 
        SelectedExecution != null ? SelectedExecution.ExecutionLogs : StructuredLogs;

    public bool HasActiveLogs => ActiveLogs.Count > 0;

    public string ActiveLogsCountText => ActiveLogs.Count > 0 ? $"{ActiveLogs.Count}" : string.Empty;

    public string ActiveConsoleTitle => SelectedExecution != null
        ? $"Logs for Run · {SelectedExecution.FormattedTime}"
        : "Live Output & Diagnostics";

    [RelayCommand]
    public void SelectExecution(ScheduleExecutionItemViewModel? item)
    {
        if (SelectedExecution == item)
            SelectedExecution = null;
        else
            SelectedExecution = item;
    }

    [RelayCommand]
    public void ReturnToLiveLogs()
    {
        SelectedExecution = null;
    }

    public bool HasHistory => History.Count > 0;
    public bool HasStructuredLogs => StructuredLogs.Count > 0;
    public string HistoryCountText => History.Count > 0 ? $"{History.Count}" : string.Empty;
    public string LogsCountText => StructuredLogs.Count > 0 ? $"{StructuredLogs.Count}" : string.Empty;

    [ObservableProperty]
    private string _copyLogsButtonText = "Copy";

    public void AddLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        string timeStr = DateTime.Now.ToString("HH:mm:ss");
        string rawLine = $"[{timeStr}] {message}";
        var entry = CreateLogEntry(timeStr, message);

        Dispatcher.UIThread.Post(() =>
        {
            LiveLogs.Add(rawLine);
            if (LiveLogs.Count > 150)
                LiveLogs.RemoveAt(0);

            StructuredLogs.Add(entry);
            if (StructuredLogs.Count > 150)
                StructuredLogs.RemoveAt(0);

            OnPropertyChanged(nameof(HasStructuredLogs));
            OnPropertyChanged(nameof(LogsCountText));
            if (!IsViewingHistoricalRun)
            {
                OnPropertyChanged(nameof(ActiveLogs));
                OnPropertyChanged(nameof(ActiveLogsCountText));
                OnPropertyChanged(nameof(HasActiveLogs));
            }
        });
    }

    public static ScheduleLogEntry CreateLogEntry(string timestamp, string message)
    {
        string trimmed = message.Trim();
        string category = "INFO";
        string fgColor = "#E2E8F0"; // Crisp light slate
        string badgeBg = "#15FFFFFF";
        string badgeText = "#94A3B8";
        FontWeight weight = FontWeight.Normal;
        string displayMessage = trimmed;

        if (trimmed.StartsWith("--- Job") || (trimmed.StartsWith("---") && trimmed.EndsWith("---")))
        {
            category = "START";
            displayMessage = trimmed.Trim('-', ' ');
            fgColor = "#38BDF8"; // Vibrant sky blue
            badgeBg = "#2538BDF8";
            badgeText = "#38BDF8";
            weight = FontWeight.SemiBold;
        }
        else if (trimmed.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Contains("abnormally with code", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Contains("Failed to", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Contains("timed out after", StringComparison.OrdinalIgnoreCase))
        {
            category = "ERROR";
            if (trimmed.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                displayMessage = trimmed.Substring(6).Trim();
            fgColor = "#F87171"; // Coral red
            badgeBg = "#25F87171";
            badgeText = "#F87171";
            weight = FontWeight.SemiBold;
        }
        else if (trimmed.Contains("finished successfully", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Contains("All sequential script steps completed", StringComparison.OrdinalIgnoreCase))
        {
            category = "SUCCESS";
            fgColor = "#4ADE80"; // Bright mint green
            badgeBg = "#254ADE80";
            badgeText = "#4ADE80";
            weight = FontWeight.SemiBold;
        }
        else if (trimmed.Contains("Starting '", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("[Script ", StringComparison.OrdinalIgnoreCase))
        {
            category = "STEP";
            fgColor = "#818CF8"; // Soft indigo
            badgeBg = "#25818CF8";
            badgeText = "#818CF8";
            weight = FontWeight.SemiBold;
        }
        else if (trimmed.Contains("Cooldown", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Contains("Waiting for", StringComparison.OrdinalIgnoreCase))
        {
            category = "WAIT";
            fgColor = "#FBBF24"; // Amber
            badgeBg = "#25FBBF24";
            badgeText = "#FBBF24";
        }
        else if (trimmed.Contains("Retrying", StringComparison.OrdinalIgnoreCase))
        {
            category = "RETRY";
            fgColor = "#FB923C"; // Orange
            badgeBg = "#25FB923C";
            badgeText = "#FB923C";
            weight = FontWeight.SemiBold;
        }
        else if (trimmed.Contains("stopped by user", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Contains("Execution cancelled", StringComparison.OrdinalIgnoreCase))
        {
            category = "STOP";
            fgColor = "#FB923C"; // Orange
            badgeBg = "#25FB923C";
            badgeText = "#FB923C";
            weight = FontWeight.SemiBold;
        }
        else if (trimmed.Contains("Client process", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Contains("Launching client", StringComparison.OrdinalIgnoreCase))
        {
            category = "CLIENT";
            fgColor = "#94A3B8"; // Slate
            badgeBg = "#2094A3B8";
            badgeText = "#94A3B8";
        }
        else if (trimmed.Contains("Army Queue", StringComparison.OrdinalIgnoreCase))
        {
            category = "ARMY";
            fgColor = "#C084FC"; // Purple
            badgeBg = "#20C084FC";
            badgeText = "#C084FC";
        }

        return new ScheduleLogEntry
        {
            Timestamp = timestamp,
            Category = category,
            Message = displayMessage,
            ForegroundColor = fgColor,
            BadgeBgColor = badgeBg,
            BadgeTextColor = badgeText,
            FontWeight = weight
        };
    }

    public void RecordExecution(DateTime startTime, DateTime endTime, string status, string? message)
    {
        TimeSpan duration = endTime - startTime;
        var record = new ScheduleExecutionRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = startTime,
            Duration = duration,
            Status = status,
            Message = message,
            Logs = LiveLogs.TakeLast(100).ToList()
        };

        Job.LastRunTime = startTime;
        Job.LastDuration = duration;
        Job.LastResult = status;

        var itemVm = new ScheduleExecutionItemViewModel(record, _settingsService, SelectExecution);

        Dispatcher.UIThread.Post(() =>
        {
            History.Insert(0, itemVm);
            if (History.Count > 20)
                History.RemoveAt(History.Count - 1);

            Job.History = History.Select(h => h.Record).ToList();
            OnPropertyChanged(nameof(HasHistory));
            OnPropertyChanged(nameof(HistoryCountText));
            RefreshDisplayStrings(true);
            _schedulerService.SaveJobs();
        });
    }

    public void SetStatus(string status, string brush)
    {
        StatusText = status;
        StatusBrush = brush;
        IsRunning = status.StartsWith("Running", StringComparison.OrdinalIgnoreCase);
    }

    public void RefreshFromJob()
    {
        if (Name != Job.Name)
            Name = Job.Name;
        if (Description != Job.Description)
            Description = Job.Description;
        if (IsEnabled != Job.IsEnabled)
            IsEnabled = Job.IsEnabled;

        RefreshDisplayStrings(true);
    }

    public void RefreshDisplayStrings() => RefreshDisplayStrings(true, null);

    public void RefreshDisplayStrings(bool fullRefresh, bool? override24H = null)
    {
        if (Name != Job.Name)
            Name = Job.Name;
        if (Description != Job.Description)
            Description = Job.Description;
        if (IsEnabled != Job.IsEnabled)
            IsEnabled = Job.IsEnabled;

        bool use24H = override24H ?? (_settingsService?.Get("SchedulerUse24HourTime", false) ?? false);
        TargetDisplayText = Job.Target.TargetType switch
        {
            ScheduleTargetType.SingleAccount => $"Account: {Job.Target.TargetName ?? "None"}",
            ScheduleTargetType.AccountGroup => $"Group: {Job.Target.TargetName ?? "None"}",
            ScheduleTargetType.ArmyQueuePreset => $"Army Preset: {Job.Target.TargetName ?? "None"}",
            ScheduleTargetType.MultipleAccounts => $"{Job.Target.TargetAccounts.Count} Accounts",
            _ => "Unknown Target"
        };

        TargetTooltipText = Job.Target.TargetType switch
        {
            ScheduleTargetType.SingleAccount => $"Target: Single Account ({Job.Target.TargetName ?? "None"})\nServer: {Job.Target.Server ?? "Twilly"}",
            ScheduleTargetType.AccountGroup => $"Target: Account Group ({Job.Target.TargetName ?? "None"})\nServer: {Job.Target.Server ?? "Twilly"}",
            ScheduleTargetType.ArmyQueuePreset => $"Target: Army Queue Preset ({Job.Target.TargetName ?? "None"})",
            ScheduleTargetType.MultipleAccounts => $"Target: {Job.Target.TargetAccounts.Count} Accounts\nAccounts: {string.Join(", ", Job.Target.TargetAccounts)}\nServer: {Job.Target.Server ?? "Twilly"}",
            _ => "No target specified"
        };

        // 2. Scripts Display & Tooltip
        PipelineDisplayText = Job.Pipeline.PipelineMode switch
        {
            SchedulePipelineMode.PlaylistFile => !string.IsNullOrWhiteSpace(Job.Pipeline.PlaylistPath)
                ? $"Playlist: {Path.GetFileName(Job.Pipeline.PlaylistPath)}"
                : "Playlist: None selected",
            SchedulePipelineMode.CustomSteps => Job.Pipeline.Steps.Count == 1 ? "1 Script" : $"{Job.Pipeline.Steps.Count} Scripts",
            _ => "No Scripts Configured"
        };

        ScriptsTooltipText = Job.Pipeline.PipelineMode switch
        {
            SchedulePipelineMode.PlaylistFile => !string.IsNullOrWhiteSpace(Job.Pipeline.PlaylistPath)
                ? $"Playlist File:\n{Job.Pipeline.PlaylistPath}"
                : "No playlist file selected",
            SchedulePipelineMode.CustomSteps => Job.Pipeline.Steps.Count > 0
                ? "Configured Scripts in Sequence:\n" + string.Join(Environment.NewLine, Job.Pipeline.Steps.Select((s, i) => $"{i + 1}. {(!string.IsNullOrWhiteSpace(s.Name) ? s.Name : Path.GetFileNameWithoutExtension(s.ScriptPath))}"))
                : "No scripts configured",
            _ => "No scripts configured"
        };

        // 3. Trigger Display & Tooltip
        string timeStr = FormatTimeOfDay(Job.Trigger.TimeOfDay, use24H);

        TriggerDisplayText = Job.Trigger.TriggerType switch
        {
            ScheduleTriggerType.Once => Job.Trigger.SpecificDateTime.HasValue
                ? $"Once at {FormatDateTime(Job.Trigger.SpecificDateTime.Value.LocalDateTime, use24H)}"
                : "Once (Date not set)",
            ScheduleTriggerType.Daily => $"Daily at {timeStr}",
            ScheduleTriggerType.Weekly => $"Weekly ({(Job.Trigger.DaysOfWeek.Count == 7 ? "Everyday" : string.Join(", ", Job.Trigger.DaysOfWeek.Select(d => d.ToString().Substring(0, 3))))}) at {timeStr}",
            ScheduleTriggerType.Interval => $"Every {Job.Trigger.IntervalMinutes} minutes",
            ScheduleTriggerType.DailyResetSync => Job.Trigger.ServerResetOffsetMinutes == 0
                ? "Daily Reset (00:00 EST)"
                : Job.Trigger.ServerResetOffsetMinutes > 0
                    ? $"Daily Reset +{Job.Trigger.ServerResetOffsetMinutes}m (EST)"
                    : $"Daily Reset {Job.Trigger.ServerResetOffsetMinutes}m (EST)",
            ScheduleTriggerType.WeeklyResetSync => Job.Trigger.ServerResetOffsetMinutes == 0
                ? "Weekly Reset (Fri 00:00 EST)"
                : Job.Trigger.ServerResetOffsetMinutes > 0
                    ? $"Weekly Reset +{Job.Trigger.ServerResetOffsetMinutes}m (EST)"
                    : $"Weekly Reset {Job.Trigger.ServerResetOffsetMinutes}m (EST)",
            _ => "Manual"
        };

        TriggerTooltipText = Job.Trigger.TriggerType switch
        {
            ScheduleTriggerType.Once => Job.Trigger.SpecificDateTime.HasValue
                ? $"Runs once on: {FormatDateTimeFull(Job.Trigger.SpecificDateTime.Value.LocalDateTime, use24H)}"
                : "One-time execution",
            ScheduleTriggerType.Daily => $"Recurring daily at {timeStr} local time.",
            ScheduleTriggerType.Weekly => $"Runs weekly on: {string.Join(", ", Job.Trigger.DaysOfWeek)} at {timeStr} local time.",
            ScheduleTriggerType.Interval => $"Runs continuously every {Job.Trigger.IntervalMinutes} minutes.",
            ScheduleTriggerType.DailyResetSync => $"Synchronized with AQWorlds Daily Server Reset (Midnight 00:00 EST / UTC-5).\nOffset: {Job.Trigger.ServerResetOffsetMinutes} minute(s).",
            ScheduleTriggerType.WeeklyResetSync => $"Synchronized with AQWorlds Weekly Server Reset (Friday Midnight 00:00 EST / UTC-5, when Ultra Boss insignias & weekly quests refresh).\nOffset: {Job.Trigger.ServerResetOffsetMinutes} minute(s).",
            _ => "Schedule trigger"
        };

        // 4. Next Run (Precise countdown formatting without 1m offset)
        if (!IsEnabled)
        {
            NextRunCountdownText = "Disabled";
            NextRunFormatted = "Schedule paused";
            SetStatus("Disabled", "#9E9E9E");
        }
        else if (Job.NextRunTime.HasValue)
        {
            DateTime next = Job.NextRunTime.Value;
            TimeSpan delta = next - DateTime.Now;
            if (delta.TotalSeconds <= 0)
            {
                NextRunCountdownText = "Due Now";
            }
            else if (delta.TotalDays >= 1)
            {
                NextRunCountdownText = $"In {(int)delta.TotalDays}d {delta.Hours}h";
            }
            else if (delta.TotalHours >= 1)
            {
                NextRunCountdownText = $"In {(int)delta.TotalHours}h {delta.Minutes}m";
            }
            else if (delta.Minutes >= 1)
            {
                NextRunCountdownText = $"In {delta.Minutes}m {delta.Seconds:D2}s";
            }
            else
            {
                NextRunCountdownText = $"In {delta.Seconds}s";
            }

            NextRunFormatted = use24H
                ? $"{next:ddd, MMM d @ HH:mm:ss}"
                : $"{next:ddd, MMM d @ h:mm:ss tt}";

            if (!IsRunning)
            {
                SetStatus("Scheduled", "#2196F3");
            }
        }
        else
        {
            NextRunCountdownText = "Calculating...";
            NextRunFormatted = string.Empty;
        }

        // 5. Last Run Summary & Display
        if (Job.LastRunTime.HasValue)
        {
            string durStr = Job.LastDuration.HasValue
                ? $" · {(int)Job.LastDuration.Value.TotalMinutes}m {Job.LastDuration.Value.Seconds}s"
                : string.Empty;
            string resStr = !string.IsNullOrWhiteSpace(Job.LastResult) ? $" · {Job.LastResult}" : string.Empty;
            string timePart = use24H ? $"{Job.LastRunTime.Value:MMM d, HH:mm}" : $"{Job.LastRunTime.Value:MMM d, h:mm tt}";
            LastRunSummary = $"Last Run: {timePart}{durStr}{resStr}";
            LastRunDisplayText = $"Last Run: {timePart}{resStr}";
            LastRunTooltipText = $"Last executed on: {FormatDateTimeFull(Job.LastRunTime.Value, use24H)}\nStatus: {Job.LastResult ?? "Completed"}\nDuration: {(Job.LastDuration.HasValue ? $"{(int)Job.LastDuration.Value.TotalMinutes}m {Job.LastDuration.Value.Seconds}s" : "N/A")}";
        }
        else
        {
            LastRunSummary = "Last Run: Never";
            LastRunDisplayText = "Last Run: Never";
            LastRunTooltipText = "This task has not been executed yet.";
        }

        if (fullRefresh)
        {
            foreach (var h in History)
                h.Refresh(use24H);

            if (SelectedExecution != null)
            {
                SelectedExecution.Refresh(use24H);
                OnPropertyChanged(nameof(ActiveConsoleTitle));
            }
        }
    }

    private static string FormatTimeOfDay(TimeSpan tod, bool use24H)
    {
        DateTime dt = DateTime.Today.Add(tod);
        return use24H ? dt.ToString("HH:mm") : dt.ToString("h:mm tt");
    }

    private static string FormatDateTime(DateTime dt, bool use24H)
    {
        return use24H ? dt.ToString("yyyy-MM-dd HH:mm") : dt.ToString("yyyy-MM-dd h:mm tt");
    }

    private static string FormatDateTimeFull(DateTime dt, bool use24H)
    {
        return use24H ? dt.ToString("dddd, MMMM d, yyyy HH:mm:ss") : dt.ToString("dddd, MMMM d, yyyy h:mm:ss tt");
    }

    [RelayCommand]
    private async Task RunNow()
    {
        await _schedulerService.RunJobNowAsync(this);
    }

    [RelayCommand]
    private void StopJob()
    {
        _schedulerService.AbortJob(this);
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LiveLogs.Clear();
        StructuredLogs.Clear();
        OnPropertyChanged(nameof(HasStructuredLogs));
        OnPropertyChanged(nameof(LogsCountText));
        if (!IsViewingHistoricalRun)
        {
            OnPropertyChanged(nameof(ActiveLogs));
            OnPropertyChanged(nameof(ActiveLogsCountText));
            OnPropertyChanged(nameof(HasActiveLogs));
        }
    }

    [RelayCommand]
    private void ClearHistory()
    {
        SelectedExecution = null;
        History.Clear();
        Job.History = new();
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HistoryCountText));
        RefreshDisplayStrings(true);
        _schedulerService.SaveJobs();
    }

    [RelayCommand]
    private async Task CopyLogs()
    {
        var logsToCopy = ActiveLogs;
        if (logsToCopy.Count == 0 && LiveLogs.Count == 0)
            return;

        string text = string.Join(Environment.NewLine, logsToCopy.Select(l => l.RawEntry));
        if (string.IsNullOrWhiteSpace(text) && !IsViewingHistoricalRun)
            text = string.Join(Environment.NewLine, LiveLogs);

        bool copied = false;
        try
        {
            var clip = Ioc.Default.GetService<IClipboardService>();
            if (clip != null)
            {
                clip.SetText(text);
                copied = true;
            }
        }
        catch { }

        if (!copied)
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
                {
                    await desktop.MainWindow.Clipboard.SetTextAsync(text);
                    copied = true;
                }
            }
            catch { }
        }

        if (copied)
        {
            CopyLogsButtonText = "Copied!";
            _ = Task.Delay(2000).ContinueWith(_ =>
            {
                Dispatcher.UIThread.Post(() => CopyLogsButtonText = "Copy");
            });
        }
    }
}

public class ScheduleLogEntry
{
    public string Timestamp { get; init; } = string.Empty;
    public string Category { get; init; } = "INFO";
    public string Message { get; init; } = string.Empty;
    public string ForegroundColor { get; init; } = "#E2E8F0";
    public string BadgeBgColor { get; init; } = "#15FFFFFF";
    public string BadgeTextColor { get; init; } = "#94A3B8";
    public FontWeight FontWeight { get; init; } = FontWeight.Normal;
    public string RawEntry => $"[{Timestamp}] [{Category}] {Message}";
    public override string ToString() => RawEntry;
}

public partial class ScheduleExecutionItemViewModel : ObservableObject
{
    private readonly ISettingsService? _settingsService;
    private readonly Action<ScheduleExecutionItemViewModel>? _onSelect;
    public ScheduleExecutionRecord Record { get; }

    public ObservableCollection<ScheduleLogEntry> ExecutionLogs { get; } = new();

    public ScheduleExecutionItemViewModel(
        ScheduleExecutionRecord record,
        ISettingsService? settingsService = null,
        Action<ScheduleExecutionItemViewModel>? onSelect = null)
    {
        Record = record;
        _settingsService = settingsService;
        _onSelect = onSelect;

        PopulateExecutionLogs();
        Refresh();
    }

    private void PopulateExecutionLogs()
    {
        if (Record.Logs != null && Record.Logs.Count > 0)
        {
            foreach (var line in Record.Logs)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string timePart = Record.Timestamp.ToString("HH:mm:ss");
                string msgPart = line;

                if (line.StartsWith('[') && line.Length >= 10 && line[9] == ']')
                {
                    timePart = line.Substring(1, 8);
                    msgPart = line.Substring(10).Trim();
                }

                ExecutionLogs.Add(ScheduledJobViewModel.CreateLogEntry(timePart, msgPart));
            }
        }

        if (ExecutionLogs.Count == 0)
        {
            string timePart = Record.Timestamp.ToString("HH:mm:ss");
            ExecutionLogs.Add(ScheduledJobViewModel.CreateLogEntry(
                timePart,
                $"--- Execution record: {Record.Status} (Duration: {FormattedDuration}) ---"
            ));
            if (HasMessage)
            {
                ExecutionLogs.Add(ScheduledJobViewModel.CreateLogEntry(
                    timePart,
                    $"ERROR: {Record.Message}"
                ));
            }
            else
            {
                ExecutionLogs.Add(ScheduledJobViewModel.CreateLogEntry(
                    timePart,
                    $"Run completed with status '{Record.Status}'. No detailed console logs were captured for this session."
                ));
            }
        }
    }

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select()
    {
        _onSelect?.Invoke(this);
    }

    public string Status => Record.Status ?? "Completed";
    public string? Message => Record.Message;
    public bool HasMessage => !string.IsNullOrWhiteSpace(Record.Message);

    public string StatusColor => Status.ToLowerInvariant() switch
    {
        "completed" or "success" => "#4ADE80",
        "failed" or "error" => "#F87171",
        "stopped" or "cancelled" or "aborted" => "#FB923C",
        "running" => "#38BDF8",
        _ => "#94A3B8"
    };

    public string StatusBgColor => Status.ToLowerInvariant() switch
    {
        "completed" or "success" => "#204ADE80",
        "failed" or "error" => "#25F87171",
        "stopped" or "cancelled" or "aborted" => "#25FB923C",
        "running" => "#2538BDF8",
        _ => "#15FFFFFF"
    };

    public string StatusBorderColor => Status.ToLowerInvariant() switch
    {
        "completed" or "success" => "#404ADE80",
        "failed" or "error" => "#45F87171",
        "stopped" or "cancelled" or "aborted" => "#40FB923C",
        "running" => "#4038BDF8",
        _ => "#20FFFFFF"
    };

    public string StatusIcon => Status.ToLowerInvariant() switch
    {
        "completed" or "success" => "CheckCircleOutline",
        "failed" or "error" => "AlertCircleOutline",
        "stopped" or "cancelled" or "aborted" => "StopCircleOutline",
        "running" => "ClockOutline",
        _ => "InformationOutline"
    };

    public string FormattedDuration => Record.Duration.TotalHours >= 1
        ? $"{(int)Record.Duration.TotalHours}h {Record.Duration.Minutes}m {Record.Duration.Seconds}s"
        : Record.Duration.TotalMinutes >= 1
            ? $"{(int)Record.Duration.TotalMinutes}m {Record.Duration.Seconds}s"
            : $"{Math.Max(0, Record.Duration.Seconds)}s";

    [ObservableProperty]
    private string _formattedTime = string.Empty;

    [ObservableProperty]
    private string _tooltip = string.Empty;

    public void Refresh(bool? use24H = null)
    {
        bool is24H = use24H ?? (_settingsService?.Get("SchedulerUse24HourTime", false) ?? false);
        FormattedTime = is24H
            ? Record.Timestamp.ToString("MMM d, HH:mm")
            : Record.Timestamp.ToString("MMM d, h:mm tt");

        string fullTime = is24H
            ? Record.Timestamp.ToString("dddd, MMMM d, yyyy HH:mm:ss")
            : Record.Timestamp.ToString("dddd, MMMM d, yyyy h:mm:ss tt");

        Tooltip = $"Execution Time: {fullTime}\nStatus: {Status}\nDuration: {FormattedDuration}" +
                  (HasMessage ? $"\nDetails: {Message}" : string.Empty);
    }
}
