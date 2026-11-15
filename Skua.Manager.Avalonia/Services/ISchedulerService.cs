using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Skua.Core.Models;
using Skua.Manager.Avalonia.ViewModels;

namespace Skua.Manager.Avalonia.Services;

public interface ISchedulerService
{
    bool IsRunning { get; }
    ObservableCollection<ScheduledJobViewModel> Jobs { get; }

    void Start();
    void Pause();
    void Resume();

    Task RunJobNowAsync(ScheduledJobViewModel job);
    void AbortJob(ScheduledJobViewModel job);

    void AddJob(ScheduledJob job);
    void RemoveJob(ScheduledJobViewModel jobVm);
    ScheduledJob DuplicateJob(ScheduledJob job);

    void SaveJobs();
    void LoadJobs();

    DateTime CalculateNextRun(ScheduleTriggerConfig trigger, DateTime fromLocal);
}
