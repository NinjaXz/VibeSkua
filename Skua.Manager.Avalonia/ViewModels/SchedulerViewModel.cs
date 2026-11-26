using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Manager.Avalonia.Services;
using Skua.Shared.Avalonia.ViewModels;

namespace Skua.Manager.Avalonia.ViewModels;

public partial class SchedulerViewModel : BotControlViewModelBase
{
    private readonly ISchedulerService _schedulerService;
    private readonly AccountManagerViewModel _accountManager;
    private readonly ArmyQueueViewModel _armyQueue;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IScriptManager _scriptManager;

    public SchedulerViewModel(
        ISchedulerService schedulerService,
        AccountManagerViewModel accountManager,
        ArmyQueueViewModel armyQueue,
        ISettingsService settingsService,
        IDialogService dialogService,
        IFileDialogService fileDialogService,
        IScriptManager scriptManager)
        : base("Task Scheduler")
    {
        _schedulerService = schedulerService;
        _accountManager = accountManager;
        _armyQueue = armyQueue;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _fileDialogService = fileDialogService;
        _scriptManager = scriptManager;

        _isSchedulerActive = _schedulerService.IsRunning;
        _autoStartWithManager = _settingsService.Get("SchedulerAutoStart", true);
        _runOnWindowsStartup = WindowsStartupHelper.IsStartupEnabled();
        _clockFormatIndex = _settingsService.Get("SchedulerUse24HourTime", false) ? 1 : 0;
    }

    public ObservableCollection<ScheduledJobViewModel> Jobs => _schedulerService.Jobs;

    [ObservableProperty]
    private ScheduledJobViewModel? _selectedJob;

    [ObservableProperty]
    private bool _isSchedulerActive;

    partial void OnIsSchedulerActiveChanged(bool value)
    {
        if (value)
            _schedulerService.Resume();
        else
            _schedulerService.Pause();

        OnPropertyChanged(nameof(EngineStatusText));
        OnPropertyChanged(nameof(EngineStatusBrush));
    }

    public string EngineStatusText => IsSchedulerActive ? "Engine Active" : "Engine Paused";
    public string EngineStatusBrush => IsSchedulerActive ? "#4CAF50" : "#9E9E9E";

    [ObservableProperty]
    private bool _autoStartWithManager;

    partial void OnAutoStartWithManagerChanged(bool value)
    {
        _settingsService.Set("SchedulerAutoStart", value);
    }

    [ObservableProperty]
    private bool _runOnWindowsStartup;

    partial void OnRunOnWindowsStartupChanged(bool value)
    {
        WindowsStartupHelper.SetStartupEnabled(value);
    }

    [ObservableProperty]
    private int _clockFormatIndex;

    partial void OnClockFormatIndexChanged(int value)
    {
        bool use24H = value == 1;
        _settingsService.Set("SchedulerUse24HourTime", use24H);
        foreach (var job in Jobs)
        {
            job.RefreshDisplayStrings(true, use24H);
        }
    }

    [RelayCommand]
    private void AddJob()
    {
        ScheduledJob newJob = new()
        {
            Name = $"Task #{Jobs.Count + 1}",
            IsEnabled = true
        };

        JobEditorDialogViewModel dialogVm = new(
            newJob,
            _accountManager,
            _armyQueue,
            _fileDialogService,
            _dialogService,
            _scriptManager,
            _settingsService);

        if (_dialogService.ShowDialog(dialogVm, "Add Scheduled Task") == true)
        {
            _schedulerService.AddJob(newJob);
        }
        SyncClockFormatFromSettings();
    }

    [RelayCommand]
    private void EditJob(ScheduledJobViewModel? jobVm)
    {
        jobVm ??= SelectedJob;
        if (jobVm == null) return;

        JobEditorDialogViewModel dialogVm = new(
            jobVm.Job,
            _accountManager,
            _armyQueue,
            _fileDialogService,
            _dialogService,
            _scriptManager,
            _settingsService);

        if (_dialogService.ShowDialog(dialogVm, $"Edit Task: {jobVm.Name}") == true)
        {
            if (jobVm.Job.IsEnabled)
            {
                jobVm.Job.NextRunTime = _schedulerService.CalculateNextRun(jobVm.Job.Trigger, DateTime.Now);
            }
            jobVm.RefreshFromJob();
            _schedulerService.SaveJobs();
        }
        SyncClockFormatFromSettings();
    }

    private void SyncClockFormatFromSettings()
    {
        int settingIndex = _settingsService.Get("SchedulerUse24HourTime", false) ? 1 : 0;
        if (ClockFormatIndex != settingIndex)
        {
            ClockFormatIndex = settingIndex;
        }
    }

    [RelayCommand]
    private void DuplicateJob(ScheduledJobViewModel? jobVm)
    {
        jobVm ??= SelectedJob;
        if (jobVm == null) return;

        ScheduledJob copy = _schedulerService.DuplicateJob(jobVm.Job);
        _schedulerService.AddJob(copy);
    }

    [RelayCommand]
    private void DeleteJob(ScheduledJobViewModel? jobVm)
    {
        jobVm ??= SelectedJob;
        if (jobVm == null) return;

        if (_dialogService.ShowMessageBox($"Are you sure you want to delete scheduled task '{jobVm.Name}'?", "Delete Task", true) == true)
        {
            _schedulerService.RemoveJob(jobVm);
        }
    }
}
