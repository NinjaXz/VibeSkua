using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Manager.Avalonia.ViewModels;
using Skua.Shared.Avalonia.ViewModels;
using Timer = System.Timers.Timer;

namespace Skua.Manager.Avalonia.Services;

public sealed class SchedulerService : ISchedulerService, IDisposable
{
    private readonly AccountManagerViewModel _accountManager;
    private readonly LauncherViewModel _launcherViewModel;
    private readonly ArmyQueueViewModel _armyQueueViewModel;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly Timer _tickTimer;
    private readonly HttpClient _httpClient = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _runningJobTokens = new();
    private readonly object _lock = new();
    private bool _isEvaluating;

    public bool IsRunning { get; private set; } = true;
    public ObservableCollection<ScheduledJobViewModel> Jobs { get; } = new();

    public SchedulerService(
        AccountManagerViewModel accountManager,
        LauncherViewModel launcherViewModel,
        ArmyQueueViewModel armyQueueViewModel,
        ISettingsService settingsService,
        IDialogService dialogService)
    {
        _accountManager = accountManager;
        _launcherViewModel = launcherViewModel;
        _armyQueueViewModel = armyQueueViewModel;
        _settingsService = settingsService;
        _dialogService = dialogService;

        LoadJobs();

        _tickTimer = new Timer
        {
            Interval = 1000,
            AutoReset = true
        };
        _tickTimer.Elapsed += OnTimerTick!;
        _tickTimer.Start();
    }

    public void Start()
    {
        IsRunning = true;
        _tickTimer.Start();
    }

    public void Pause()
    {
        IsRunning = false;
    }

    public void Resume()
    {
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;
        _tickTimer.Stop();
        lock (_lock)
        {
            foreach (var cts in _runningJobTokens.Values)
            {
                try { cts.Cancel(); } catch { }
            }
            _runningJobTokens.Clear();
        }
    }

    public void AddJob(ScheduledJob job)
    {
        job.NextRunTime = CalculateNextRun(job.Trigger, DateTime.Now);
        var vm = new ScheduledJobViewModel(job, this, _settingsService);
        Dispatcher.UIThread.Post(() =>
        {
            Jobs.Add(vm);
            SaveJobs();
        });
    }

    public void RemoveJob(ScheduledJobViewModel jobVm)
    {
        AbortJob(jobVm);
        Dispatcher.UIThread.Post(() =>
        {
            Jobs.Remove(jobVm);
            SaveJobs();
        });
    }

    public ScheduledJob DuplicateJob(ScheduledJob job)
    {
        string json = JsonSerializer.Serialize(job);
        var copy = JsonSerializer.Deserialize<ScheduledJob>(json) ?? new();
        copy.Id = Guid.NewGuid();
        copy.Name = $"{job.Name} (Copy)";
        copy.NextRunTime = CalculateNextRun(copy.Trigger, DateTime.Now);
        copy.History = new();
        copy.LastRunTime = null;
        copy.LastDuration = null;
        copy.LastResult = null;
        return copy;
    }

    private void OnTimerTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!IsRunning || _isEvaluating)
            return;

        _isEvaluating = true;
        try
        {
            DateTime now = DateTime.Now;

            foreach (var jobVm in Jobs.ToList())
            {
                // Update countdown display every tick
                Dispatcher.UIThread.Post(jobVm.RefreshDisplayStrings);

                if (!jobVm.IsEnabled || jobVm.IsRunning)
                    continue;

                if (!jobVm.Job.NextRunTime.HasValue)
                {
                    jobVm.Job.NextRunTime = CalculateNextRun(jobVm.Job.Trigger, now);
                    continue;
                }

                if (now >= jobVm.Job.NextRunTime.Value)
                {
                    // Trigger due job
                    _ = Task.Run(() => ExecuteJobAsync(jobVm, false));
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[SchedulerService] Tick error: {ex.Message}");
        }
        finally
        {
            _isEvaluating = false;
        }
    }

    public async Task RunJobNowAsync(ScheduledJobViewModel jobVm)
    {
        if (jobVm.IsRunning)
            return;

        await Task.Run(() => ExecuteJobAsync(jobVm, true));
    }

    public void AbortJob(ScheduledJobViewModel jobVm)
    {
        lock (_lock)
        {
            if (_runningJobTokens.TryGetValue(jobVm.Job.Id, out var cts))
            {
                try { cts.Cancel(); } catch { }
                _runningJobTokens.Remove(jobVm.Job.Id);
            }
        }

        jobVm.SetStatus("Stopped", "#F44336");
        jobVm.AddLog("Job manually stopped by user.");
    }

    private async Task ExecuteJobAsync(ScheduledJobViewModel jobVm, bool isManualTrigger)
    {
        Guid jobId = jobVm.Job.Id;
        CancellationTokenSource cts = new();
        lock (_lock)
        {
            if (_runningJobTokens.ContainsKey(jobId))
                return;
            _runningJobTokens[jobId] = cts;
        }

        CancellationToken token = cts.Token;
        DateTime startTime = DateTime.Now;
        string finalStatus = "Completed";
        string? finalMessage = null;

        try
        {
            jobVm.SetStatus("Running", "#4CAF50");
            jobVm.AddLog($"--- Job '{jobVm.Name}' Started {(isManualTrigger ? "(Manual Run)" : "(Scheduled)")} ---");

            if (jobVm.Job.PostRun.NotifyOnStart)
            {
                _ = SendDiscordEmbedAsync(jobVm, "Task Started", $"Job **{jobVm.Name}** has commenced execution.", 0x2196F3);
            }

            // 1. Resolve Target
            var target = jobVm.Job.Target;
            if (target.TargetType == ScheduleTargetType.ArmyQueuePreset)
            {
                await ExecuteArmyPresetAsync(jobVm, target.TargetName, token);
            }
            else if (target.TargetType == ScheduleTargetType.SingleAccount)
            {
                await ExecuteAccountPipelineAsync(jobVm, target.TargetName, target.Server, token);
            }
            else if (target.TargetType == ScheduleTargetType.AccountGroup)
            {
                await ExecuteGroupPipelineAsync(jobVm, target.TargetName, target.Server, token);
            }
            else if (target.TargetType == ScheduleTargetType.MultipleAccounts)
            {
                await ExecuteMultipleAccountsPipelineAsync(jobVm, target.TargetAccounts, target.Server, token);
            }

            finalStatus = "Completed";
            finalMessage = "All scripts finished successfully.";
            jobVm.AddLog($"Job '{jobVm.Name}' finished successfully.");

            if (jobVm.Job.PostRun.NotifyOnFinish)
            {
                TimeSpan dur = DateTime.Now - startTime;
                _ = SendDiscordEmbedAsync(jobVm, "Task Completed", $"Job **{jobVm.Name}** completed in **{(int)dur.TotalMinutes}m {dur.Seconds}s**.", 0x00FF00);
            }

            // Post-run system action
            HandleSystemPowerAction(jobVm.Job.PostRun.PowerAction, jobVm);
        }
        catch (OperationCanceledException)
        {
            finalStatus = "Aborted";
            finalMessage = "Execution cancelled.";
            jobVm.AddLog("Execution cancelled.");
        }
        catch (Exception ex)
        {
            finalStatus = "Failed";
            finalMessage = ex.Message;
            jobVm.AddLog($"ERROR: {ex.Message}");

            if (jobVm.Job.PostRun.NotifyOnError)
            {
                _ = SendDiscordEmbedAsync(jobVm, "Task Failed", $"Job **{jobVm.Name}** failed with error:\n`{ex.Message}`", 0xFF0000);
            }
        }
        finally
        {
            lock (_lock)
            {
                _runningJobTokens.Remove(jobId);
                cts.Dispose();
            }

            DateTime endTime = DateTime.Now;
            jobVm.RecordExecution(startTime, endTime, finalStatus, finalMessage);

            // Reschedule next run
            if (jobVm.Job.Trigger.TriggerType == ScheduleTriggerType.Once)
            {
                jobVm.IsEnabled = false; // Disable one-shot once completed
                jobVm.Job.NextRunTime = null;
            }
            else
            {
                jobVm.Job.NextRunTime = CalculateNextRun(jobVm.Job.Trigger, DateTime.Now);
            }

            Dispatcher.UIThread.Post(() =>
            {
                jobVm.RefreshDisplayStrings();
                SaveJobs();
            });
        }
    }

    private async Task ExecuteArmyPresetAsync(ScheduledJobViewModel jobVm, string? presetName, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            throw new InvalidOperationException("No Army Queue preset specified.");

        jobVm.AddLog($"Loading Army Queue preset: '{presetName}'...");

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _armyQueueViewModel.SelectedPreset = presetName;
            _armyQueueViewModel.LoadSelectedPreset();
        });

        jobVm.AddLog($"Starting Army Queue...");
        using var reg = token.Register(() => _armyQueueViewModel.StopQueueDirect());
        await _armyQueueViewModel.StartQueueAsync(isAutomated: true);
    }

    private async Task ExecuteAccountPipelineAsync(ScheduledJobViewModel jobVm, string? username, string? server, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("No account username specified.");

        IAccountItemViewModel? acc = _accountManager.Accounts.FirstOrDefault(
            a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (acc == null)
            throw new InvalidOperationException($"Account '{username}' not found in Account Manager.");

        // Check Concurrency / Conflict Policy
        await HandleConcurrencyPolicyAsync(jobVm, acc.Username, token);

        var pipeline = jobVm.Job.Pipeline;
        bool restartBetween = pipeline.RestartClientBetweenScripts;

        var steps = await ResolvePipelineStepsAsync(pipeline, token);
        if (steps.Count == 0)
            throw new InvalidOperationException("No scripts configured in pipeline.");

        if (restartBetween && steps.Count > 1)
        {
            jobVm.AddLog($"Pipeline configured with 'Restart client between each script' ({steps.Count} scripts).");

            for (int i = 0; i < steps.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var step = steps[i];
                bool isLast = (i == steps.Count - 1);
                bool exitOnStop = !isLast || jobVm.Job.PostRun.ExitClientOnFinish;

                jobVm.AddLog($"[Script {i + 1}/{steps.Count}] Starting '{step.Name}'...");
                await ExecuteSingleScriptWithRetryAsync(jobVm, acc, step, server, exitOnStop, token);

                if (!isLast)
                {
                    jobVm.AddLog($"[Script {i + 1}/{steps.Count}] '{step.Name}' completed. Cooldown before restarting client for next script...");
                    await Task.Delay(4000, token);
                }
            }

            jobVm.AddLog("All sequential script steps completed.");
        }
        else
        {
            (string playlistFile, bool isTemp) = await PreparePlaylistFileAsync(jobVm, token);
            try
            {
                await ExecutePlaylistWithRetryAsync(jobVm, acc, playlistFile, server, jobVm.Job.PostRun.ExitClientOnFinish, token);
            }
            finally
            {
                if (isTemp && File.Exists(playlistFile))
                {
                    try { File.Delete(playlistFile); } catch { }
                }
            }
        }
    }

    private async Task ExecuteSingleScriptWithRetryAsync(
        ScheduledJobViewModel jobVm,
        IAccountItemViewModel acc,
        ScheduleScriptStep step,
        string? server,
        bool exitOnFinish,
        CancellationToken token)
    {
        int maxRetries = 2;
        int attempt = 0;

        while (true)
        {
            attempt++;
            token.ThrowIfCancellationRequested();

            string attemptStr = attempt > 1 ? $" (Retry {attempt - 1}/{maxRetries})" : string.Empty;
            jobVm.AddLog($"Launching client for '{acc.DisplayOrUsername}' with script '{step.Name}'{attemptStr}...");

            Process? proc = LaunchClientProcess(
                acc,
                playlistPath: null,
                scriptPath: step.ScriptPath,
                optionsStorage: step.Storage,
                server: server,
                exitOnFinish: exitOnFinish);

            if (proc == null)
                throw new InvalidOperationException($"Failed to launch client process for account '{acc.Username}'.");

            jobVm.AddLog($"Client process started (PID: {proc.Id}). Waiting for script '{step.Name}' to finish...");

            while (!proc.HasExited)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(1000, token);
            }

            jobVm.AddLog($"Client process (PID: {proc.Id}) exited with code {proc.ExitCode}.");

            if (proc.ExitCode == 101)
            {
                if (attempt <= maxRetries)
                {
                    jobVm.AddLog($"Login timed out (Exit code 101). Server may be full or restarting. Retrying in 120s (Attempt {attempt}/{maxRetries})...");
                    await Task.Delay(120_000, token);
                    continue;
                }

                throw new InvalidOperationException($"Login timed out after {maxRetries + 1} attempts for script '{step.Name}'. Server was unreachable or full.");
            }

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException($"Client process for script '{step.Name}' exited abnormally with code {proc.ExitCode}.");
            }

            break;
        }
    }

    private async Task ExecutePlaylistWithRetryAsync(
        ScheduledJobViewModel jobVm,
        IAccountItemViewModel acc,
        string playlistFile,
        string? server,
        bool exitOnFinish,
        CancellationToken token)
    {
        int maxRetries = 2;
        int attempt = 0;

        while (true)
        {
            attempt++;
            token.ThrowIfCancellationRequested();

            string attemptStr = attempt > 1 ? $" (Retry {attempt - 1}/{maxRetries})" : string.Empty;
            jobVm.AddLog($"Launching client for '{acc.DisplayOrUsername}' on server '{server ?? "Twilly"}'{attemptStr}...");
            Process? proc = LaunchClientProcess(
                acc,
                playlistPath: playlistFile,
                scriptPath: null,
                optionsStorage: null,
                server: server,
                exitOnFinish: exitOnFinish);

            if (proc == null)
                throw new InvalidOperationException($"Failed to launch client process for account '{acc.Username}'.");

            jobVm.AddLog($"Client process started (PID: {proc.Id}). Waiting for completion...");

            while (!proc.HasExited)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(1000, token);
            }

            jobVm.AddLog($"Client process (PID: {proc.Id}) exited with code {proc.ExitCode}.");

            if (proc.ExitCode == 101)
            {
                if (attempt <= maxRetries)
                {
                    jobVm.AddLog($"Login timed out (Exit code 101). Server may be full or restarting. Retrying in 120s (Attempt {attempt}/{maxRetries})...");
                    await Task.Delay(120_000, token);
                    continue;
                }

                throw new InvalidOperationException($"Login timed out after {maxRetries + 1} attempts. Server was unreachable or full.");
            }

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException($"Client process exited abnormally with code {proc.ExitCode}.");
            }

            break;
        }
    }

    private async Task ExecuteMultipleAccountsPipelineAsync(ScheduledJobViewModel jobVm, List<string> accountUsernames, string? server, CancellationToken token)
    {
        if (accountUsernames == null || accountUsernames.Count == 0)
            throw new InvalidOperationException("No accounts selected for execution.");

        List<IAccountItemViewModel> accounts = new();
        foreach (string uname in accountUsernames)
        {
            var acc = _accountManager.Accounts.FirstOrDefault(a => a.Username.Equals(uname, StringComparison.OrdinalIgnoreCase));
            if (acc != null)
                accounts.Add(acc);
        }

        if (accounts.Count == 0)
            throw new InvalidOperationException("None of the selected accounts could be found in Account Manager.");

        var pipeline = jobVm.Job.Pipeline;
        bool restartBetween = pipeline.RestartClientBetweenScripts;

        var steps = await ResolvePipelineStepsAsync(pipeline, token);
        if (steps.Count == 0)
            throw new InvalidOperationException("No scripts configured in pipeline.");

        if (restartBetween && steps.Count > 1)
        {
            jobVm.AddLog($"Pipeline configured with 'Restart client between each script' ({steps.Count} scripts, {accounts.Count} accounts).");

            for (int i = 0; i < steps.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var step = steps[i];
                bool isLast = (i == steps.Count - 1);
                bool exitOnStop = !isLast || jobVm.Job.PostRun.ExitClientOnFinish;

                jobVm.AddLog($"[Script {i + 1}/{steps.Count}] Starting '{step.Name}' across {accounts.Count} accounts...");
                List<Process> stepProcs = new();

                foreach (var acc in accounts)
                {
                    token.ThrowIfCancellationRequested();
                    await HandleConcurrencyPolicyAsync(jobVm, acc.Username, token);

                    Process? p = LaunchClientProcess(acc, null, step.ScriptPath, step.Storage, server, exitOnStop);
                    if (p != null)
                        stepProcs.Add(p);

                    await Task.Delay(3000, token);
                }

                while (stepProcs.Any(p => !p.HasExited))
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(1500, token);
                }

                if (!isLast)
                {
                    jobVm.AddLog($"[Script {i + 1}/{steps.Count}] Finished for all accounts. Cooldown before restarting clients for next script...");
                    await Task.Delay(4000, token);
                }
            }

            jobVm.AddLog("All sequential script steps completed for all accounts.");
        }
        else
        {
            (string playlistFile, bool isTemp) = await PreparePlaylistFileAsync(jobVm, token);
            try
            {
                List<Process> procs = new();
                foreach (var acc in accounts)
                {
                    token.ThrowIfCancellationRequested();
                    await HandleConcurrencyPolicyAsync(jobVm, acc.Username, token);

                    jobVm.AddLog($"Launching account '{acc.DisplayOrUsername}'...");
                    Process? p = LaunchClientProcess(acc, playlistFile, null, null, server, jobVm.Job.PostRun.ExitClientOnFinish);
                    if (p != null)
                        procs.Add(p);

                    await Task.Delay(3000, token);
                }

                jobVm.AddLog($"All {procs.Count} clients launched. Awaiting completion...");
                while (procs.Any(p => !p.HasExited))
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(1500, token);
                }
            }
            finally
            {
                if (isTemp && File.Exists(playlistFile))
                {
                    try { File.Delete(playlistFile); } catch { }
                }
            }
        }
    }

    private async Task ExecuteGroupPipelineAsync(ScheduledJobViewModel jobVm, string? groupName, string? server, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            throw new InvalidOperationException("No account group specified.");

        var group = _accountManager.Groups.FirstOrDefault(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
        if (group == null || group.Accounts.Count == 0)
            throw new InvalidOperationException($"Account group '{groupName}' not found or has no accounts.");

        var pipeline = jobVm.Job.Pipeline;
        bool restartBetween = pipeline.RestartClientBetweenScripts;

        var steps = await ResolvePipelineStepsAsync(pipeline, token);
        if (steps.Count == 0)
            throw new InvalidOperationException("No scripts configured in pipeline.");

        if (restartBetween && steps.Count > 1)
        {
            jobVm.AddLog($"Pipeline configured with 'Restart client between each script' ({steps.Count} scripts, Group '{groupName}').");

            for (int i = 0; i < steps.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var step = steps[i];
                bool isLast = (i == steps.Count - 1);
                bool exitOnStop = !isLast || jobVm.Job.PostRun.ExitClientOnFinish;

                jobVm.AddLog($"[Script {i + 1}/{steps.Count}] Starting '{step.Name}' for group '{groupName}'...");
                List<Process> stepProcs = new();

                foreach (var acc in group.Accounts)
                {
                    token.ThrowIfCancellationRequested();
                    await HandleConcurrencyPolicyAsync(jobVm, acc.Username, token);

                    Process? p = LaunchClientProcess(acc, null, step.ScriptPath, step.Storage, server, exitOnStop);
                    if (p != null)
                        stepProcs.Add(p);

                    await Task.Delay(3000, token);
                }

                while (stepProcs.Any(p => !p.HasExited))
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(1500, token);
                }

                if (!isLast)
                {
                    jobVm.AddLog($"[Script {i + 1}/{steps.Count}] Finished for group '{groupName}'. Cooldown before restarting clients for next script...");
                    await Task.Delay(4000, token);
                }
            }

            jobVm.AddLog($"All sequential script steps completed for group '{groupName}'.");
        }
        else
        {
            (string playlistFile, bool isTemp) = await PreparePlaylistFileAsync(jobVm, token);
            try
            {
                List<Process> procs = new();
                foreach (var acc in group.Accounts)
                {
                    token.ThrowIfCancellationRequested();
                    await HandleConcurrencyPolicyAsync(jobVm, acc.Username, token);

                    jobVm.AddLog($"Launching group account '{acc.DisplayOrUsername}'...");
                    Process? p = LaunchClientProcess(acc, playlistFile, null, null, server, jobVm.Job.PostRun.ExitClientOnFinish);
                    if (p != null)
                        procs.Add(p);

                    await Task.Delay(3000, token);
                }

                jobVm.AddLog($"All {procs.Count} group clients launched. Awaiting completion...");
                while (procs.Any(p => !p.HasExited))
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(1500, token);
                }
            }
            finally
            {
                if (isTemp && File.Exists(playlistFile))
                {
                    try { File.Delete(playlistFile); } catch { }
                }
            }
        }
    }

    private List<Process> FindProcessesForAccount(string username)
    {
        List<Process> result = new();

        // 1. Check Manager-tracked processes
        foreach (var pInfo in _launcherViewModel.SkuaProcesses.ToList())
        {
            if (pInfo.AccountName.Equals(username, StringComparison.OrdinalIgnoreCase) && !pInfo.Process.HasExited)
            {
                result.Add(pInfo.Process);
            }
        }

        // 2. Check external processes via %TEMP%\VibeSkuaTabs\{pid}.txt
        try
        {
            string tabsDir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
            if (Directory.Exists(tabsDir))
            {
                foreach (string file in Directory.GetFiles(tabsDir, "*.txt"))
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    if (int.TryParse(fileName, out int pid))
                    {
                        if (result.Any(p => p.Id == pid))
                            continue;

                        try
                        {
                            string user = File.ReadAllText(file).Trim();
                            if (user.Equals(username, StringComparison.OrdinalIgnoreCase))
                            {
                                Process proc = Process.GetProcessById(pid);
                                if (!proc.HasExited)
                                    result.Add(proc);
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        return result;
    }

    private async Task HandleConcurrencyPolicyAsync(ScheduledJobViewModel jobVm, string username, CancellationToken token)
    {
        bool isAccountBusy() => FindProcessesForAccount(username).Count > 0;

        if (!isAccountBusy())
            return;

        jobVm.AddLog($"Account '{username}' is currently busy in an active session.");

        switch (jobVm.Job.ConflictPolicy)
        {
            case ScheduleConflictPolicy.SkipTrigger:
                throw new OperationCanceledException($"Account '{username}' is busy; trigger skipped as per conflict policy.");

            case ScheduleConflictPolicy.ForceTerminateAndStart:
                jobVm.AddLog($"Force-terminating existing process(es) for '{username}'...");
                var busyProcs = FindProcessesForAccount(username);
                foreach (var p in busyProcs)
                {
                    try
                    {
                        if (!ProcessCloser.RequestClose(p))
                            p.Kill(true);
                    }
                    catch { }
                }
                await Task.Delay(2500, token);
                break;

            case ScheduleConflictPolicy.WaitUntilFree:
            default:
                jobVm.SetStatus("Waiting for Account", "#FF9800");
                jobVm.AddLog($"Waiting for account '{username}' to exit...");
                while (isAccountBusy())
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(3000, token);
                }
                jobVm.SetStatus("Running", "#4CAF50");
                jobVm.AddLog($"Account '{username}' is now free. Proceeding with launch...");
                break;
        }
    }

    private async Task<(string Path, bool IsTemp)> PreparePlaylistFileAsync(ScheduledJobViewModel jobVm, CancellationToken token)
    {
        var pipeline = jobVm.Job.Pipeline;
        if (pipeline.PipelineMode == SchedulePipelineMode.PlaylistFile && !string.IsNullOrWhiteSpace(pipeline.PlaylistPath))
        {
            if (!File.Exists(pipeline.PlaylistPath))
                throw new FileNotFoundException($"Playlist file not found: {pipeline.PlaylistPath}");
            return (pipeline.PlaylistPath, false);
        }

        // Custom steps -> generate temporary playlist JSON
        if (pipeline.Steps.Count == 0)
            throw new InvalidOperationException("No scripts configured in pipeline.");

        string tempDir = Path.Combine(Path.GetTempPath(), "VibeSkua_Playlists");
        Directory.CreateDirectory(tempDir);
        string tempFile = Path.Combine(tempDir, $"playlist_{jobVm.Job.Id:N}_{DateTime.Now.Ticks}.json");

        var playlistItems = pipeline.Steps.Select(s => new ScriptSchedulerViewModel.SavedScriptItem
        {
            Id = s.Id,
            Name = !string.IsNullOrWhiteSpace(s.Name) ? s.Name : Path.GetFileNameWithoutExtension(s.ScriptPath),
            Path = s.ScriptPath
        }).ToList();

        string json = JsonSerializer.Serialize(playlistItems, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(tempFile, json, token);
        return (tempFile, true);
    }

    private async Task<List<ScheduleScriptStep>> ResolvePipelineStepsAsync(SchedulePipelineConfig pipeline, CancellationToken token)
    {
        if (pipeline.PipelineMode == SchedulePipelineMode.CustomSteps)
        {
            return pipeline.Steps.ToList();
        }
        else if (pipeline.PipelineMode == SchedulePipelineMode.PlaylistFile && !string.IsNullOrWhiteSpace(pipeline.PlaylistPath))
        {
            if (!File.Exists(pipeline.PlaylistPath))
                throw new FileNotFoundException($"Playlist file not found: {pipeline.PlaylistPath}");

            string json = await File.ReadAllTextAsync(pipeline.PlaylistPath, token);
            var items = JsonSerializer.Deserialize<List<ScriptSchedulerViewModel.SavedScriptItem>>(json);
            if (items == null || items.Count == 0)
                return new List<ScheduleScriptStep>();

            return items.Select(item => new ScheduleScriptStep
            {
                Id = item.Id,
                Name = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : Path.GetFileNameWithoutExtension(item.Path),
                ScriptPath = item.Path,
                Storage = $"Queue_{Path.GetFileNameWithoutExtension(item.Path)}_{item.Id}"
            }).ToList();
        }

        return new List<ScheduleScriptStep>();
    }

    private Process? LaunchClientProcess(
        IAccountItemViewModel acc,
        string? playlistPath,
        string? scriptPath,
        string? optionsStorage,
        string? server,
        bool exitOnFinish)
    {
        try
        {
            string exePath = SkuaExecutableLocator.ExecutablePath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                Trace.WriteLine("Executable not found.");
                return null;
            }

            string targetServer = !string.IsNullOrWhiteSpace(server)
                ? server
                : _settingsService.Get("LastServer", "Twilly");

            ProcessStartInfo psi = new(exePath)
            {
                WorkingDirectory = SkuaExecutableLocator.WorkingDirectory,
                ArgumentList =
                {
                    "-u",
                    acc.Username,
                    "-p",
                    acc.Password,
                    "-s",
                    targetServer
                }
            };

            if (!string.IsNullOrWhiteSpace(scriptPath))
            {
                psi.ArgumentList.Add("--run-script");
                psi.ArgumentList.Add(scriptPath);

                if (!string.IsNullOrWhiteSpace(optionsStorage))
                {
                    psi.ArgumentList.Add("--script-options-storage");
                    psi.ArgumentList.Add(optionsStorage);
                }
            }
            else if (!string.IsNullOrWhiteSpace(playlistPath))
            {
                psi.ArgumentList.Add("--run-playlist");
                psi.ArgumentList.Add(playlistPath);
            }

            if (exitOnFinish)
            {
                psi.ArgumentList.Add("--exit-on-script-stop");
            }

            if (_settingsService.Get("syncTheme", false))
            {
                psi.ArgumentList.Add("--use-theme");
                psi.ArgumentList.Add(_settingsService.Get("CurrentTheme", "no-theme"));
            }

            Process? process = Process.Start(psi);
            if (process != null)
            {
                StrongReferenceMessenger.Default.Send(new AddProcessMessage(process, acc.DisplayOrUsername));
            }

            return process;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[SchedulerService] Error launching client: {ex.Message}");
            return null;
        }
    }

    public DateTime CalculateNextRun(ScheduleTriggerConfig trigger, DateTime fromLocal)
    {
        switch (trigger.TriggerType)
        {
            case ScheduleTriggerType.Once:
                return trigger.SpecificDateTime?.LocalDateTime ?? fromLocal;

            case ScheduleTriggerType.Interval:
                int interval = Math.Max(1, trigger.IntervalMinutes);
                return fromLocal.AddMinutes(interval);

            case ScheduleTriggerType.Daily:
                DateTime todayRun = fromLocal.Date.Add(trigger.TimeOfDay);
                return todayRun > fromLocal ? todayRun : todayRun.AddDays(1);

            case ScheduleTriggerType.Weekly:
                var days = trigger.DaysOfWeek.Count > 0 ? trigger.DaysOfWeek : new List<DayOfWeek> { fromLocal.DayOfWeek };
                for (int i = 0; i <= 7; i++)
                {
                    DateTime cand = fromLocal.Date.AddDays(i).Add(trigger.TimeOfDay);
                    if (cand > fromLocal && days.Contains(cand.DayOfWeek))
                        return cand;
                }
                return fromLocal.Date.AddDays(1).Add(trigger.TimeOfDay);

            case ScheduleTriggerType.DailyResetSync:
                TimeZoneInfo estZone = GetAqwTimeZone();
                DateTime nowEst = TimeZoneInfo.ConvertTime(fromLocal, estZone);
                DateTime resetEstToday = nowEst.Date.AddMinutes(trigger.ServerResetOffsetMinutes);

                DateTime nextResetEst = resetEstToday > nowEst
                    ? resetEstToday
                    : resetEstToday.AddDays(1);

                return TimeZoneInfo.ConvertTime(nextResetEst, estZone, TimeZoneInfo.Local);

            case ScheduleTriggerType.WeeklyResetSync:
                TimeZoneInfo estZoneW = GetAqwTimeZone();
                DateTime nowEstW = TimeZoneInfo.ConvertTime(fromLocal, estZoneW);
                int daysUntilFriday = ((int)DayOfWeek.Friday - (int)nowEstW.DayOfWeek + 7) % 7;
                DateTime resetEstFriday = nowEstW.Date.AddDays(daysUntilFriday).AddMinutes(trigger.ServerResetOffsetMinutes);

                if (resetEstFriday <= nowEstW)
                    resetEstFriday = resetEstFriday.AddDays(7);

                return TimeZoneInfo.ConvertTime(resetEstFriday, estZoneW, TimeZoneInfo.Local);

            default:
                return fromLocal.AddHours(1);
        }
    }

    private static TimeZoneInfo GetAqwTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
        catch
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("EST", TimeSpan.FromHours(-5), "EST", "EST");
            }
        }
    }

    private void HandleSystemPowerAction(SystemPowerAction action, ScheduledJobViewModel jobVm)
    {
        if (action == SystemPowerAction.None)
            return;

        try
        {
            if (action == SystemPowerAction.SleepPC)
            {
                jobVm.AddLog("Post-run action: Putting PC to sleep...");
                if (OperatingSystem.IsWindows())
                {
                    Process.Start("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
                }
            }
            else if (action == SystemPowerAction.ShutdownPC)
            {
                jobVm.AddLog("Post-run action: Initiating PC shutdown (30s abort window)...");
                if (OperatingSystem.IsWindows())
                {
                    Process.Start("shutdown.exe", "/s /t 30 /c \"VibeSkua: Scheduled task finished. Shutting down in 30 seconds.\"");
                }
            }
        }
        catch (Exception ex)
        {
            jobVm.AddLog($"Failed to execute system power action: {ex.Message}");
        }
    }

    private async Task SendDiscordEmbedAsync(ScheduledJobViewModel jobVm, string title, string description, int colorHex)
    {
        string? webhookUrl = _settingsService.GetClient()?.DiscordWebhookUrl;
        if (string.IsNullOrWhiteSpace(webhookUrl))
            webhookUrl = _settingsService.Get<string>("DiscordWebhookUrl");
        if (string.IsNullOrWhiteSpace(webhookUrl))
            webhookUrl = _settingsService.Get<string>("DiscordWebhook");
        if (string.IsNullOrWhiteSpace(webhookUrl))
            webhookUrl = jobVm.Job.PostRun.DiscordWebhookUrl;

        if (string.IsNullOrWhiteSpace(webhookUrl))
            return;

        try
        {
            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = $"📅 Task Scheduler · {title}",
                        description,
                        color = colorHex,
                        timestamp = DateTime.UtcNow.ToString("o"),
                        footer = new
                        {
                            text = "VibeSkua Manager"
                        }
                    }
                }
            };

            string json = JsonSerializer.Serialize(payload);
            using StringContent content = new(json, Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(webhookUrl, content);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[SchedulerService] Discord webhook error: {ex.Message}");
        }
    }

    public void SaveJobs()
    {
        try
        {
            string path = Path.Combine(ClientFileSources.SkuaDIR, "scheduler_tasks.json");
            List<ScheduledJob> list = Jobs.Select(j => j.Job).ToList();
            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[SchedulerService] Failed to save jobs: {ex.Message}");
        }
    }

    public void LoadJobs()
    {
        try
        {
            string path = Path.Combine(ClientFileSources.SkuaDIR, "scheduler_tasks.json");
            if (!File.Exists(path))
                return;

            string json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<ScheduledJob>>(json);
            if (list == null) return;

            Jobs.Clear();
            foreach (var job in list)
            {
                if (job.IsEnabled && (!job.NextRunTime.HasValue || job.NextRunTime.Value < DateTime.Now))
                {
                    job.NextRunTime = CalculateNextRun(job.Trigger, DateTime.Now);
                }
                Jobs.Add(new ScheduledJobViewModel(job, this, _settingsService));
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[SchedulerService] Failed to load jobs: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _tickTimer.Dispose();
        _httpClient.Dispose();
    }
}
