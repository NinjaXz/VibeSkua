using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Shared.Avalonia.ViewModels.ScriptRepo;

namespace Skua.Manager.Avalonia.ViewModels;

public partial class DayOfWeekSelection : ObservableObject
{
    public DayOfWeek Day { get; }
    public string DisplayName { get; }

    public DayOfWeekSelection(DayOfWeek day)
    {
        Day = day;
        DisplayName = day.ToString().Substring(0, 3);
    }

    [ObservableProperty]
    private bool _isSelected;
}

public partial class AccountCheckItem : ObservableObject
{
    public string Username { get; }
    public string DisplayName { get; }

    public AccountCheckItem(string username, string displayName, bool isSelected = false)
    {
        Username = username;
        DisplayName = !string.IsNullOrWhiteSpace(displayName) ? $"{username} ({displayName})" : username;
        _isSelected = isSelected;
    }

    [ObservableProperty]
    private bool _isSelected;
}

public partial class ScheduleScriptStepViewModel : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _scriptPath = string.Empty;

    public string Storage => $"Queue_{Path.GetFileNameWithoutExtension(ScriptPath)}_{Id}";
}

public partial class JobEditorDialogViewModel : ObservableObject
{
    private readonly ScheduledJob _originalJob;
    private readonly IFileDialogService _fileDialogService;
    private readonly IDialogService _dialogService;
    private readonly IScriptManager? _scriptManager;
    private readonly ISettingsService? _settingsService;

    public Action? CloseAction { get; set; }
    public bool DialogResult { get; private set; }

    public JobEditorDialogViewModel(
        ScheduledJob job,
        AccountManagerViewModel accountManager,
        ArmyQueueViewModel armyQueue,
        IFileDialogService fileDialogService,
        IDialogService dialogService,
        IScriptManager? scriptManager = null,
        ISettingsService? settingsService = null)
    {
        _originalJob = job;
        _fileDialogService = fileDialogService;
        _dialogService = dialogService;
        _scriptManager = scriptManager ?? Ioc.Default.GetService<IScriptManager>();
        _settingsService = settingsService ?? Ioc.Default.GetService<ISettingsService>();

        // 1. General
        _name = job.Name;
        _description = job.Description;
        _isEnabled = job.IsEnabled;

        // 2. Trigger
        _selectedTriggerType = job.Trigger.TriggerType;
        _use24HourFormat = _settingsService?.Get("SchedulerUse24HourTime", false) ?? false;
        _timeHours = job.Trigger.TimeOfDay.Hours;
        _timeMinutes = job.Trigger.TimeOfDay.Minutes;
        _selectedAmPmIndex = _timeHours >= 12 ? 1 : 0;
        int h12 = _timeHours % 12;
        _timeHours12 = h12 == 0 ? 12 : h12;

        _intervalMinutes = job.Trigger.IntervalMinutes > 0 ? job.Trigger.IntervalMinutes : 60;
        _serverResetOffsetMinutes = job.Trigger.ServerResetOffsetMinutes;
        _specificDate = job.Trigger.SpecificDateTime ?? DateTimeOffset.Now.AddHours(1);

        DaysOfWeekOptions = new ObservableCollection<DayOfWeekSelection>
        {
            new(DayOfWeek.Monday) { IsSelected = job.Trigger.DaysOfWeek.Contains(DayOfWeek.Monday) },
            new(DayOfWeek.Tuesday) { IsSelected = job.Trigger.DaysOfWeek.Contains(DayOfWeek.Tuesday) },
            new(DayOfWeek.Wednesday) { IsSelected = job.Trigger.DaysOfWeek.Contains(DayOfWeek.Wednesday) },
            new(DayOfWeek.Thursday) { IsSelected = job.Trigger.DaysOfWeek.Contains(DayOfWeek.Thursday) },
            new(DayOfWeek.Friday) { IsSelected = job.Trigger.DaysOfWeek.Contains(DayOfWeek.Friday) },
            new(DayOfWeek.Saturday) { IsSelected = job.Trigger.DaysOfWeek.Contains(DayOfWeek.Saturday) },
            new(DayOfWeek.Sunday) { IsSelected = job.Trigger.DaysOfWeek.Contains(DayOfWeek.Sunday) },
        };

        // 3. Target
        _selectedTargetType = job.Target.TargetType;
        _server = !string.IsNullOrWhiteSpace(job.Target.Server) ? job.Target.Server : "Twilly";
        if (!AvailableServers.Contains(_server))
            AvailableServers.Insert(0, _server);

        AvailableAccounts = new ObservableCollection<string>(
            accountManager.Accounts.Select(a => a.Username).OrderBy(u => u));
        AvailableGroups = new ObservableCollection<string>(
            accountManager.Groups.Select(g => g.Name).OrderBy(n => n));
        AvailableArmyPresets = new ObservableCollection<string>(
            armyQueue.Presets.OrderBy(p => p));

        _selectedAccount = _selectedTargetType == ScheduleTargetType.SingleAccount && !string.IsNullOrWhiteSpace(job.Target.TargetName)
            ? job.Target.TargetName
            : AvailableAccounts.FirstOrDefault();

        _selectedGroup = _selectedTargetType == ScheduleTargetType.AccountGroup && !string.IsNullOrWhiteSpace(job.Target.TargetName)
            ? job.Target.TargetName
            : AvailableGroups.FirstOrDefault();

        _selectedArmyPreset = _selectedTargetType == ScheduleTargetType.ArmyQueuePreset && !string.IsNullOrWhiteSpace(job.Target.TargetName)
            ? job.Target.TargetName
            : AvailableArmyPresets.FirstOrDefault();

        var selectedAccountsSet = new HashSet<string>(job.Target.TargetAccounts ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        SelectableAccounts = new ObservableCollection<AccountCheckItem>(
            accountManager.Accounts
                .OrderBy(a => a.Username)
                .Select(a => new AccountCheckItem(a.Username, a.DisplayName, selectedAccountsSet.Contains(a.Username))));

        // 4. Scripts
        _selectedPipelineMode = job.Pipeline.PipelineMode;
        _playlistPath = job.Pipeline.PlaylistPath;
        _restartClientBetweenScripts = job.Pipeline.RestartClientBetweenScripts;
        Steps = new ObservableCollection<ScheduleScriptStepViewModel>(
            job.Pipeline.Steps.Select(s => new ScheduleScriptStepViewModel
            {
                Id = s.Id != Guid.Empty ? s.Id : Guid.NewGuid(),
                ScriptPath = s.ScriptPath,
                Name = !string.IsNullOrWhiteSpace(s.Name) ? s.Name : Path.GetFileNameWithoutExtension(s.ScriptPath)
            }));

        // 5. Policy & Post-Run
        _selectedConflictPolicy = job.ConflictPolicy;
        _exitClientOnFinish = job.PostRun.ExitClientOnFinish;
        _notifyOnStart = job.PostRun.NotifyOnStart;
        _notifyOnFinish = job.PostRun.NotifyOnFinish;
        _notifyOnError = job.PostRun.NotifyOnError;
        _selectedPowerAction = job.PostRun.PowerAction;

        // Register for Script Repo "Queue" button broadcast
        StrongReferenceMessenger.Default.Register<JobEditorDialogViewModel, QueueScriptMessage, int>(this,
            (int)MessageChannels.ScriptStatus, (r, m) => r.HandleQueueScript(m));
    }

    #region Observable Properties

    // General
    [ObservableProperty] private string _name;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private bool _isEnabled;

    // Trigger
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnceTrigger))]
    [NotifyPropertyChangedFor(nameof(IsDailyTrigger))]
    [NotifyPropertyChangedFor(nameof(IsWeeklyTrigger))]
    [NotifyPropertyChangedFor(nameof(IsIntervalTrigger))]
    [NotifyPropertyChangedFor(nameof(IsDailyResetTrigger))]
    [NotifyPropertyChangedFor(nameof(IsWeeklyResetTrigger))]
    [NotifyPropertyChangedFor(nameof(IsServerResetTrigger))]
    [NotifyPropertyChangedFor(nameof(IsClockTimeTrigger))]
    private ScheduleTriggerType _selectedTriggerType;

    public bool IsOnceTrigger => SelectedTriggerType == ScheduleTriggerType.Once;
    public bool IsDailyTrigger => SelectedTriggerType == ScheduleTriggerType.Daily;
    public bool IsWeeklyTrigger => SelectedTriggerType == ScheduleTriggerType.Weekly;
    public bool IsIntervalTrigger => SelectedTriggerType == ScheduleTriggerType.Interval;
    public bool IsDailyResetTrigger => SelectedTriggerType == ScheduleTriggerType.DailyResetSync;
    public bool IsWeeklyResetTrigger => SelectedTriggerType == ScheduleTriggerType.WeeklyResetSync;
    public bool IsServerResetTrigger => IsDailyResetTrigger || IsWeeklyResetTrigger;
    public bool IsClockTimeTrigger => IsOnceTrigger || IsDailyTrigger || IsWeeklyTrigger;

    [ObservableProperty]
    private bool _use24HourFormat;

    partial void OnUse24HourFormatChanged(bool value)
    {
        _settingsService?.Set("SchedulerUse24HourTime", value);
        OnPropertyChanged(nameof(TimePreviewText));
    }

    [ObservableProperty] private int _timeHours;
    [ObservableProperty] private int _timeHours12 = 12;
    [ObservableProperty] private int _timeMinutes;
    [ObservableProperty] private int _selectedAmPmIndex;

    private bool _isSyncingTime;

    partial void OnTimeHoursChanged(int value)
    {
        if (_isSyncingTime) return;
        _isSyncingTime = true;
        try
        {
            int clamped = Math.Clamp(value, 0, 23);
            _timeHours = clamped;
            _selectedAmPmIndex = clamped >= 12 ? 1 : 0;
            int h12 = clamped % 12;
            TimeHours12 = h12 == 0 ? 12 : h12;
            OnPropertyChanged(nameof(SelectedAmPmIndex));
            OnPropertyChanged(nameof(TimePreviewText));
        }
        finally
        {
            _isSyncingTime = false;
        }
    }

    partial void OnTimeHours12Changed(int value)
    {
        if (_isSyncingTime) return;
        _isSyncingTime = true;
        try
        {
            int clamped12 = Math.Clamp(value, 1, 12);
            _timeHours12 = clamped12;
            TimeHours = Convert12To24(clamped12, SelectedAmPmIndex == 1);
            OnPropertyChanged(nameof(TimePreviewText));
        }
        finally
        {
            _isSyncingTime = false;
        }
    }

    partial void OnSelectedAmPmIndexChanged(int value)
    {
        if (_isSyncingTime) return;
        _isSyncingTime = true;
        try
        {
            TimeHours = Convert12To24(TimeHours12, value == 1);
            OnPropertyChanged(nameof(TimePreviewText));
        }
        finally
        {
            _isSyncingTime = false;
        }
    }

    partial void OnTimeMinutesChanged(int value)
    {
        OnPropertyChanged(nameof(TimePreviewText));
    }

    private static int Convert12To24(int hour12, bool isPm)
    {
        int h = Math.Clamp(hour12, 1, 12);
        if (h == 12)
            return isPm ? 12 : 0;
        return isPm ? h + 12 : h;
    }

    public string TimePreviewText
    {
        get
        {
            int h24 = Math.Clamp(TimeHours, 0, 23);
            int m = Math.Clamp(TimeMinutes, 0, 59);
            string time24 = $"{h24:D2}:{m:D2}";

            int h12 = h24 % 12 == 0 ? 12 : h24 % 12;
            string ampm = h24 >= 12 ? "PM" : "AM";
            string time12 = $"{h12:D2}:{m:D2} {ampm}";

            return Use24HourFormat
                ? $"Active Time: {time24} ({time12})"
                : $"Active Time: {time12} ({time24})";
        }
    }

    [ObservableProperty] private int _intervalMinutes;
    [ObservableProperty] private int _serverResetOffsetMinutes;
    [ObservableProperty] private DateTimeOffset? _specificDate;

    public ObservableCollection<DayOfWeekSelection> DaysOfWeekOptions { get; }

    // Target
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSingleAccountTarget))]
    [NotifyPropertyChangedFor(nameof(IsGroupTarget))]
    [NotifyPropertyChangedFor(nameof(IsArmyPresetTarget))]
    [NotifyPropertyChangedFor(nameof(IsMultipleAccountsTarget))]
    private ScheduleTargetType _selectedTargetType;

    public bool IsSingleAccountTarget => SelectedTargetType == ScheduleTargetType.SingleAccount;
    public bool IsGroupTarget => SelectedTargetType == ScheduleTargetType.AccountGroup;
    public bool IsArmyPresetTarget => SelectedTargetType == ScheduleTargetType.ArmyQueuePreset;
    public bool IsMultipleAccountsTarget => SelectedTargetType == ScheduleTargetType.MultipleAccounts;

    [ObservableProperty] private string? _selectedAccount;
    [ObservableProperty] private string? _selectedGroup;
    [ObservableProperty] private string? _selectedArmyPreset;
    [ObservableProperty] private string _server;

    public List<string> AvailableServers { get; } = new()
    {
        "Twilly",
        "Artix",
        "Galanoth",
        "Yorumi",
        "Safiria",
        "Espada",
        "Gravelyn",
        "Swordhaven"
    };

    public ObservableCollection<string> AvailableAccounts { get; }
    public ObservableCollection<string> AvailableGroups { get; }
    public ObservableCollection<string> AvailableArmyPresets { get; }
    public ObservableCollection<AccountCheckItem> SelectableAccounts { get; }

    // Scripts
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaylistFileMode))]
    [NotifyPropertyChangedFor(nameof(IsCustomStepsMode))]
    private SchedulePipelineMode _selectedPipelineMode;

    public bool IsPlaylistFileMode => SelectedPipelineMode == SchedulePipelineMode.PlaylistFile;
    public bool IsCustomStepsMode => SelectedPipelineMode == SchedulePipelineMode.CustomSteps;

    [ObservableProperty] private string? _playlistPath;
    [ObservableProperty] private bool _restartClientBetweenScripts;

    public ObservableCollection<ScheduleScriptStepViewModel> Steps { get; }
    [ObservableProperty] private ScheduleScriptStepViewModel? _selectedStep;

    // Policy & Post-Run
    [ObservableProperty] private ScheduleConflictPolicy _selectedConflictPolicy;
    [ObservableProperty] private bool _exitClientOnFinish;
    [ObservableProperty] private bool _notifyOnStart;
    [ObservableProperty] private bool _notifyOnFinish;
    [ObservableProperty] private bool _notifyOnError;
    [ObservableProperty] private SystemPowerAction _selectedPowerAction;

    #endregion

    #region Multi-Account Selection Commands

    [RelayCommand]
    private void SelectAllAccounts()
    {
        foreach (var acc in SelectableAccounts)
            acc.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAllAccounts()
    {
        foreach (var acc in SelectableAccounts)
            acc.IsSelected = false;
    }

    #endregion

    #region Script Commands

    [RelayCommand]
    private void OpenScriptSearch()
    {
        var windowService = Ioc.Default.GetRequiredService<IWindowService>();
        var scriptRepo = Ioc.Default.GetService<ScriptRepoViewModel>();
        if (scriptRepo != null)
        {
            windowService.RegisterManagedWindow("Script Repo", scriptRepo);
        }
        windowService.ShowManagedWindow("Script Repo");
    }

    private void HandleQueueScript(QueueScriptMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Path))
            return;

        var step = new ScheduleScriptStepViewModel
        {
            Id = Guid.NewGuid(),
            ScriptPath = message.Path,
            Name = Path.GetFileNameWithoutExtension(message.Path)
        };
        Steps.Add(step);
        SelectedStep = step;
    }

    [RelayCommand]
    private void BrowsePlaylist()
    {
        string? file = _fileDialogService.OpenFile(
            ClientFileSources.SkuaDIR,
            "Playlist Files (*.json)|*.json|All Files (*.*)|*.*");

        if (!string.IsNullOrWhiteSpace(file))
        {
            PlaylistPath = file;
        }
    }

    [RelayCommand]
    private void AddScriptStep()
    {
        string? file = _fileDialogService.OpenFile(
            ClientFileSources.SkuaScriptsDIR,
            "Script Files (*.cs)|*.cs|All Files (*.*)|*.*");

        if (!string.IsNullOrWhiteSpace(file))
        {
            var step = new ScheduleScriptStepViewModel
            {
                Id = Guid.NewGuid(),
                ScriptPath = file,
                Name = Path.GetFileNameWithoutExtension(file)
            };
            Steps.Add(step);
            SelectedStep = step;
        }
    }

    [RelayCommand]
    private async Task EditScriptOptions(ScheduleScriptStepViewModel? step)
    {
        step ??= SelectedStep;
        if (step == null || string.IsNullOrWhiteSpace(step.ScriptPath) || !File.Exists(step.ScriptPath))
            return;

        if (_scriptManager == null)
        {
            _dialogService.ShowMessageBox("Script Manager service is not available.", "Options Error");
            return;
        }

        try
        {
            object? compiled = await Task.Run(() => _scriptManager.Compile(File.ReadAllText(step.ScriptPath)));
            if (compiled == null)
            {
                _dialogService.ShowMessageBox("Script failed to compile.", "Script Error");
                return;
            }

            _scriptManager.OverrideStorage = step.Storage;
            _scriptManager.LoadScriptConfig(compiled);
            if (_scriptManager.Config != null && (_scriptManager.Config.Options.Count > 0 || _scriptManager.Config.MultipleOptions.Count > 0))
            {
                _scriptManager.Config.Configure();
            }
            else
            {
                _dialogService.ShowMessageBox("This script has no configurable options.", "Script Options");
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Script cannot be configured as it has compilation errors:\n{ex.Message}", "Script Error");
        }
        finally
        {
            _scriptManager.OverrideStorage = null;
        }
    }

    [RelayCommand]
    private void RemoveScriptStep(ScheduleScriptStepViewModel? step)
    {
        step ??= SelectedStep;
        if (step != null && Steps.Contains(step))
        {
            Steps.Remove(step);
            SelectedStep = Steps.LastOrDefault();
        }
    }

    [RelayCommand]
    private void MoveStepUp(ScheduleScriptStepViewModel? step)
    {
        step ??= SelectedStep;
        if (step == null) return;
        int index = Steps.IndexOf(step);
        if (index > 0)
        {
            Steps.Move(index, index - 1);
            SelectedStep = step;
        }
    }

    [RelayCommand]
    private void MoveStepDown(ScheduleScriptStepViewModel? step)
    {
        step ??= SelectedStep;
        if (step == null) return;
        int index = Steps.IndexOf(step);
        if (index >= 0 && index < Steps.Count - 1)
        {
            Steps.Move(index, index + 1);
            SelectedStep = step;
        }
    }

    #endregion

    #region Dialog Action Commands

    public void Cleanup()
    {
        StrongReferenceMessenger.Default.Unregister<QueueScriptMessage, int>(this, (int)MessageChannels.ScriptStatus);
    }

    [RelayCommand]
    private void Save()
    {
        // 1. Task Name Validation
        if (string.IsNullOrWhiteSpace(Name))
        {
            _dialogService.ShowMessageBox("Please enter a name for this scheduled task.", "Task Name Required");
            return;
        }

        // 2. Trigger Validation
        if (SelectedTriggerType == ScheduleTriggerType.Once)
        {
            if (!SpecificDate.HasValue)
            {
                _dialogService.ShowMessageBox("Please select a date for this one-time task.", "Trigger Date Required");
                return;
            }

            DateTime date = SpecificDate.Value.Date;
            DateTime localDateTime = new DateTime(date.Year, date.Month, date.Day, Math.Clamp(TimeHours, 0, 23), Math.Clamp(TimeMinutes, 0, 59), 0);
            DateTimeOffset fullTriggerDateTime = new DateTimeOffset(localDateTime);

            if (fullTriggerDateTime <= DateTimeOffset.Now)
            {
                _dialogService.ShowMessageBox("The one-time execution date and time must be set in the future.", "Invalid Trigger Date & Time");
                return;
            }
        }
        else if (SelectedTriggerType == ScheduleTriggerType.Weekly)
        {
            if (!DaysOfWeekOptions.Any(d => d.IsSelected))
            {
                _dialogService.ShowMessageBox("Please select at least one day of the week to run this weekly task.", "Days Required");
                return;
            }
        }
        else if (SelectedTriggerType == ScheduleTriggerType.Interval)
        {
            if (IntervalMinutes < 1)
            {
                _dialogService.ShowMessageBox("Repeat interval must be at least 1 minute.", "Invalid Interval");
                return;
            }
        }

        // 3. Target Validation
        switch (SelectedTargetType)
        {
            case ScheduleTargetType.SingleAccount:
                if (string.IsNullOrWhiteSpace(SelectedAccount) || !AvailableAccounts.Contains(SelectedAccount))
                {
                    _dialogService.ShowMessageBox("Please select an account to run this task on.\nIf you don't have any accounts added, please add one in the Account Manager tab first.", "Account Required");
                    return;
                }
                break;

            case ScheduleTargetType.AccountGroup:
                if (string.IsNullOrWhiteSpace(SelectedGroup) || !AvailableGroups.Contains(SelectedGroup))
                {
                    _dialogService.ShowMessageBox("Please select an account group for this task.\nIf you haven't created a group yet, please create one in the Account Manager tab first.", "Account Group Required");
                    return;
                }
                break;

            case ScheduleTargetType.ArmyQueuePreset:
                if (string.IsNullOrWhiteSpace(SelectedArmyPreset) || !AvailableArmyPresets.Contains(SelectedArmyPreset))
                {
                    _dialogService.ShowMessageBox("Please select an Army Queue preset for this task.\nIf you haven't created any presets yet, please create one in the Army Queue tab first.", "Army Preset Required");
                    return;
                }
                break;

            case ScheduleTargetType.MultipleAccounts:
                int selectedCount = SelectableAccounts.Count(a => a.IsSelected);
                if (selectedCount == 0)
                {
                    _dialogService.ShowMessageBox("Please select at least one account from the list for Multiple Accounts mode.", "Target Accounts Required");
                    return;
                }
                break;
        }

        // 4. Scripts Validation (Applies when not running an Army Preset)
        if (SelectedTargetType != ScheduleTargetType.ArmyQueuePreset)
        {
            if (SelectedPipelineMode == SchedulePipelineMode.CustomSteps)
            {
                if (Steps.Count == 0)
                {
                    _dialogService.ShowMessageBox("Please add at least one script to run in the 'Scripts' tab.\nYou can use 'Search Scripts...' to find scripts or click 'Add Script File' to browse a local file.", "No Scripts Configured");
                    return;
                }

                for (int i = 0; i < Steps.Count; i++)
                {
                    var step = Steps[i];
                    if (string.IsNullOrWhiteSpace(step.ScriptPath) || !File.Exists(step.ScriptPath))
                    {
                        _dialogService.ShowMessageBox($"Script #{i + 1} ('{step.Name}') does not exist on disk:\n{step.ScriptPath}\nPlease remove or re-select this script file.", "Script File Missing");
                        return;
                    }
                }
            }
            else if (SelectedPipelineMode == SchedulePipelineMode.PlaylistFile)
            {
                if (string.IsNullOrWhiteSpace(PlaylistPath))
                {
                    _dialogService.ShowMessageBox("Please select a playlist (.json) file in the 'Scripts' tab.", "Playlist File Required");
                    return;
                }

                if (!File.Exists(PlaylistPath))
                {
                    _dialogService.ShowMessageBox($"The selected playlist file does not exist on disk:\n{PlaylistPath}\nPlease select a valid playlist file.", "Playlist File Missing");
                    return;
                }
            }
        }

        // Apply back to _originalJob
        _originalJob.Name = Name.Trim();
        _originalJob.Description = Description?.Trim();
        _originalJob.IsEnabled = IsEnabled;

        // Trigger
        _originalJob.Trigger.TriggerType = SelectedTriggerType;
        _originalJob.Trigger.TimeOfDay = new TimeSpan(Math.Clamp(TimeHours, 0, 23), Math.Clamp(TimeMinutes, 0, 59), 0);
        _originalJob.Trigger.IntervalMinutes = Math.Max(1, IntervalMinutes);
        _originalJob.Trigger.ServerResetOffsetMinutes = ServerResetOffsetMinutes;
        
        if (SelectedTriggerType == ScheduleTriggerType.Once && SpecificDate.HasValue)
        {
            DateTime date = SpecificDate.Value.Date;
            DateTime localDateTime = new DateTime(date.Year, date.Month, date.Day, Math.Clamp(TimeHours, 0, 23), Math.Clamp(TimeMinutes, 0, 59), 0);
            _originalJob.Trigger.SpecificDateTime = new DateTimeOffset(localDateTime);
        }
        else
        {
            _originalJob.Trigger.SpecificDateTime = SpecificDate;
        }
        _originalJob.Trigger.DaysOfWeek = DaysOfWeekOptions
            .Where(d => d.IsSelected)
            .Select(d => d.Day)
            .ToList();

        if (_originalJob.Trigger.DaysOfWeek.Count == 0)
        {
            _originalJob.Trigger.DaysOfWeek = Enum.GetValues<DayOfWeek>().ToList();
        }

        // Target
        _originalJob.Target.TargetType = SelectedTargetType;
        if (SelectedTargetType == ScheduleTargetType.MultipleAccounts)
        {
            var selected = SelectableAccounts.Where(a => a.IsSelected).Select(a => a.Username).ToList();
            _originalJob.Target.TargetAccounts = selected;
            _originalJob.Target.TargetName = $"{selected.Count} Accounts";
        }
        else
        {
            _originalJob.Target.TargetAccounts = new List<string>();
            _originalJob.Target.TargetName = SelectedTargetType switch
            {
                ScheduleTargetType.SingleAccount => SelectedAccount,
                ScheduleTargetType.AccountGroup => SelectedGroup,
                ScheduleTargetType.ArmyQueuePreset => SelectedArmyPreset,
                _ => null
            };
        }
        _originalJob.Target.Server = !string.IsNullOrWhiteSpace(Server) ? Server : "Twilly";

        // Scripts
        _originalJob.Pipeline.PipelineMode = SelectedPipelineMode;
        _originalJob.Pipeline.PlaylistPath = PlaylistPath;
        _originalJob.Pipeline.RestartClientBetweenScripts = RestartClientBetweenScripts;
        _originalJob.Pipeline.Steps = Steps.Select(s => new ScheduleScriptStep
        {
            Id = s.Id,
            ScriptPath = s.ScriptPath,
            Name = s.Name,
            Storage = s.Storage
        }).ToList();

        // Policy & Post-Run
        _originalJob.ConflictPolicy = SelectedConflictPolicy;
        _originalJob.PostRun.ExitClientOnFinish = ExitClientOnFinish;
        _originalJob.PostRun.NotifyOnStart = NotifyOnStart;
        _originalJob.PostRun.NotifyOnFinish = NotifyOnFinish;
        _originalJob.PostRun.NotifyOnError = NotifyOnError;
        _originalJob.PostRun.PowerAction = SelectedPowerAction;

        Cleanup();
        DialogResult = true;
        CloseAction?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Cleanup();
        DialogResult = false;
        CloseAction?.Invoke();
    }

    #endregion
}
