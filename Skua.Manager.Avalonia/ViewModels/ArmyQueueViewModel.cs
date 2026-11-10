using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Manager.Avalonia.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Shared.Avalonia.ViewModels.ScriptRepo;

namespace Skua.Manager.Avalonia.ViewModels;

public partial class AccountPickerItemViewModel : ObservableObject
{
    public AccountPickerItemViewModel(IAccountItemViewModel account)
    {
        Account = account;
        Username = account.Username;
        DisplayName = account.DisplayOrUsername;
    }

    public IAccountItemViewModel Account { get; }
    public string Username { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isSelected;
}

public partial class ArmyQueueMemberViewModel : ObservableObject
{
    public ArmyQueueMemberViewModel(string username, string displayName, string? scriptPath = null)
    {
        _username = username;
        _displayName = displayName;
        _scriptPath = scriptPath;
    }

    [ObservableProperty]
    private string _username;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayScriptName))]
    [NotifyPropertyChangedFor(nameof(HasCustomScript))]
    private string? _scriptPath;

    [ObservableProperty]
    private string _runtimeStatus = "Idle";

    [ObservableProperty]
    private int? _processId;

    public string DisplayOrUsername => string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;

    public bool HasCustomScript => !string.IsNullOrWhiteSpace(ScriptPath);

    public string DisplayScriptName => HasCustomScript
        ? Path.GetFileName(ScriptPath)
        : "(Uses Squad Script)";

    [RelayCommand]
    private void BrowseScript()
    {
        var fileService = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<IFileDialogService>();
        string? script = fileService.OpenFile(ClientFileSources.SkuaScriptsDIR, "Script Files (*.cs)|*.cs|All Files (*.*)|*.*");
        if (!string.IsNullOrWhiteSpace(script))
        {
            ScriptPath = script;
        }
    }

    [RelayCommand]
    private void ClearScript()
    {
        ScriptPath = null;
    }
}

public partial class ArmyQueueStepViewModel : ObservableObject
{
    public ArmyQueueStepViewModel(string name = "Squad 1")
    {
        _name = name;
        Members = new ObservableCollection<ArmyQueueMemberViewModel>();
    }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStepScriptName))]
    [NotifyPropertyChangedFor(nameof(HasStepScript))]
    private string? _stepScriptPath;

    [ObservableProperty]
    private string _stepStatus = "Pending";

    public ObservableCollection<ArmyQueueMemberViewModel> Members { get; }

    public bool HasStepScript => !string.IsNullOrWhiteSpace(StepScriptPath);

    public string DisplayStepScriptName => HasStepScript
        ? Path.GetFileName(StepScriptPath)
        : string.Empty;

    [RelayCommand]
    private void BrowseStepScript()
    {
        var fileService = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<IFileDialogService>();
        string? script = fileService.OpenFile(ClientFileSources.SkuaScriptsDIR, "Script Files (*.cs)|*.cs|All Files (*.*)|*.*");
        if (!string.IsNullOrWhiteSpace(script))
        {
            StepScriptPath = script;
        }
    }

    [RelayCommand]
    private void ClearStepScript()
    {
        StepScriptPath = null;
    }
}

public partial class ArmyQueueViewModel : ObservableRecipient
{
    private readonly AccountManagerViewModel _accountManager;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IFileDialogService _fileService;
    private readonly IWindowService _windowService;
    private readonly IScriptManager _scriptManager;
    private readonly List<AccountPickerItemViewModel> _allPickerAccounts = new();
    private CancellationTokenSource? _accountPickerSearchCts;
    private CancellationTokenSource? _queueCts;

    private ArmyQueueStepViewModel? _pendingScriptTargetStep;
    private ArmyQueueMemberViewModel? _pendingScriptTargetMember;
    public bool HasPendingScriptTarget => _pendingScriptTargetStep != null || _pendingScriptTargetMember != null;

    public ArmyQueueViewModel(
        AccountManagerViewModel accountManager,
        ISettingsService settingsService,
        IDialogService dialogService,
        IFileDialogService fileService,
        IWindowService windowService,
        IScriptManager scriptManager)
    {
        _accountManager = accountManager;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _fileService = fileService;
        _windowService = windowService;
        _scriptManager = scriptManager;

        Steps = new ObservableCollection<ArmyQueueStepViewModel>();
        Presets = new ObservableCollection<string>();
        AvailableAccounts = new ObservableCollection<AccountPickerItemViewModel>();

        _showHelpGuide = !_settingsService.Get("ArmyQueueHelpDismissed", false);

        StrongReferenceMessenger.Default.Register<ArmyQueueViewModel, LoadScriptMessage, int>(this,
            (int)MessageChannels.ScriptStatus, (r, m) => r.HandleLoadScript(m));

        LoadPresetsList();
        LoadActiveQueueOrDefaults();
        EnsureSelection();
    }

    private void OpenScriptRepo()
    {
        var scriptRepo = Ioc.Default.GetService<ScriptRepoViewModel>();
        if (scriptRepo != null)
        {
            _windowService.RegisterManagedWindow("Script Repo", scriptRepo);
        }
        _windowService.ShowManagedWindow("Script Repo");
    }

    [RelayCommand]
    public void OpenScriptRepoForStep(ArmyQueueStepViewModel? step)
    {
        _pendingScriptTargetStep = step ?? SelectedStep;
        _pendingScriptTargetMember = null;
        OpenScriptRepo();
    }

    [RelayCommand]
    public void OpenScriptRepoForMember(ArmyQueueMemberViewModel? member)
    {
        if (member == null) return;
        _pendingScriptTargetMember = member;
        _pendingScriptTargetStep = null;
        OpenScriptRepo();
    }

    private void HandleLoadScript(LoadScriptMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Path))
            return;

        if (_pendingScriptTargetMember != null)
        {
            _pendingScriptTargetMember.ScriptPath = message.Path;
            _pendingScriptTargetMember = null;
            AutoSave();
        }
        else if (_pendingScriptTargetStep != null)
        {
            _pendingScriptTargetStep.StepScriptPath = message.Path;
            _pendingScriptTargetStep = null;
            AutoSave();
        }
    }

    public static string GetStepStorageName(ArmyQueueStepViewModel step)
    {
        string safeSquad = Regex.Replace(step?.Name ?? "Squad", @"[^\w\.-]", "_");
        string scriptBase = Path.GetFileNameWithoutExtension(step?.StepScriptPath ?? "Script");
        return $"ArmyQueue_{safeSquad}_{scriptBase}";
    }

    public static string GetMemberStorageName(ArmyQueueStepViewModel? step, ArmyQueueMemberViewModel member)
    {
        string safeSquad = Regex.Replace(step?.Name ?? "Squad", @"[^\w\.-]", "_");
        string safeUser = Regex.Replace(member?.Username ?? "Player", @"[^\w\.-]", "_");
        string scriptBase = Path.GetFileNameWithoutExtension(member?.ScriptPath ?? "Script");
        return $"ArmyQueue_{safeSquad}_{safeUser}_{scriptBase}";
    }

    [RelayCommand]
    public async Task EditStepScriptConfig(ArmyQueueStepViewModel? step)
    {
        step ??= SelectedStep;
        if (step == null || string.IsNullOrWhiteSpace(step.StepScriptPath))
        {
            _dialogService.ShowMessageBox("Please select a script for this squad first.", "No Script Selected");
            return;
        }

        if (!File.Exists(step.StepScriptPath))
        {
            _dialogService.ShowMessageBox($"Script file not found:\n{step.StepScriptPath}", "Script Not Found");
            return;
        }

        string storageName = GetStepStorageName(step);
        await ConfigureScriptInternal(step.StepScriptPath, storageName);
    }

    [RelayCommand]
    public async Task EditMemberScriptConfig(ArmyQueueMemberViewModel? member)
    {
        if (member == null || string.IsNullOrWhiteSpace(member.ScriptPath))
        {
            _dialogService.ShowMessageBox("Please assign a script to this account first.", "No Script Selected");
            return;
        }

        if (!File.Exists(member.ScriptPath))
        {
            _dialogService.ShowMessageBox($"Script file not found:\n{member.ScriptPath}", "Script Not Found");
            return;
        }

        string storageName = GetMemberStorageName(SelectedStep, member);
        await ConfigureScriptInternal(member.ScriptPath, storageName);
    }

    private async Task ConfigureScriptInternal(string scriptPath, string storageName)
    {
        try
        {
            _scriptManager.SetLoadedScript(scriptPath);
            object? compiled = await Task.Run(() => _scriptManager.Compile(File.ReadAllText(scriptPath)));
            if (compiled == null)
            {
                _dialogService.ShowMessageBox("Failed to compile script for configuration.", "Script Error");
                return;
            }

            _scriptManager.OverrideStorage = storageName;
            _scriptManager.LoadScriptConfig(compiled);
            if (_scriptManager.Config != null && (_scriptManager.Config.Options.Count > 0 || _scriptManager.Config.MultipleOptions.Count > 0))
            {
                _scriptManager.Config.Configure();
            }
            else
            {
                _dialogService.ShowMessageBox("The loaded script has no options to configure.", "No Options");
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

    public ObservableCollection<ArmyQueueStepViewModel> Steps { get; }
    public ObservableCollection<string> Presets { get; }
    public ObservableCollection<AccountPickerItemViewModel> AvailableAccounts { get; private set; }

    [ObservableProperty]
    private ArmyQueueStepViewModel? _selectedStep;

    partial void OnSelectedStepChanged(ArmyQueueStepViewModel? value)
    {
        if (value != null)
            AutoSave();
    }

    [ObservableProperty]
    private string _queueName = "Daily Ultras";

    [ObservableProperty]
    private string? _selectedPreset;

    [ObservableProperty]
    private int _delayBetweenStepsSeconds = 5;

    [ObservableProperty]
    private int _delayBetweenLaunchesSeconds = 3;

    [ObservableProperty]
    private int _timeoutMinutes = 30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    private bool _isPaused;

    public bool CanPause => IsRunning && !IsPaused;
    public bool CanResume => IsRunning && IsPaused;

    private TaskCompletionSource<bool>? _pauseTcs;

    [ObservableProperty]
    private string _statusText = "Ready to start queue.";

    [ObservableProperty]
    private int _activeStepIndex;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _showHelpGuide;

    [ObservableProperty]
    private bool _showAccountPicker;

    [ObservableProperty]
    private string _accountPickerSearchText = string.Empty;

    partial void OnAccountPickerSearchTextChanged(string value) => _ = FilterAvailableAccountsAsync(value);

    /// <summary>
    /// Debounces account searches and replaces the bound collection once, avoiding per-item UI notifications.
    /// </summary>
    private async Task FilterAvailableAccountsAsync(string searchText)
    {
        CancellationTokenSource searchCts = new();
        CancellationToken searchToken = searchCts.Token;
        CancellationTokenSource? previousCts = Interlocked.Exchange(ref _accountPickerSearchCts, searchCts);
        previousCts?.Cancel();
        previousCts?.Dispose();

        try
        {
            await Task.Delay(200, searchToken);

            AccountPickerItemViewModel[] accounts = _allPickerAccounts.ToArray();
            string normalizedSearchText = searchText.Trim();
            AccountPickerItemViewModel[] matches = await Task.Run(() => accounts
                .Where(account => string.IsNullOrEmpty(normalizedSearchText) ||
                    account.DisplayName.Contains(normalizedSearchText, StringComparison.OrdinalIgnoreCase) ||
                    account.Username.Contains(normalizedSearchText, StringComparison.OrdinalIgnoreCase))
                .ToArray(), searchToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (searchToken.IsCancellationRequested || !ReferenceEquals(_accountPickerSearchCts, searchCts))
                    return;

                AvailableAccounts = new ObservableCollection<AccountPickerItemViewModel>(matches);
                OnPropertyChanged(nameof(AvailableAccounts));
            });
        }
        catch (OperationCanceledException)
        {
            // A newer search superseded this request.
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _accountPickerSearchCts, null, searchCts), searchCts))
                searchCts.Dispose();
        }
    }

    public void EnsureSelection()
    {
        if (SelectedStep == null && Steps.Count > 0)
        {
            SelectedStep = Steps[0];
        }
    }

    [RelayCommand]
    public void ToggleHelpGuide()
    {
        ShowHelpGuide = !ShowHelpGuide;
    }

    [RelayCommand]
    public void DismissHelpGuide()
    {
        ShowHelpGuide = false;
        _settingsService.Set("ArmyQueueHelpDismissed", true);
    }

    [RelayCommand]
    public void AddStep()
    {
        ArmyQueueStepViewModel step = new($"Squad {Steps.Count + 1}");
        AttachStepEvents(step);
        Steps.Add(step);
        SelectedStep = step;
        AutoSave();
    }

    [RelayCommand]
    public void RemoveStep(ArmyQueueStepViewModel? step)
    {
        RemoveStepSpecific(step ?? SelectedStep);
    }

    [RelayCommand]
    public void RemoveStepSpecific(ArmyQueueStepViewModel? step)
    {
        step ??= SelectedStep;
        if (step is null)
            return;

        if (Steps.Count <= 1)
        {
            step.Name = "Squad 1";
            step.StepScriptPath = null;
            step.Members.Clear();
            SelectedStep = step;
            AutoSave();
            return;
        }

        int index = Steps.IndexOf(step);
        Steps.Remove(step);
        if (SelectedStep == step)
        {
            int newIndex = Math.Clamp(index, 0, Steps.Count - 1);
            SelectedStep = Steps[newIndex];
        }
        AutoSave();
    }

    [RelayCommand]
    public void MoveStepUp(ArmyQueueStepViewModel? step)
    {
        step ??= SelectedStep;
        if (step is null) return;

        int index = Steps.IndexOf(step);
        if (index > 0)
        {
            Steps.Move(index, index - 1);
            SelectedStep = step;
            AutoSave();
        }
    }

    public void ReorderStep(ArmyQueueStepViewModel? draggedStep, ArmyQueueStepViewModel? targetStep)
    {
        if (IsRunning || draggedStep == null || targetStep == null || draggedStep == targetStep)
            return;

        int oldIndex = Steps.IndexOf(draggedStep);
        int newIndex = Steps.IndexOf(targetStep);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
            return;

        Steps.Move(oldIndex, newIndex);
        SelectedStep = draggedStep;
        AutoSave();
    }

    [RelayCommand]
    public void MoveStepDown(ArmyQueueStepViewModel? step)
    {
        step ??= SelectedStep;
        if (step is null) return;

        int index = Steps.IndexOf(step);
        if (index < Steps.Count - 1)
        {
            Steps.Move(index, index + 1);
            SelectedStep = step;
            AutoSave();
        }
    }

    [RelayCommand]
    public void PauseQueue()
    {
        if (!IsRunning || IsPaused)
            return;

        IsPaused = true;
        _pauseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        StatusText = "Queue PAUSED. Current squad will finish, but won't start next squad until resumed.";
    }

    [RelayCommand]
    public void ResumeQueue()
    {
        if (!IsRunning || !IsPaused)
            return;

        IsPaused = false;
        _pauseTcs?.TrySetResult(true);
        StatusText = "Queue resumed. Proceeding...";
    }

    [RelayCommand]
    public void OpenAccountPicker()
    {
        if (SelectedStep is null)
        {
            _dialogService.ShowMessageBox("Please create or select a squad first.", "Army Queue");
            return;
        }

        HashSet<string> currentMembers = new(
            SelectedStep.Members.Select(m => m.Username),
            StringComparer.OrdinalIgnoreCase);

        AccountPickerSearchText = string.Empty;
        _allPickerAccounts.Clear();

        foreach (IAccountItemViewModel acc in _accountManager.Accounts)
        {
            _allPickerAccounts.Add(new AccountPickerItemViewModel(acc)
            {
                IsSelected = currentMembers.Contains(acc.Username)
            });
        }

        _ = FilterAvailableAccountsAsync(AccountPickerSearchText);
        ShowAccountPicker = true;
    }

    [RelayCommand]
    public void ConfirmAccountPicker()
    {
        if (SelectedStep is null)
        {
            ShowAccountPicker = false;
            return;
        }

        List<AccountPickerItemViewModel> selected = _allPickerAccounts.Where(a => a.IsSelected).ToList();
        var selectedUsernames = new HashSet<string>(selected.Select(s => s.Username), StringComparer.OrdinalIgnoreCase);

        // Remove members that were unchecked
        for (int i = SelectedStep.Members.Count - 1; i >= 0; i--)
        {
            if (!selectedUsernames.Contains(SelectedStep.Members[i].Username))
                SelectedStep.Members.RemoveAt(i);
        }

        // Add newly checked accounts
        foreach (var item in selected)
        {
            if (!SelectedStep.Members.Any(m => m.Username.Equals(item.Username, StringComparison.OrdinalIgnoreCase)))
            {
                var newMember = new ArmyQueueMemberViewModel(item.Username, item.DisplayName);
                newMember.PropertyChanged += OnMemberPropertyChanged;
                SelectedStep.Members.Add(newMember);
            }
        }

        ShowAccountPicker = false;
        AutoSave();
    }

    [RelayCommand]
    public void CancelAccountPicker()
    {
        ShowAccountPicker = false;
    }

    [RelayCommand]
    public void SelectAllPicker()
    {
        foreach (var item in AvailableAccounts)
            item.IsSelected = true;
    }

    [RelayCommand]
    public void DeselectAllPicker()
    {
        foreach (AccountPickerItemViewModel item in _allPickerAccounts)
            item.IsSelected = false;
    }

    [RelayCommand]
    public void RemoveMember(ArmyQueueMemberViewModel? member)
    {
        if (SelectedStep != null && member != null)
        {
            member.PropertyChanged -= OnMemberPropertyChanged;
            SelectedStep.Members.Remove(member);
            AutoSave();
        }
    }

    [RelayCommand]
    public void ImportGroupAsStep()
    {
        SelectGroupDialogViewModel dialog = new(_accountManager.Groups);
        if (_dialogService.ShowDialog(dialog, "Import Group as Squad") == true && dialog.SelectedGroup != null)
        {
            ArmyQueueStepViewModel step = new(dialog.SelectedGroup.Name);
            foreach (IAccountItemViewModel acc in dialog.SelectedGroup.Accounts)
            {
                step.Members.Add(new ArmyQueueMemberViewModel(acc.Username, acc.DisplayOrUsername));
            }
            AttachStepEvents(step);
            Steps.Add(step);
            SelectedStep = step;
            AutoSave();
        }
    }

    [RelayCommand]
    public async Task StartQueue()
    {
        if (IsRunning)
            return;

        if (Steps.Count == 0 || Steps.All(s => s.Members.Count == 0))
        {
            _dialogService.ShowMessageBox("Please add at least one squad with accounts to the queue.", "Empty Queue");
            return;
        }

        IsRunning = true;
        _queueCts = new CancellationTokenSource();
        CancellationToken token = _queueCts.Token;

        try
        {
            await RunQueueAsync(token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Army Queue was stopped.";
        }
        catch (Exception ex)
        {
            StatusText = $"Queue Error: {ex.Message}";
            _dialogService.ShowMessageBox($"Queue encountered an error: {ex.Message}", "Queue Execution Error");
        }
        finally
        {
            IsRunning = false;
            IsPaused = false;
            _queueCts?.Dispose();
            _queueCts = null;
            _pauseTcs = null;
        }
    }

    [RelayCommand]
    public void StopQueue()
    {
        if (!IsRunning || _queueCts == null)
            return;

        if (_dialogService.ShowMessageBox("Are you sure you want to stop the running Army Queue?", "Stop Queue", true) == true)
        {
            StopQueueDirect();
        }
    }

    public void StopQueueDirect()
    {
        if (!IsRunning || _queueCts == null)
            return;

        _pauseTcs?.TrySetCanceled();
        _queueCts?.Cancel();
        IsPaused = false;
        StatusText = "Stopping Army Queue...";
    }

    public void PauseResumeQueue()
    {
        if (!IsRunning) return;
        if (IsPaused)
            ResumeQueue();
        else
            PauseQueue();
    }

    public void SetStepScriptDirect(int stepIndex, string? scriptPath)
    {
        if (stepIndex >= 0 && stepIndex < Steps.Count)
        {
            Steps[stepIndex].StepScriptPath = scriptPath;
            AutoSave();
        }
    }

    public void AddStepDirect(string squadName)
    {
        ArmyQueueStepViewModel step = new(string.IsNullOrWhiteSpace(squadName) ? ("Squad " + (Steps.Count + 1)) : squadName);
        AttachStepEvents(step);
        Steps.Add(step);
        SelectedStep = step;
        AutoSave();
    }

    public void RemoveStepDirect(int stepIndex)
    {
        if (stepIndex >= 0 && stepIndex < Steps.Count)
        {
            RemoveStepSpecific(Steps[stepIndex]);
        }
    }

    private async Task RunQueueAsync(CancellationToken token)
    {
        int totalSteps = Steps.Count;
        ActiveStepIndex = 0;
        ProgressValue = 0;

        foreach (var s in Steps)
        {
            s.StepStatus = "Pending";
            foreach (var m in s.Members)
            {
                m.RuntimeStatus = "Waiting";
                m.ProcessId = null;
            }
        }

        for (int i = 0; i < Steps.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            ActiveStepIndex = i + 1;
            ArmyQueueStepViewModel currentStep = Steps[i];
            currentStep.StepStatus = "Running";
            StatusText = $"Running Squad {ActiveStepIndex}/{totalSteps}: '{currentStep.Name}' ({currentStep.Members.Count} accounts)...";

            List<Process> activeProcesses = new();
            Dictionary<int, ArmyQueueMemberViewModel> pidToMember = new();

            // 1. Launch all members of the squad with staggered delay
            foreach (ArmyQueueMemberViewModel member in currentStep.Members)
            {
                token.ThrowIfCancellationRequested();

                IAccountItemViewModel? acc = _accountManager.Accounts.FirstOrDefault(
                    a => a.Username.Equals(member.Username, StringComparison.OrdinalIgnoreCase));

                if (acc is null)
                {
                    member.RuntimeStatus = "Account Missing";
                    continue;
                }

                member.RuntimeStatus = "Launching...";
                string targetScript = !string.IsNullOrWhiteSpace(member.ScriptPath)
                    ? member.ScriptPath
                    : (currentStep.StepScriptPath ?? string.Empty);

                string? optionsStorage = null;
                if (!string.IsNullOrWhiteSpace(targetScript))
                {
                    optionsStorage = !string.IsNullOrWhiteSpace(member.ScriptPath)
                        ? GetMemberStorageName(currentStep, member)
                        : GetStepStorageName(currentStep);
                }

                Process? proc = LaunchMemberProcess(acc, targetScript, optionsStorage);
                if (proc != null)
                {
                    member.ProcessId = proc.Id;
                    member.RuntimeStatus = "Running";
                    activeProcesses.Add(proc);
                    pidToMember[proc.Id] = member;
                }
                else
                {
                    member.RuntimeStatus = "Launch Failed";
                }

                if (DelayBetweenLaunchesSeconds > 0 && member != currentStep.Members.Last())
                {
                    await Task.Delay(DelayBetweenLaunchesSeconds * 1000, token);
                }
            }

            // 2. Monitor processes until all accounts finish naturally (no time limit)
            DateTime startTime = DateTime.UtcNow;

            while (activeProcesses.Any(p => !p.HasExited))
            {
                token.ThrowIfCancellationRequested();

                int completedCount = activeProcesses.Count(p => p.HasExited);
                int totalCount = activeProcesses.Count;
                TimeSpan elapsed = DateTime.UtcNow - startTime;

                foreach (Process p in activeProcesses)
                {
                    if (p.HasExited && pidToMember.TryGetValue(p.Id, out ArmyQueueMemberViewModel? m))
                    {
                        if (m.RuntimeStatus == "Running")
                            m.RuntimeStatus = "Finished";
                    }
                }

                string elapsedStr = elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m"
                    : $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";

                StatusText = $"Squad {ActiveStepIndex}/{totalSteps} '{currentStep.Name}': {completedCount}/{totalCount} accounts finished (Runtime: {elapsedStr})";
                ProgressValue = ((i * 100.0) / totalSteps) + ((completedCount * 100.0) / (totalSteps * Math.Max(1, totalCount)));

                await Task.Delay(1000, token);
            }

            currentStep.StepStatus = "Completed";
            foreach (var m in currentStep.Members)
            {
                if (m.RuntimeStatus == "Running")
                    m.RuntimeStatus = "Finished";
            }

            ProgressValue = ((i + 1) * 100.0) / totalSteps;

            // 3. Pause & Ghost session cooldown before next squad
            if (i < Steps.Count - 1)
            {
                if (IsPaused && _pauseTcs != null)
                {
                    string nextSquad = Steps[i + 1].Name;
                    StatusText = $"Squad '{currentStep.Name}' completed. Queue is PAUSED. Click 'Resume Queue' to start {nextSquad}.";
                    using var reg = token.Register(() => _pauseTcs.TrySetCanceled());
                    try
                    {
                        await _pauseTcs.Task;
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }

                int cooldown = Math.Max(1, DelayBetweenStepsSeconds);
                for (int c = cooldown; c > 0; c--)
                {
                    token.ThrowIfCancellationRequested();

                    if (IsPaused && _pauseTcs != null)
                    {
                        string nextSquad = Steps[i + 1].Name;
                        StatusText = $"Squad '{currentStep.Name}' completed. Queue is PAUSED. Click 'Resume Queue' to start {nextSquad}.";
                        using var reg = token.Register(() => _pauseTcs.TrySetCanceled());
                        try
                        {
                            await _pauseTcs.Task;
                        }
                        catch (TaskCanceledException)
                        {
                            break;
                        }
                    }

                    StatusText = $"Squad '{currentStep.Name}' completed! Waiting {c}s ghost session cooldown before next squad...";
                    await Task.Delay(1000, token);
                }
            }
        }

        StatusText = "All Army Queue squads completed successfully!";
        ProgressValue = 100;
        _dialogService.ShowMessageBox("All scheduled Army Queue squads have completed their runs!", "Army Queue Finished");
    }

    private Process? LaunchMemberProcess(IAccountItemViewModel acc, string scriptPath, string? optionsStorage = null)
    {
        try
        {
            string exePath = SkuaExecutableLocator.ExecutablePath;
            string server = _settingsService.Get("LastServer", "Twilly");

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
                    server
                }
            };

            if (_settingsService.Get("syncTheme", false))
            {
                psi.ArgumentList.Add("--use-theme");
                psi.ArgumentList.Add(_settingsService.Get("CurrentTheme", "no-theme"));
            }

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

            psi.ArgumentList.Add("--exit-on-script-stop");

            Process? process = Process.Start(psi);
            if (process != null)
            {
                StrongReferenceMessenger.Default.Send(new AddProcessMessage(process, acc.DisplayOrUsername));
            }

            return process;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error launching queue account {acc.Username}: {ex.Message}");
            return null;
        }
    }

    [RelayCommand]
    public void SavePreset()
    {
        if (string.IsNullOrWhiteSpace(QueueName))
        {
            _dialogService.ShowMessageBox("Please enter a name for this Army Queue preset.", "Save Preset");
            return;
        }

        try
        {
            string configDir = ClientFileSources.SkuaDIR;
            Directory.CreateDirectory(configDir);
            string filePath = Path.Combine(configDir, "ArmyQueues.json");

            Dictionary<string, ArmyQueueConfig> configs = LoadAllConfigs();

            ArmyQueueConfig config = new()
            {
                Name = QueueName.Trim(),
                DelayBetweenStepsSeconds = DelayBetweenStepsSeconds,
                DelayBetweenLaunchesSeconds = DelayBetweenLaunchesSeconds,
                TimeoutMinutes = TimeoutMinutes,
                Steps = Steps.Select(s => new ArmyQueueStep
                {
                    Name = s.Name,
                    StepScriptPath = s.StepScriptPath,
                    Accounts = s.Members.Select(m => new ArmyQueueAccount
                    {
                        Username = m.Username,
                        DisplayName = m.DisplayName,
                        ScriptPath = m.ScriptPath
                    }).ToList()
                }).ToList()
            };

            configs[config.Name] = config;
            File.WriteAllText(filePath, JsonSerializer.Serialize(configs, new JsonSerializerOptions { WriteIndented = true }));

            LoadPresetsList();
            SelectedPreset = config.Name;
            _dialogService.ShowMessageBox($"Preset '{config.Name}' saved successfully!", "Preset Saved");
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Failed to save preset: {ex.Message}", "Save Error");
        }
    }

    [RelayCommand]
    public void LoadSelectedPreset()
    {
        if (string.IsNullOrWhiteSpace(SelectedPreset))
            return;

        try
        {
            Dictionary<string, ArmyQueueConfig> configs = LoadAllConfigs();
            if (!configs.TryGetValue(SelectedPreset, out ArmyQueueConfig? config) || config == null)
            {
                _dialogService.ShowMessageBox($"Preset '{SelectedPreset}' not found.", "Load Error");
                return;
            }

            QueueName = config.Name;
            DelayBetweenStepsSeconds = config.DelayBetweenStepsSeconds > 0 ? config.DelayBetweenStepsSeconds : 5;
            DelayBetweenLaunchesSeconds = config.DelayBetweenLaunchesSeconds > 0 ? config.DelayBetweenLaunchesSeconds : 3;
            TimeoutMinutes = config.TimeoutMinutes > 0 ? config.TimeoutMinutes : 30;

            Steps.Clear();
            foreach (ArmyQueueStep s in config.Steps)
            {
                ArmyQueueStepViewModel stepVm = new(s.Name)
                {
                    StepScriptPath = s.StepScriptPath
                };
                foreach (ArmyQueueAccount a in s.Accounts)
                {
                    stepVm.Members.Add(new ArmyQueueMemberViewModel(a.Username, a.DisplayName, a.ScriptPath));
                }
                AttachStepEvents(stepVm);
                Steps.Add(stepVm);
            }

            SelectedStep = Steps.FirstOrDefault();
            StatusText = $"Loaded preset '{config.Name}'.";
            AutoSave();
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Failed to load preset: {ex.Message}", "Load Error");
        }
    }

    [RelayCommand]
    public void DeleteSelectedPreset()
    {
        if (string.IsNullOrWhiteSpace(SelectedPreset))
            return;

        if (_dialogService.ShowMessageBox($"Are you sure you want to delete preset '{SelectedPreset}'?", "Delete Preset", true) != true)
            return;

        try
        {
            string filePath = Path.Combine(ClientFileSources.SkuaDIR, "ArmyQueues.json");
            Dictionary<string, ArmyQueueConfig> configs = LoadAllConfigs();
            configs.Remove(SelectedPreset);
            File.WriteAllText(filePath, JsonSerializer.Serialize(configs, new JsonSerializerOptions { WriteIndented = true }));

            LoadPresetsList();
            SelectedPreset = Presets.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Failed to delete preset: {ex.Message}", "Delete Error");
        }
    }

    private Dictionary<string, ArmyQueueConfig> LoadAllConfigs()
    {
        string filePath = Path.Combine(ClientFileSources.SkuaDIR, "ArmyQueues.json");
        if (!File.Exists(filePath))
            return new Dictionary<string, ArmyQueueConfig>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Dictionary<string, ArmyQueueConfig>>(json)
                ?? new Dictionary<string, ArmyQueueConfig>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, ArmyQueueConfig>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void LoadPresetsList()
    {
        Presets.Clear();
        Dictionary<string, ArmyQueueConfig> configs = LoadAllConfigs();
        foreach (string name in configs.Keys)
            Presets.Add(name);

        if (string.IsNullOrWhiteSpace(SelectedPreset) && Presets.Count > 0)
            SelectedPreset = Presets[0];
    }

    private void AttachStepEvents(ArmyQueueStepViewModel step)
    {
        step.PropertyChanged -= OnStepPropertyChanged;
        step.PropertyChanged += OnStepPropertyChanged;
        step.Members.CollectionChanged -= OnMembersCollectionChanged;
        step.Members.CollectionChanged += OnMembersCollectionChanged;
        foreach (var m in step.Members)
        {
            m.PropertyChanged -= OnMemberPropertyChanged;
            m.PropertyChanged += OnMemberPropertyChanged;
        }
    }

    private void OnStepPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        AutoSave();
    }

    private void OnMembersCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (ArmyQueueMemberViewModel m in e.NewItems)
            {
                m.PropertyChanged -= OnMemberPropertyChanged;
                m.PropertyChanged += OnMemberPropertyChanged;
            }
        }
        AutoSave();
    }

    private void OnMemberPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        AutoSave();
    }

    public void AutoSave()
    {
        if (IsRunning)
            return;

        try
        {
            string configDir = ClientFileSources.SkuaDIR;
            Directory.CreateDirectory(configDir);
            string filePath = Path.Combine(configDir, "ArmyQueueActive.json");

            ArmyQueueConfig config = new()
            {
                Name = string.IsNullOrWhiteSpace(QueueName) ? "Daily Ultras" : QueueName.Trim(),
                DelayBetweenStepsSeconds = DelayBetweenStepsSeconds,
                DelayBetweenLaunchesSeconds = DelayBetweenLaunchesSeconds,
                Steps = Steps.Select(s => new ArmyQueueStep
                {
                    Name = s.Name,
                    StepScriptPath = s.StepScriptPath,
                    Accounts = s.Members.Select(m => new ArmyQueueAccount
                    {
                        Username = m.Username,
                        DisplayName = m.DisplayName,
                        ScriptPath = m.ScriptPath
                    }).ToList()
                }).ToList()
            };

            File.WriteAllText(filePath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error auto-saving Army Queue: {ex.Message}");
        }
    }

    private void LoadActiveQueueOrDefaults()
    {
        try
        {
            string configDir = ClientFileSources.SkuaDIR;
            string filePath = Path.Combine(configDir, "ArmyQueueActive.json");

            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                ArmyQueueConfig? config = JsonSerializer.Deserialize<ArmyQueueConfig>(json);
                if (config != null && config.Steps.Count > 0)
                {
                    QueueName = string.IsNullOrWhiteSpace(config.Name) ? "Daily Ultras" : config.Name;
                    Steps.Clear();
                    foreach (ArmyQueueStep s in config.Steps)
                    {
                        ArmyQueueStepViewModel stepVm = new(s.Name)
                        {
                            StepScriptPath = s.StepScriptPath
                        };
                        foreach (ArmyQueueAccount a in s.Accounts)
                        {
                            stepVm.Members.Add(new ArmyQueueMemberViewModel(a.Username, a.DisplayName, a.ScriptPath));
                        }
                        AttachStepEvents(stepVm);
                        Steps.Add(stepVm);
                    }
                    SelectedStep = Steps.FirstOrDefault();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error loading active Army Queue: {ex.Message}");
        }

        AddDefaultStep();
    }

    private void AddDefaultStep()
    {
        ArmyQueueStepViewModel step1 = new("Squad 1");
        AttachStepEvents(step1);
        Steps.Add(step1);
        SelectedStep = step1;
        AutoSave();
    }
}
