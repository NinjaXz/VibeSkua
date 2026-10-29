using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Models.Servers;
using Skua.Core.Utils;
using Skua.Manager.Avalonia.Services;
using Skua.Shared.Avalonia.ViewModels;
using Skua.Shared.Avalonia.ViewModels.Dialogs;
using Skua.Shared.Avalonia.ViewModels.ScriptRepo;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Threading;

namespace Skua.Manager.Avalonia.ViewModels;

public sealed partial class AccountManagerViewModel : BotControlViewModelBase
{
    // Constructor and Dependencies
    public AccountManagerViewModel(ISettingsService settingsService, IDialogService dialogService, IFileDialogService fileService, IScriptManager scriptManager)
        : base("Accounts")
    {
        IMessengerExtensions.Register<AccountManagerViewModel, RemoveAccountMessage>(Messenger, this, (r, m) => r._RemoveAccount(m.Account));
        IMessengerExtensions.Register<AccountManagerViewModel, AccountSelectedMessage>(Messenger, this, AccountSelected);
        IMessengerExtensions.Register<AccountManagerViewModel, StartAccountMessage>(Messenger, this, (r, m) => r._StartAccount(m.Account, m.WithScript));
        IMessengerExtensions.Register<AccountManagerViewModel, RemoveGroupMessage>(Messenger, this, (r, m) => r._RemoveGroup(m.Group));
        IMessengerExtensions.Register<AccountManagerViewModel, RenameGroupMessage>(Messenger, this, (r, m) => r._RenameGroup(m.Group));
        IMessengerExtensions.Register<AccountManagerViewModel, RemoveAccountFromGroupMessage>(Messenger, this, (r, m) => r._RemoveAccountFromGroup(m.Group, m.Account));
        IMessengerExtensions.Register<AccountManagerViewModel, ReplaceAccountInGroupMessage>(Messenger, this, (r, m) => r._ReplaceAccountInGroup(m.Group, m.CurrentAccount, m.ReplacementAccount));
        IMessengerExtensions.Register<AccountManagerViewModel, RefreshAccountDisplayNamesMessage>(Messenger, this, (r, _) => r.RefreshAccountDisplayNames());
        IMessengerExtensions.Register<AccountManagerViewModel, StartGroupMessage>(Messenger, this, (r, m) => r._StartGroup(m.Group, m.WithScript, m.NewWindow));
        StrongReferenceMessenger.Default.Register<AccountManagerViewModel, LoadScriptMessage, int>(this,
            (int)MessageChannels.ScriptStatus, (r, m) => r.HandleLoadScript(m));
        _settingsService = settingsService;
        _dialogService = dialogService;
        _fileService = fileService;
        _scriptManager = scriptManager;
        ServerList = new();
        Task.Run(async () => await _GetServers());
        Accounts = new();
        _GetSavedAccounts();
        _GetSavedGroups();
        RefreshTagFilters();
        _syncThemes = _settingsService.Get("syncTheme", false);
        _scriptPath = _settingsService.Get("AutoStartScriptPath", string.Empty);
        _startWithScript = _settingsService.Get("AutoStartScript", false);
        _showTags = _settingsService.Get("ShowAccountTags", true);
        _showGridView = _settingsService.Get("ShowGridView", false);
        SetupSettingsWatcher();
    }

    private readonly string _exePath = SkuaExecutableLocator.ExecutablePath;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IFileDialogService _fileService;
    private readonly IScriptManager _scriptManager;

    // Collections
    public RangedObservableCollection<IAccountItemViewModel> Accounts { get; }
    public RangedObservableCollection<IAccountItemViewModel> FilteredAccounts { get; } = new();
    public RangedObservableCollection<IGroupItemViewModel> Groups { get; } = new();

    // Account Properties
    [ObservableProperty]
    private int _selectedAccountQuant;

    [ObservableProperty]
    private string _usernameInput;

    [ObservableProperty]
    private string _displayNameInput;

    public string PasswordInput { private get; set; }

    // UI State Properties
    [ObservableProperty]
    private bool _showTags = true;

    partial void OnShowTagsChanged(bool value)
    {
        PerformInternalSettingsChange(() => _settingsService.Set("ShowAccountTags", value));
    }

    [ObservableProperty]
    private bool _showGridView;

    partial void OnShowGridViewChanged(bool value)
    {
        PerformInternalSettingsChange(() => _settingsService.Set("ShowGridView", value));
    }

    [ObservableProperty]
    private int _columns = 3;

    public IReadOnlyList<int> ColumnOptions { get; } = [2, 3, 4, 5, 6];

    partial void OnColumnsChanged(int value)
    {
        if (value < 2)
            Columns = 2;
        else if (value > 6)
            Columns = 6;
    }

    // Tag Filtering Properties
    [ObservableProperty]
    private bool _isFilterPopupOpen;

    private string _tagFilter = string.Empty;
    public string TagFilter
    {
        get => _tagFilter;
        set
        {
            if (SetProperty(ref _tagFilter, value))
                ApplyTagFilter();
        }
    }

    private string _tagSearchText = string.Empty;
    public string TagSearchText
    {
        get => _tagSearchText;
        set
        {
            if (SetProperty(ref _tagSearchText, value))
                _UpdateFilteredTags();
        }
    }

    public ObservableCollection<TagFilterItem> AllTags { get; } = new();
    public ObservableCollection<TagFilterItem> FilteredTags { get; } = new();
    private HashSet<string> _selectedTagFilters = new();

    // Script and Server Properties
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScriptPath))]
    private string _scriptPath = string.Empty;

    public bool HasScriptPath => !string.IsNullOrWhiteSpace(ScriptPath);

    partial void OnScriptPathChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            PerformInternalSettingsChange(() => _settingsService.Set("AutoStartScriptPath", value));
    }

    [ObservableProperty]
    private bool _startWithScript;

    partial void OnStartWithScriptChanged(bool value)
    {
        PerformInternalSettingsChange(() => _settingsService.Set("AutoStartScript", value));
    }

    [ObservableProperty]
    private Server _selectedServer;

    partial void OnSelectedServerChanged(Server value)
    {
        if (value != null)
            PerformInternalSettingsChange(() => _settingsService.Set("LastServer", value.Name));
    }

    private List<Server> _cachedServers = new();

    [ObservableProperty]
    private RangedObservableCollection<Server> _serverList;

    private bool _syncThemes;

    // Account Management
    [RelayCommand]
    public void AddAccount()
    {
        if (string.IsNullOrWhiteSpace(UsernameInput) || string.IsNullOrWhiteSpace(PasswordInput))
        {
            _dialogService.ShowMessageBox("Username or password must not be empty", "Missing Input");
            return;
        }

        string username = UsernameInput.Trim();
        string password = PasswordInput;
        string displayName = string.IsNullOrWhiteSpace(DisplayNameInput) ? username : DisplayNameInput.Trim();

        // Check if account with this username already exists
        IAccountItemViewModel? existingAccount = Accounts.FirstOrDefault(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (existingAccount != null)
        {
            // Update existing account
            existingAccount.Password = password;
            existingAccount.DisplayName = displayName;
        }
        else
        {
            // Create new account
            AccountItemViewModel newAccount = new()
            {
                Username = username,
                Password = password,
                DisplayName = displayName
            };
            Accounts.Add(newAccount);
            ReindexAccounts();
        }

        ApplyTagFilter();

        UsernameInput = string.Empty;
        DisplayNameInput = string.Empty;
        StrongReferenceMessenger.Default.Send<ClearPasswordBoxMessage>();

        _SaveAccounts();
    }

    [RelayCommand]
    public async Task StartAccounts()
    {
        await StartAccountBatch(Accounts.Where(acc => acc.UseCheck).ToList(), "selected accounts", StartWithScript);
    }

    [RelayCommand]
    public async Task StartAccountsInNewWindow()
    {
        await StartAccountBatch(Accounts.Where(acc => acc.UseCheck).ToList(), "selected accounts", StartWithScript, true);
    }

    [RelayCommand]
    public async Task StartAllAccountsInNewWindow()
    {
        await StartAccountBatch(Accounts.ToList(), "all accounts", StartWithScript, true);
    }

    [RelayCommand]
    public async Task StartAllAccounts()
    {
        await StartAccountBatch(Accounts.ToList(), "all accounts", StartWithScript);
    }

    private async Task StartAccountBatch(List<IAccountItemViewModel> accounts, string batchName, bool runScript, bool newWindow = false)
    {
        if (_isLaunching || accounts.Count == 0)
            return;

        try
        {
            _isLaunching = true;
            _syncThemes = _settingsService.Get("syncTheme", false);
            string? hostInstanceId = newWindow ? Guid.NewGuid().ToString("N") : null;

            foreach (IAccountItemViewModel account in accounts)
            {
                _LaunchAcc(account.Username, account.Password, account.DisplayName, runScript, hostInstanceId);
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error starting {batchName}{(newWindow ? " in a new window" : string.Empty)}: {ex.Message}");
        }
        finally
        {
            _isLaunching = false;
            if (_pendingReload)
            {
                _pendingReload = false;
                ScheduleReload();
            }
        }
    }

    [RelayCommand]
    public async Task RemoveAccounts()
    {
        List<IAccountItemViewModel> toRemove = Accounts.Where(acc => acc.UseCheck).ToList();
        if (toRemove.Count == 0)
        {
            _dialogService.ShowMessageBox("No accounts selected to remove.", "Remove Accounts");
            return;
        }

        string message = toRemove.Count == 1
            ? $"Are you sure you want to remove account '{toRemove[0].Username}'?"
            : $"Are you sure you want to remove all {toRemove.Count} selected accounts?";

        if (_dialogService.ShowMessageBox(message, "Confirm Account Removal", true) != true)
            return;

        foreach (IAccountItemViewModel acc in toRemove)
            _RemoveAccountInternal(acc);

        _SaveAccounts();
        _SaveGroups();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (IAccountItemViewModel account in Accounts)
        {
            account.UseCheck = false;
        }
        SelectedAccountQuant = 0;
    }

    [RelayCommand]
    private void MoveSelectedUp()
    {
        if (SelectedAccountQuant != 1)
            return;

        IAccountItemViewModel? selectedAccount = Accounts.FirstOrDefault(account => account.UseCheck);
        if (selectedAccount is null)
            return;

        int index = Accounts.IndexOf(selectedAccount);
        if (index <= 0)
            return;

        Accounts.Move(index, index - 1);
        _SaveAccounts();
        ApplyTagFilter();
    }

    [RelayCommand]
    private void MoveSelectedDown()
    {
        if (SelectedAccountQuant != 1)
            return;

        IAccountItemViewModel? selectedAccount = Accounts.FirstOrDefault(account => account.UseCheck);
        if (selectedAccount is null)
            return;

        int index = Accounts.IndexOf(selectedAccount);
        if (index < 0 || index >= Accounts.Count - 1)
            return;

        Accounts.Move(index, index + 1);
        _SaveAccounts();
        ApplyTagFilter();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (IAccountItemViewModel account in Accounts)
        {
            account.UseCheck = false;
        }

        foreach (IAccountItemViewModel account in FilteredAccounts)
        {
            account.UseCheck = true;
        }
        SelectedAccountQuant = Accounts.Count(a => a.UseCheck);
    }

    private void _RemoveAccount(IAccountItemViewModel account)
    {
        string message = $"Are you sure you want to remove account '{account.Username}'?";
        if (_dialogService.ShowMessageBox(message, "Confirm Account Removal", true) != true)
            return;

        _RemoveAccountInternal(account);
        _SaveAccounts();
        _SaveGroups();
    }

    private void _RemoveAccountInternal(IAccountItemViewModel account)
    {
        if (account.UseCheck)
            SelectedAccountQuant--;

        // Remove account from all groups
        foreach (IGroupItemViewModel group in Groups.ToList())
        {
            if (group.Accounts.Contains(account))
            {
                group.Accounts.Remove(account);
            }
        }

        Accounts.Remove(account);
        FilteredAccounts.Remove(account);
        ReindexAccounts();
    }

    private void AccountSelected(AccountManagerViewModel recipient, AccountSelectedMessage message)
    {
        if (message.Add)
            recipient.SelectedAccountQuant++;
        else
            recipient.SelectedAccountQuant--;
    }

    private void _StartAccount(IAccountItemViewModel account, bool withScript)
    {
        _syncThemes = _settingsService.Get("syncTheme", false);

        if (withScript && string.IsNullOrEmpty(ScriptPath))
        {
            _dialogService.ShowMessageBox("No script selected. Please select a script first.", "No Script");
            return;
        }

        bool runScript = withScript || StartWithScript;
        _LaunchAcc(account.Username, account.Password, account.DisplayName, runScript);
    }

    private async void _StartGroup(IGroupItemViewModel group, bool withScript, bool newWindow = false)
    {
        if (_isLaunching)
            return;

        if (withScript && string.IsNullOrEmpty(ScriptPath))
        {
            _dialogService.ShowMessageBox("No script selected. Please select a script first.", "No Script");
            return;
        }

        bool runScript = withScript || StartWithScript;
        await StartAccountBatch(group.Accounts.ToList(), $"group '{group.Name}'", runScript, newWindow);
    }

    private void _LaunchAcc(string username, string password, string? displayName = null, bool? withScript = null, string? hostInstanceId = null)
    {
        try
        {
            ProcessStartInfo psi = new(_exePath)
            {
                ArgumentList =
                {
                    "-u",
                    username,
                    "-p",
                    password,
                    "-s",
                    SelectedServer?.Name ?? "Twilly"
                },
                WorkingDirectory = SkuaExecutableLocator.WorkingDirectory
            };

            if (!string.IsNullOrWhiteSpace(hostInstanceId))
            {
                psi.ArgumentList.Add("--host-instance");
                psi.ArgumentList.Add(hostInstanceId);
            }
            if (_syncThemes)
            {
                psi.ArgumentList.Add("--use-theme");
                psi.ArgumentList.Add(_settingsService.Get("CurrentTheme", "no-theme"));
            }

            bool shouldUseScript = withScript ?? StartWithScript;
            if (shouldUseScript && !string.IsNullOrEmpty(ScriptPath))
            {
                psi.ArgumentList.Add("--run-script");
                psi.ArgumentList.Add(ScriptPath);
            }
            else if (!string.IsNullOrEmpty(ScriptPath))
            {
                psi.ArgumentList.Add("--load-script");
                psi.ArgumentList.Add(ScriptPath);
            }

            Process? process = Process.Start(psi);
            if (process != null)
            {
                string accountName = !string.IsNullOrWhiteSpace(displayName) ? displayName : username;
                StrongReferenceMessenger.Default.Send(new AddProcessMessage(process, accountName));
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Error while starting process: {ex.Message}", "Launch Error");
        }
    }

    // Group Management
    public void AddGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return;

        if (Groups.Any(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
        {
            _dialogService.ShowMessageBox($"A group named '{groupName}' already exists.", "Duplicate Group");
            return;
        }

        GroupItemViewModel newGroup = new(groupName);
        Groups.Add(newGroup);
        _SaveGroups();
    }

    public void AddAccountToGroup(IAccountItemViewModel account, IGroupItemViewModel group)
    {
        if (!group.Accounts.Contains(account))
        {
            group.Accounts.Add(account);
            _SaveGroups();
        }
    }

    private void _RemoveGroup(IGroupItemViewModel group)
    {
        if (Groups.Contains(group))
        {
            if (_dialogService.ShowMessageBox($"Are you sure you want to delete group '{group.Name}'?", "Confirm Delete Group", true) != true)
                return;

            Groups.Remove(group);
            _SaveGroups();
        }
    }

    private void _RenameGroup(IGroupItemViewModel group)
    {
        InputDialogViewModel inputDialogViewModel = new(
            "Rename Group", "Enter new group name", "Group Name", numericInputOnly: false)
        {
            DialogTextInput = group.Name
        };

        bool? result = _dialogService.ShowDialog(inputDialogViewModel, "Rename Group");

        if (result == true && !string.IsNullOrWhiteSpace(inputDialogViewModel.DialogTextInput))
        {
            string newName = inputDialogViewModel.DialogTextInput.Trim();

            if (newName.Equals(group.Name, StringComparison.OrdinalIgnoreCase))
                return;

            if (Groups.Any(g => g.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                _dialogService.ShowMessageBox($"A group named '{newName}' already exists.", "Duplicate Group");
                return;
            }

            group.Name = newName;

            _SaveGroups();
        }
    }

    private void _RemoveAccountFromGroup(IGroupItemViewModel group, IAccountItemViewModel account)
    {
        if (group.Accounts.Contains(account))
        {
            group.Accounts.Remove(account);
            _SaveGroups();
        }
    }

    private void _ReplaceAccountInGroup(IGroupItemViewModel group, IAccountItemViewModel currentAccount, IAccountItemViewModel replacementAccount)
    {
        int index = group.Accounts.IndexOf(currentAccount);
        if (index < 0)
            return;

        bool duplicate = group.Accounts
            .Where((_, i) => i != index)
            .Any(a => string.Equals(a.Username, replacementAccount.Username, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
            return;

        group.Accounts[index] = replacementAccount;
        _SaveGroups();
    }

    // Tag Filtering
    public void RefreshTagFilters()
    {
        // Remember which tags were selected before refresh
        HashSet<string> previouslySelected = new(_selectedTagFilters, StringComparer.OrdinalIgnoreCase);

        AllTags.Clear();
        Dictionary<string, int> tagCounts = new(StringComparer.OrdinalIgnoreCase);

        foreach (IAccountItemViewModel account in Accounts)
        {
            foreach (string tag in account.Tags)
            {
                if (tagCounts.ContainsKey(tag))
                    tagCounts[tag]++;
                else
                    tagCounts[tag] = 1;
            }
        }

        foreach (KeyValuePair<string, int> kvp in tagCounts.OrderBy(x => x.Key))
        {
            TagFilterItem tagItem = new(kvp.Key, kvp.Value);

            // Restore selection state if this tag was previously selected
            if (previouslySelected.Contains(kvp.Key))
            {
                tagItem.IsSelected = true;
            }

            tagItem.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TagFilterItem.IsSelected))
                {
                    _OnTagFilterSelectionChanged();
                }
            };
            AllTags.Add(tagItem);
        }

        // Remove any selected tags that no longer exist
        _selectedTagFilters.RemoveWhere(tag => !tagCounts.ContainsKey(tag));

        _UpdateFilteredTags();
    }

    [RelayCommand]
    private void ClearTagFilters()
    {
        foreach (TagFilterItem tag in AllTags)
        {
            tag.IsSelected = false;
        }
        TagSearchText = string.Empty;
    }

    public void ApplyTagFilter()
    {
        // Filter uses OR logic: shows accounts that have ANY of the selected tags
        List<IAccountItemViewModel> target = _selectedTagFilters.Count == 0
            ? Accounts.ToList()
            : Accounts.Where(a => a.Tags.Any(t => _selectedTagFilters.Contains(t))).ToList();

        // Update in place: clearing and refilling rebuilt every row and threw the list back to the top
        // each time the Manager got focus or a client saved settings.
        if (FilteredAccounts.SequenceEqual(target))
            return;

        for (int i = FilteredAccounts.Count - 1; i >= 0; i--)
        {
            if (!target.Contains(FilteredAccounts[i]))
                FilteredAccounts.RemoveAt(i);
        }

        for (int i = 0; i < target.Count; i++)
        {
            if (i < FilteredAccounts.Count && ReferenceEquals(FilteredAccounts[i], target[i]))
                continue;

            int current = FilteredAccounts.IndexOf(target[i]);
            if (current >= 0)
                FilteredAccounts.Move(current, i);
            else
                FilteredAccounts.Insert(i, target[i]);
        }
    }

    private void _UpdateFilteredTags()
    {
        FilteredTags.Clear();

        IEnumerable<TagFilterItem> filtered = string.IsNullOrWhiteSpace(TagSearchText)
            ? AllTags
            : AllTags.Where(t => t.Name.Contains(TagSearchText, StringComparison.OrdinalIgnoreCase));

        foreach (TagFilterItem tag in filtered)
        {
            FilteredTags.Add(tag);
        }
    }

    private void _OnTagFilterSelectionChanged()
    {
        _selectedTagFilters.Clear();
        foreach (TagFilterItem tag in AllTags.Where(t => t.IsSelected))
        {
            _selectedTagFilters.Add(tag.Name);
        }
        ApplyTagFilter();
    }

    // Persistence
    public void SaveAccounts() => _SaveAccounts();

    [RelayCommand]
    public void SaveSetup()
    {
        PerformInternalSettingsChange(() =>
        {
            _SaveAccountsInternal();
            _settingsService.Set("AutoStartScriptPath", ScriptPath);
            _settingsService.Set("AutoStartScript", StartWithScript);
        });
        _dialogService.ShowMessageBox("Manager setup and account selection successfully saved.", "Setup Saved");
    }

    private volatile bool _isLaunching = false;
    private volatile bool _pendingReload = false;
    private int _isSavingInternalCounter = 0;
    private FileSystemWatcher? _settingsWatcher;
    private RegisteredWaitHandle? _registeredWaitHandle;
    private EventWaitHandle? _globalNotifyEvent;
    private CancellationTokenSource? _reloadDebounceCts;
    private readonly object _reloadLock = new();

    private void PerformInternalSettingsChange(Action action)
    {
        Interlocked.Increment(ref _isSavingInternalCounter);
        try
        {
            action();
        }
        finally
        {
            Task.Delay(1000).ContinueWith(_ => Interlocked.Decrement(ref _isSavingInternalCounter));
        }
    }

    public void ScheduleReload()
    {
        if (_isSavingInternalCounter > 0) return;
        if (_isLaunching)
        {
            _pendingReload = true;
            return;
        }
        lock (_reloadLock)
        {
            _reloadDebounceCts?.Cancel();
            _reloadDebounceCts?.Dispose();
            _reloadDebounceCts = new CancellationTokenSource();
            CancellationToken token = _reloadDebounceCts.Token;

            Task.Delay(250, token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_isSavingInternalCounter > 0 || _isLaunching)
                    {
                        if (_isLaunching) _pendingReload = true;
                        return;
                    }
                    ReloadAccountsAndGroups();
                }, DispatcherPriority.Background);
            }, TaskScheduler.Default);
        }
    }

    private void SetupSettingsWatcher()
    {
        try
        {
            // 1. Cross-process instant signaling via named EventWaitHandle
            try
            {
                _globalNotifyEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Global\VibeSkua.Settings.Changed");
                _registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(
                    _globalNotifyEvent,
                    (state, timedOut) =>
                    {
                        ScheduleReload();
                    },
                    null,
                    -1,
                    false);
            }
            catch { }

            // 2. FileSystemWatcher watching all write/create/rename events in Skua directory
            string dir = ClientFileSources.SkuaDIR;
            if (Directory.Exists(dir))
            {
                _settingsWatcher = new FileSystemWatcher(dir)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                void OnFileChanged(string? fileName)
                {
                    if (string.IsNullOrEmpty(fileName)) return;
                    if (!fileName.Equals("Skua.settings.json", StringComparison.OrdinalIgnoreCase)) return;
                    ScheduleReload();
                }

                _settingsWatcher.Changed += (s, e) => OnFileChanged(e.Name);
                _settingsWatcher.Created += (s, e) => OnFileChanged(e.Name);
                _settingsWatcher.Renamed += (s, e) => OnFileChanged(e.Name);
            }
        }
        catch { }
    }

    [RelayCommand]
    public void ReloadAccounts()
    {
        ReloadAccountsAndGroups();
    }

    public void ReloadAccountsAndGroups()
    {
        try
        {
            _settingsService.ReloadSettings();
            _GetSavedAccounts();
            _GetSavedGroups();
            RefreshTagFilters();
            ApplyTagFilter();
        }
        catch { }
    }

    private void _SaveAccounts()
    {
        PerformInternalSettingsChange(() =>
        {
            _SaveAccountsInternal();
        });
    }

    private void _SaveAccountsInternal()
    {
        Dictionary<string, AccountData> accs = new(StringComparer.OrdinalIgnoreCase);
        foreach (IAccountItemViewModel account in Accounts)
        {
            accs[account.Username] = new AccountData
            {
                DisplayName = account.DisplayName,
                Password = account.Password,
                Tags = account.Tags.ToList(),
                UseCheck = account.UseCheck
            };
        }

        _settingsService.Set("ManagedAccounts", accs);
    }

    private void _GetSavedAccounts()
    {
        Dictionary<string, AccountData>? accs = _settingsService.Get<Dictionary<string, AccountData>>("ManagedAccounts");
        if (accs is null)
            return;

        // Remember which accounts are currently checked in UI
        HashSet<string> currentlyChecked = Accounts.Where(a => a.UseCheck).Select(a => a.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove accounts that are no longer in saved settings
        for (int i = Accounts.Count - 1; i >= 0; i--)
        {
            if (!accs.ContainsKey(Accounts[i].Username))
            {
                Accounts.RemoveAt(i);
            }
        }

        // Add or update accounts in-place
        foreach (KeyValuePair<string, AccountData> kvp in accs)
        {
            string password = kvp.Value.Password ?? string.Empty;
            if (password.StartsWith("enc:"))
                password = Skua.Core.Utils.CryptoUtils.Decrypt(password);

            IAccountItemViewModel? existing = Accounts.FirstOrDefault(a => a.Username.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.DisplayName = kvp.Value.DisplayName;
                existing.Password = password;

                if (kvp.Value.Tags != null)
                {
                    if (!existing.Tags.SequenceEqual(kvp.Value.Tags))
                    {
                        existing.Tags.Clear();
                        foreach (string tag in kvp.Value.Tags)
                            existing.Tags.Add(tag);
                    }
                }
                else
                {
                    existing.Tags.Clear();
                }
            }
            else
            {
                AccountItemViewModel accountVm = new()
                {
                    Username = kvp.Key,
                    DisplayName = kvp.Value.DisplayName,
                    Password = password,
                    UseCheck = kvp.Value.UseCheck || currentlyChecked.Contains(kvp.Key)
                };
                if (kvp.Value.Tags != null)
                {
                    foreach (string tag in kvp.Value.Tags)
                        accountVm.Tags.Add(tag);
                }
                Accounts.Add(accountVm);
            }
        }

        ReindexAccounts();
        ApplyTagFilter();
        SelectedAccountQuant = Accounts.Count(a => a.UseCheck);
    }

    private void _SaveGroups()
    {
        PerformInternalSettingsChange(() =>
        {
            List<GroupData> groupsData = Groups.Select(g => new GroupData
            {
                Name = g.Name,
                Accounts = g.Accounts.Select<IAccountItemViewModel, string>(a => a.Username).ToList()
            }).ToList();

            _settingsService.Set("AccountGroups", groupsData);
            _SaveAccountsInternal();
        });
    }

    private void _GetSavedGroups()
    {
        try
        {
            List<GroupData>? groupsData = _settingsService.Get<List<GroupData>>("AccountGroups");

            if (groupsData == null || groupsData.Count == 0)
            {
                Groups.Clear();
                return;
            }

            List<GroupItemViewModel> loadedGroups = new();

            foreach (GroupData groupData in groupsData)
            {
                try
                {
                    if (string.IsNullOrEmpty(groupData.Name))
                        continue;

                    GroupItemViewModel group = new(groupData.Name);

                    foreach (string username in groupData.Accounts)
                    {
                        if (!string.IsNullOrEmpty(username))
                        {
                            IAccountItemViewModel? account = Accounts.FirstOrDefault(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                            if (account != null && !group.Accounts.Contains(account))
                            {
                                group.Accounts.Add(account);
                            }
                        }
                    }

                    loadedGroups.Add(group);
                }
                catch
                {
                    continue;
                }
            }

            Groups.ReplaceRange(loadedGroups);
        }
        catch
        {
            Groups.Clear();
        }
    }

    private async Task _GetServers()
    {
        try
        {
            string response = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GetGHClient()
, $"http://content.aq.com/game/api/data/servers");

            List<Server>? servers = JsonConvert.DeserializeObject<List<Server>>(response);
            if (servers == null || servers.Count == 0)
                return;

            _cachedServers = servers;

            // Measure pings before displaying servers
            await _MeasureServerPings(_cachedServers);

            ServerList.AddRange(_cachedServers);

            string lastServer = _settingsService.Get<string>("LastServer");
            Server? serverToSelect = null;
            if (!string.IsNullOrEmpty(lastServer))
                serverToSelect = Enumerable.FirstOrDefault<Server>(ServerList, s => s.Name == lastServer);
            SelectedServer = serverToSelect ?? ServerList[0];
        }
        catch
        {
        }
    }

    private async Task _MeasureServerPings(List<Server> servers)
    {
        const int TimeoutMs = 2500;
        const int SampleCount = 3;
        const long FailedPing = 9999;
        byte[] versionCheck = System.Text.Encoding.UTF8.GetBytes("<msg t='sys'><body action='verChk' r='0'><ver v='165' /></body></msg>\\0");

        List<Task> pingTasks = servers.Select(server => Task.Run(async () =>
        {
            if (!server.Online || string.IsNullOrWhiteSpace(server.IP) || server.Port <= 0 || server.Port > 65535)
            {
                server.Ping = FailedPing;
                return;
            }

            List<double> samples = new(SampleCount);

            for (int sample = 0; sample < SampleCount; sample++)
            {
                try
                {
                    using System.Net.Sockets.TcpClient client = new();
                    await client.ConnectAsync(server.IP, server.Port).WaitAsync(TimeSpan.FromMilliseconds(TimeoutMs));

                    using System.Net.Sockets.NetworkStream stream = client.GetStream();
                    await stream.WriteAsync(versionCheck.AsMemory());
                    await stream.FlushAsync();

                    // Start timing after the version check is sent; TCP connect time alone can be misleading.
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    using CancellationTokenSource timeout = new(TimeoutMs);
                    byte[] buffer = new byte[1];
                    System.Text.StringBuilder response = new();

                    while (!timeout.IsCancellationRequested)
                    {
                        int bytesRead = await stream.ReadAsync(buffer.AsMemory(), timeout.Token);
                        if (bytesRead == 0)
                            break;

                        if (buffer[0] != 0)
                        {
                            response.Append((char)buffer[0]);
                            continue;
                        }

                        string message = response.ToString();
                        response.Clear();

                        if (message.Contains("action='apiOK'", StringComparison.OrdinalIgnoreCase)
                            || message.Contains("action=\\"apiOK\\"", StringComparison.OrdinalIgnoreCase))
                        {
                            stopwatch.Stop();
                            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
                            break;
                        }

                        if (message.Contains("action='apiKO'", StringComparison.OrdinalIgnoreCase)
                            || message.Contains("action=\\"apiKO\\"", StringComparison.OrdinalIgnoreCase))
                            break;
                    }
                }
                catch
                {
                    // A failed connection or missing protocol response is not a valid latency sample.
                }
            }

            if (samples.Count == 0)
            {
                server.Ping = FailedPing;
                return;
            }

            samples.Sort();
            double median = samples[samples.Count / 2];
            server.Ping = (long)Math.Round(median, MidpointRounding.AwayFromZero);
        })).ToList();

        await Task.WhenAll(pingTasks);
    }

    // Script Commands
    /// <summary>
    /// Opens the selected script's shared options editor in the Manager.
    /// </summary>
    [RelayCommand]
    public async Task EditScriptOptions()
    {
        string scriptPath = ScriptPath;
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            _dialogService.ShowMessageBox("Please select a script first.", "No Script Selected");
            return;
        }

        if (!File.Exists(scriptPath))
        {
            _dialogService.ShowMessageBox($"Script file not found:\n{scriptPath}", "Script Not Found");
            return;
        }

        try
        {
            _scriptManager.OverrideStorage = null;
            _scriptManager.SetLoadedScript(scriptPath);
            object? compiled = await Task.Run(() => _scriptManager.Compile(File.ReadAllText(scriptPath)));
            if (compiled == null)
            {
                _dialogService.ShowMessageBox("Failed to compile script for configuration.", "Script Error");
                return;
            }

            _scriptManager.LoadScriptConfig(compiled);
            IScriptOptionContainer? config = _scriptManager.Config;
            if (config != null && (config.Options.Count > 0 || config.MultipleOptions.Count > 0))
            {
                config.Configure();
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

    [RelayCommand]
    public void ChangeScriptPath()
    {
        string? folderPath = _fileService.OpenFile(ClientFileSources.SkuaScriptsDIR, "Skua Scripts (*.cs)|*.cs");

        if (!string.IsNullOrEmpty(folderPath))
            ScriptPath = folderPath;
    }

    [RelayCommand]
    public void OpenGetScripts()
    {
        IServiceProvider? services = Ioc.Default.GetService<IServiceProvider>();
        IWindowService? windowService = Ioc.Default.GetService<IWindowService>();
        if (services != null)
        {
            windowService.RegisterManagedWindow("Script Repo", services.GetRequiredService<ScriptRepoViewModel>());
            windowService.RegisterManagedWindow("Application Themes", services.GetRequiredService<ApplicationThemesViewModel>());
        }

        windowService?.ShowManagedWindow("Script Repo");
    }

    private void HandleLoadScript(LoadScriptMessage message)
    {
        if (string.IsNullOrEmpty(message.Path))
            return;

        var armyQueue = Ioc.Default.GetService<ArmyQueueViewModel>();
        if (armyQueue != null && armyQueue.HasPendingScriptTarget)
            return;

        ScriptPath = message.Path;

        _dialogService.ShowMessageBox($"Script loaded: {Path.GetFileName(message.Path)}", "Script Loaded");
    }

    private void ReindexAccounts()
    {
        for (int i = 0; i < Accounts.Count; i++)
        {
            Accounts[i].AccountNumber = i + 1;
        }
    }

    private void RefreshAccountDisplayNames()
    {
        foreach (IAccountItemViewModel account in Accounts)
        {
            account.RefreshDisplayName();
        }
    }
}
