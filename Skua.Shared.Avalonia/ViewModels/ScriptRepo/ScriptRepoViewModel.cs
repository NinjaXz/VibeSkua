using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Models.GitHub;
using Skua.Core.Models.Scripts;
using Skua.Core.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.Shared.Avalonia.ViewModels.ScriptRepo;

public partial class ScriptRepoViewModel : BotControlViewModelBase
{
    public ScriptRepoViewModel(
        IGetScriptsService getScripts,
        IProcessService processService,
        ICustomScriptService customScripts)
        : base("Search Scripts", 969, 500)
    {
        _getScriptsService = getScripts;
        _processService = processService;
        _customScripts = customScripts;
        OpenScriptFolderCommand = new RelayCommand(_processService.OpenVSC);
    }

    private bool _isLoadingInitial;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);

    protected override void OnActivated()
    {
        _getScriptsService.PropertyChanged += GetScriptsService_PropertyChanged;

        if (IsBusy || IsAnyTaskRunning())
            return;

        if (_scripts.Count == 0 || _getScriptsService.Scripts.Count == 0)
        {
            _ = LoadInitialScripts();
        }
        else
        {
            _ = RefreshScriptsList();
        }
    }

    private async Task LoadInitialScripts()
    {
        if (_isLoadingInitial) return;
        _isLoadingInitial = true;
        IsLoadingScripts = true;
        IsBusy = true;
        try
        {
            Progress<string> progress = new(ProgressHandler);
            await _getScriptsService.GetScriptsAsync(progress, default);
        }
        catch { }
        finally
        {
            IsBusy = false;
            _isLoadingInitial = false;
        }
        await RefreshScriptsList();
    }

    protected override void OnDeactivated()
    {
        _getScriptsService.PropertyChanged -= GetScriptsService_PropertyChanged;
    }

    private void GetScriptsService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IGetScriptsService.Scripts) && !_isLoadingInitial)
        {
            _ = RefreshScriptsList();
        }
    }

    private readonly IGetScriptsService _getScriptsService;
    private readonly IProcessService _processService;
    private readonly ICustomScriptService _customScripts;

    [ObservableProperty]
    private bool _isManagerMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadedQuantity), nameof(OutdatedQuantity), nameof(ScriptQuantity), nameof(BotScriptQuantity), nameof(PinnedQuantity))]
    private RangedObservableCollection<ScriptInfoViewModel> _scripts = new();

    [ObservableProperty]
    private RangedObservableCollection<ScriptInfoViewModel> _filteredScripts = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadedQuantity), nameof(OutdatedQuantity), nameof(ScriptQuantity), nameof(BotScriptQuantity), nameof(PinnedQuantity))]
    private ScriptInfoViewModel? _selectedItem;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _progressReportMessage = string.Empty;

    // True while the list is fetched or rebuilt, so an empty list shows "Loading" instead of "No scripts found"
    [ObservableProperty]
    private bool _isLoadingScripts;

    [ObservableProperty]
    private string _sortBy = SortByName;

    [ObservableProperty]
    private bool _sortDescending = false;

    [ObservableProperty]
    private string _filterBy = "All";

    // Which part of a script the search looks in (Skua 1.4.5's scopes)
    public List<string> SearchScopes { get; } = new() { "All", "Name", "Tag", "Description", "File" };

    [ObservableProperty]
    private string _searchScope = "All";

    partial void OnSearchScopeChanged(string value) => ApplySearchFilter();

    public List<string> FilterOptions { get; } = new()
    {
        "All", "Pinned", "Army", "Classes", "Dailies", "Evil", "Farm", "Good", "Legion", "Local", "Nation", "Other", "Rep", "Seasonal", "Story", "Ultras"
    };

    public int DownloadedQuantity => _scripts.Count(s => s.Downloaded);
    public int OutdatedQuantity => _scripts.Count(s => s.Outdated);
    public int ScriptQuantity => _scripts.Count;
    public int BotScriptQuantity => _scripts.Count;
    public int PinnedQuantity => _scripts.Count(s => s.IsPinned);
    public bool ShowStartButton => !IsManagerMode;
    public IRelayCommand OpenScriptFolderCommand { get; }

    private const string SortByName = "Name";
    private const string SortByDate = "Date Created";

    private bool IsSortedByDate => SortBy == SortByDate;

    public string SortDirectionText => IsSortedByDate
        ? (SortDescending ? "Newest first" : "Oldest first")
        : (SortDescending ? "Name Z to A" : "Name A to Z");
    public string SortDirectionIcon => IsSortedByDate
        ? "ClockOutline"
        : (SortDescending ? "SortDescending" : "SortAscending");

    // The button's lit state: off for A to Z and Newest, on for their reverses. It alternates on every click, as a
    // ToggleButton does by itself, so the two stay in step through the four orders.
    public bool IsSortReversed => IsSortedByDate ? !SortDescending : SortDescending;

    // A to Z, Z to A, Newest, Oldest, and round again. Each step changes one property, so each sorts once.
    [RelayCommand]
    private void CycleSort()
    {
        if (IsSortedByDate)
        {
            if (SortDescending)
                SortDescending = false;
            else
                SortBy = SortByName;
        }
        else if (SortDescending)
        {
            SortBy = SortByDate;
        }
        else
        {
            SortDescending = true;
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        SearchScope = "All";
        FilterBy = "All";
    }

    private CancellationTokenSource? _searchCts;

    partial void OnIsManagerModeChanged(bool value) => OnPropertyChanged(nameof(ShowStartButton));
    
    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        CancellationToken token = _searchCts.Token;

        _ = Task.Run(async () =>
        {
            await Task.Delay(100, token);
            if (token.IsCancellationRequested) return;

            List<ScriptInfoViewModel> matches = FindMatches(value, SearchScope);

            if (token.IsCancellationRequested) return;

            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                FilteredScripts.ReplaceRange(matches);
                if (SelectedItem != null && !matches.Contains(SelectedItem))
                    SelectedItem = null;
            });
        }, token);
    }

    partial void OnSortByChanged(string value)
    {
        OnSortOrderChanged();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        OnSortOrderChanged();
    }

    private void OnSortOrderChanged()
    {
        OnPropertyChanged(nameof(SortDirectionText));
        OnPropertyChanged(nameof(SortDirectionIcon));
        OnPropertyChanged(nameof(IsSortReversed));
        ApplySort();
    }

    partial void OnFilterByChanged(string value)
    {
        _ = RefreshScriptsList();
    }

    private void ApplySort()
    {
        if (_scripts.Count == 0) return;

        List<ScriptInfoViewModel> sorted = _scripts.ToList();
        sorted.Sort(CompareScripts);

        _scripts.ReplaceRange(sorted);
        ApplySearchFilter();
    }

    private int CompareScripts(ScriptInfoViewModel a, ScriptInfoViewModel b)
    {
        if (a.IsPinned != b.IsPinned)
            return a.IsPinned ? -1 : 1;

        if (IsSortedByDate)
        {
            // Scripts without a date go last either way; scripts from the same commit fall back to the name
            if (a.HasCreationDate != b.HasCreationDate)
                return a.HasCreationDate ? -1 : 1;

            // ScriptDates.json mixes UTC and offset dates, which load as different kinds
            int dateCmp = a.HasCreationDate
                ? a.Info.CreationDate!.Value.ToUniversalTime().CompareTo(b.Info.CreationDate!.Value.ToUniversalTime())
                : 0;
            if (dateCmp != 0)
                return SortDescending ? -dateCmp : dateCmp;

            return string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
        }

        int cmp = string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
        return SortDescending ? -cmp : cmp;
    }

    [RelayCommand]
    private async Task TogglePin(ScriptInfoViewModel? script = null)
    {
        ScriptInfoViewModel? target = script ?? SelectedItem;
        if (target is null)
            return;

        target.IsPinned = !target.IsPinned;

        ISettingsService? settingsService = Ioc.Default.GetService<ISettingsService>();
        if (settingsService != null)
        {
            StringCollection? pinnedList = settingsService.Get<StringCollection>("UserPinnedScripts")
                                           ?? settingsService.GetShared()?.UserPinnedScripts
                                           ?? new StringCollection();

            string key = target.ScriptKey;
            for (int i = pinnedList.Count - 1; i >= 0; i--)
            {
                if (string.Equals(pinnedList[i]?.Replace('\\', '/').Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    pinnedList.RemoveAt(i);
                }
            }

            if (target.IsPinned)
            {
                pinnedList.Add(key);
            }

            settingsService.Set("UserPinnedScripts", pinnedList);
        }

        OnPropertyChanged(nameof(PinnedQuantity));

        if (string.Equals(FilterBy, "Pinned", StringComparison.OrdinalIgnoreCase))
        {
            await RefreshScriptsList();
        }
        else
        {
            ApplySort();
        }
    }

    [RelayCommand]
    private async Task AddCustomFolder()
    {
        IFileDialogService? fileDialog = Ioc.Default.GetService<IFileDialogService>();
        if (fileDialog == null)
            return;

        string? folder = await fileDialog.OpenFolderAsync(ClientFileSources.SkuaScriptsDIR);
        if (!string.IsNullOrEmpty(folder))
        {
            ISettingsService? settings = Ioc.Default.GetService<ISettingsService>();
            settings?.Set("UserCustomScriptsFolder", folder);
            _customScripts.SetCustomFolder(folder);
            FilterBy = "Local";
            await RefreshScriptsList();
        }
    }

    [RelayCommand]
    private async Task ClearCustomFolder()
    {
        ISettingsService? settings = Ioc.Default.GetService<ISettingsService>();
        settings?.Set("UserCustomScriptsFolder", string.Empty);
        _customScripts.SetCustomFolder(string.Empty);
        await RefreshScriptsList();
    }

    [RelayCommand]
    private async Task LoadLocalScript()
    {
        IFileDialogService? fileDialog = Ioc.Default.GetService<IFileDialogService>();
        if (fileDialog == null)
            return;

        string? path = fileDialog.OpenFile(ClientFileSources.SkuaScriptsDIR, "Skua Scripts (*.cs)|*.cs");
        if (!string.IsNullOrEmpty(path))
        {
            ISettingsService? settings = Ioc.Default.GetService<ISettingsService>();
            if (settings != null)
            {
                StringCollection? list = settings.Get<StringCollection>("UserCustomScriptsList") ?? settings.GetShared()?.UserCustomScriptsList ?? new StringCollection();
                if (!list.Contains(path))
                {
                    list.Add(path);
                    settings.Set("UserCustomScriptsList", list);
                    _customScripts.AddCustomFile(path);
                    FilterBy = "Local";
                    await RefreshScriptsList();
                }
            }

            StrongReferenceMessenger.Default.Send<LoadScriptMessage, int>(new(path), (int)MessageChannels.ScriptStatus);
        }
    }

    [RelayCommand]
    private void OpenScript(ScriptInfoViewModel? script = null)
    {
        ScriptInfoViewModel? target = script ?? SelectedItem;
        if (target is null || !target.Downloaded)
            return;

        _processService.OpenVSC(target.LocalFile);
    }

    [RelayCommand]
    private async Task RefreshScripts(CancellationToken token)
    {
        IsBusy = true;
        try
        {
            await Task.Run(async () =>
            {
                Progress<string> progress = new(ProgressHandler);
                await _getScriptsService.RefreshScriptsAsync(progress, token);
            }, token);
        }
        catch (OperationCanceledException)
        {
            ProgressReportMessage = "Refresh cancelled.";
        }
        catch (Exception ex)
        {
            ProgressReportMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        await RefreshScriptsList();
    }

    private CancellationTokenSource? _updateDatesCts;

    private bool IsAnyTaskRunning()
    {
        return (_updateDatesCts != null && !_updateDatesCts.IsCancellationRequested) ||
               UpdateDatesCommand.IsRunning ||
               RefreshScriptsCommand.IsRunning ||
               DownloadAllCommand.IsRunning ||
               UpdateAllCommand.IsRunning;
    }

    [RelayCommand]
    private async Task UpdateDates(CancellationToken token)
    {
        if (IsBusy)
            return;

        _updateDatesCts?.Cancel();
        _updateDatesCts?.Dispose();
        _updateDatesCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var linkedToken = _updateDatesCts.Token;

        IsBusy = true;
        try
        {
            Progress<string> progress = new(s => ProgressReportMessage = s);
            await Task.Run(async () =>
            {
                await _getScriptsService.UpdateScriptDatesAsync(progress, linkedToken);
            }, linkedToken);
        }
        catch (OperationCanceledException)
        {
            ProgressReportMessage = "Update dates cancelled.";
        }
        catch (Exception ex)
        {
            ProgressReportMessage = $"Error: {ex.Message}";
        }
        finally
        {
            _updateDatesCts?.Dispose();
            _updateDatesCts = null;
            IsBusy = false;
        }
        await RefreshScriptsList();
    }

    private async Task RefreshScriptsList()
    {
        if (!await _refreshSemaphore.WaitAsync(0))
            return;

        IsLoadingScripts = true;
        try
        {
            List<ScriptInfoViewModel> scriptViewModels = await Task.Run(() =>
            {
                ISettingsService? settingsService = Ioc.Default.GetService<ISettingsService>();
                settingsService?.ReloadSettings();

                StringCollection? pinnedList = settingsService?.Get<StringCollection>("UserPinnedScripts")
                                               ?? settingsService?.GetShared()?.UserPinnedScripts;
                HashSet<string> pinnedSet = new(
                    pinnedList?.Cast<string>().Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Replace('\\', '/').Trim()) ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase
                );

                // 1. Single-pass fast dictionary of existing local files
                Dictionary<string, long> existingLocalFiles = new(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (Directory.Exists(ClientFileSources.SkuaScriptsDIR))
                    {
                        foreach (string filePath in Directory.EnumerateFiles(ClientFileSources.SkuaScriptsDIR, "*.cs", SearchOption.AllDirectories))
                        {
                            try
                            {
                                existingLocalFiles[Path.GetFullPath(filePath)] = new FileInfo(filePath).Length;
                            }
                            catch
                            {
                                existingLocalFiles[Path.GetFullPath(filePath)] = 0;
                            }
                        }
                    }
                }
                catch { }

                List<ScriptInfoViewModel> viewModels = new();
                string filter = FilterBy;
                bool isAllFilter = string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase);
                bool isPinnedFilter = string.Equals(filter, "Pinned", StringComparison.OrdinalIgnoreCase);
                bool isLocalFilter = string.Equals(filter, "Local", StringComparison.OrdinalIgnoreCase);

                // 1. Repository scripts (only if NOT filtering exclusively by "Local")
                if (!isLocalFilter && _getScriptsService?.Scripts != null)
                {
                    foreach (ScriptInfo script in _getScriptsService.Scripts)
                    {
                        string repoKey = script.FilePath.Replace('\\', '/').Trim();
                        bool isPinned = pinnedSet.Contains(repoKey) || pinnedSet.Contains(script.LocalFile.Replace('\\', '/').Trim());

                        if (isPinnedFilter)
                        {
                            if (!isPinned) continue;
                        }
                        else if (!isAllFilter)
                        {
                            bool matches = false;
                            string[] parts = script.FilePath.Split('/', '\\');
                            for (int i = 0; i < parts.Length; i++)
                            {
                                if (parts[i].StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                                {
                                    matches = true;
                                    break;
                                }
                            }
                            if (!matches) continue;
                        }

                        if (string.IsNullOrWhiteSpace(script.Name) || script.Name.Equals("null", StringComparison.OrdinalIgnoreCase))
                            script.Name = !string.IsNullOrWhiteSpace(script.FileName) && !script.FileName.Equals("null", StringComparison.OrdinalIgnoreCase)
                                ? Path.GetFileNameWithoutExtension(script.FileName)
                                : Path.GetFileNameWithoutExtension(script.FilePath);

                        if (string.IsNullOrWhiteSpace(script.Description) || script.Description.Equals("null", StringComparison.OrdinalIgnoreCase))
                            script.Description = "No description provided.";

                        if (script.Tags == null || script.Tags.Length == 0 || (script.Tags.Length == 1 && script.Tags[0] == "null"))
                            script.Tags = new[] { "no-tags" };

                        string localPath = Path.GetFullPath(script.LocalFile);
                        bool downloaded = existingLocalFiles.TryGetValue(localPath, out long size) || (existingLocalFiles.Count == 0 && File.Exists(localPath));
                        int localSize = downloaded ? (int)(size > 0 ? size : (File.Exists(localPath) ? new FileInfo(localPath).Length : 0)) : 0;
                        script.CachedDownloaded = downloaded;
                        script.CachedLocalSize = localSize;

                        viewModels.Add(new ScriptInfoViewModel(script, null, false, downloaded, localSize, isPinned));
                    }
                }

                // 2. Custom Folders and Custom Scripts (shown when FilterBy is "All", "Local", or "Pinned")
                if (isAllFilter || isLocalFilter || isPinnedFilter)
                {
                    try
                    {
                        string? localFolder = settingsService?.Get<string>("UserCustomScriptsFolder");
                        if (string.IsNullOrWhiteSpace(localFolder))
                            localFolder = settingsService?.GetShared()?.UserCustomScriptsFolder;
                        if (string.IsNullOrWhiteSpace(localFolder))
                            localFolder = settingsService?.GetClient()?.UserCustomScriptsFolder;
                        if (string.IsNullOrWhiteSpace(localFolder))
                            localFolder = settingsService?.GetManager()?.UserCustomScriptsFolder;
                        if (string.IsNullOrWhiteSpace(localFolder) && _customScripts != null)
                        {
                            var roots = _customScripts.GetSearchRoots();
                            if (roots != null && roots.Count > 0)
                                localFolder = roots[0];
                        }

                        if (!string.IsNullOrEmpty(localFolder) && Directory.Exists(localFolder))
                        {
                            foreach (string filePath in Directory.EnumerateFiles(localFolder, "*.cs", SearchOption.AllDirectories))
                            {
                                string customKey = Path.GetFullPath(filePath).Replace('\\', '/').Trim();
                                bool isPinned = pinnedSet.Contains(customKey);
                                if (isPinnedFilter && !isPinned) continue;

                                FileInfo file = new(filePath);
                                ScriptInfo info = new()
                                {
                                    Name = Path.GetFileNameWithoutExtension(file.Name),
                                    FileName = file.Name,
                                    FilePath = file.FullName,
                                    Description = "Local Custom Script",
                                    Tags = new[] { "Local" },
                                    CreationDate = file.CreationTime,
                                    Size = (int)file.Length,
                                    Sha256 = null,
                                    CachedDownloaded = true,
                                    CachedLocalSize = (int)file.Length
                                };
                                viewModels.Add(new ScriptInfoViewModel(info, file.FullName, isCustom: true, isDownloaded: true, localSize: (int)file.Length, isPinned: isPinned));
                            }
                        }

                        StringCollection? scriptList = settingsService?.Get<StringCollection>("UserCustomScriptsList");
                        if (scriptList == null || scriptList.Count == 0)
                            scriptList = settingsService?.GetShared()?.UserCustomScriptsList;
                        if (scriptList == null || scriptList.Count == 0)
                            scriptList = settingsService?.GetClient()?.UserCustomScriptsList;
                        if (scriptList == null || scriptList.Count == 0)
                            scriptList = settingsService?.GetManager()?.UserCustomScriptsList;

                        if (scriptList != null && scriptList.Count > 0)
                        {
                            HashSet<string> seenPaths = new(viewModels.Select(v => v.LocalFile), StringComparer.OrdinalIgnoreCase);
                            foreach (string? filePath in scriptList)
                            {
                                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) continue;
                                if (seenPaths.Contains(filePath)) continue;

                                string singleKey = filePath.Replace('\\', '/').Trim();
                                bool isPinned = pinnedSet.Contains(singleKey);
                                if (isPinnedFilter && !isPinned) continue;

                                FileInfo file = new(filePath);
                                ScriptInfo info = new()
                                {
                                    Name = Path.GetFileNameWithoutExtension(file.Name),
                                    FileName = file.Name,
                                    FilePath = file.FullName,
                                    Description = "Local Single Script",
                                    Tags = new[] { "Local", "Single" },
                                    CreationDate = file.CreationTime,
                                    Size = (int)file.Length,
                                    Sha256 = null,
                                    CachedDownloaded = true,
                                    CachedLocalSize = (int)file.Length
                                };
                                seenPaths.Add(filePath);
                                viewModels.Add(new ScriptInfoViewModel(info, filePath, isCustom: true, isDownloaded: true, localSize: (int)file.Length, isPinned: isPinned));
                            }
                        }
                    }
                    catch { }
                }

                // 3. Fast in-place sorting
                viewModels.Sort(CompareScripts);

                return viewModels;
            });

            _scripts.ReplaceRange(scriptViewModels);

            ApplySearchFilter();
            OnPropertyChanged(nameof(Scripts));
            OnPropertyChanged(nameof(DownloadedQuantity));
            OnPropertyChanged(nameof(OutdatedQuantity));
            OnPropertyChanged(nameof(ScriptQuantity));
            OnPropertyChanged(nameof(BotScriptQuantity));
            OnPropertyChanged(nameof(PinnedQuantity));
            if (_updateDatesCts == null || _updateDatesCts.IsCancellationRequested)
                IsBusy = false;
        }
        finally
        {
            // A refresh during the initial fetch (filter change after Cancel) must not end the loading state
            IsLoadingScripts = _isLoadingInitial;
            _refreshSemaphore.Release();
        }
    }

    public void ProgressHandler(string message)
    {
        ProgressReportMessage = message;
    }

    [RelayCommand]
    private async Task Delete(ScriptInfoViewModel? script = null)
    {
        ScriptInfoViewModel? target = script ?? SelectedItem;
        if (target is null)
            return;

        IsBusy = true;

        if (target.Info.Tags.Contains("Local"))
        {
            IDialogService? dialogService = Ioc.Default.GetService<IDialogService>();

            if (target.Info.Tags.Contains("Single"))
            {
                DialogResult? result = dialogService?.ShowMessageBox(
                    $"Do you want to permanently delete {target.FileName} from your computer, or just remove it from the Custom Scripts list?",
                    "Remove Custom Script", "Cancel", "Remove from List", "Delete File");

                if (result == null || result.Text == "Cancel" || string.IsNullOrEmpty(result.Text))
                {
                    IsBusy = false;
                    return;
                }

                if (result.Text == "Delete File")
                {
                    await _getScriptsService.DeleteScriptAsync(target.Info);
                }

                ISettingsService? settings = Ioc.Default.GetService<ISettingsService>();
                StringCollection? list = settings?.Get<StringCollection>("UserCustomScriptsList");
                if (list != null && list.Contains(target.Info.FilePath))
                {
                    list.Remove(target.Info.FilePath);
                    settings?.Set("UserCustomScriptsList", list);
                }
            }
            else
            {
                DialogResult? result = dialogService?.ShowMessageBox(
                    $"This script is inside your Custom Scripts Folder.\r\nDo you want to permanently delete {target.FileName} from your computer?",
                    "Delete Local Script", "No", "Yes");

                if (result != null && result.Text == "Yes")
                {
                    await _getScriptsService.DeleteScriptAsync(target.Info);
                }
                else
                {
                    IsBusy = false;
                    return;
                }
            }

            target.Downloaded = false;
            await RefreshScriptsList();
            IsBusy = false;
            return;
        }

        IDialogService? repoDialog = Ioc.Default.GetService<IDialogService>();
        if (repoDialog != null)
        {
            DialogResult? result = repoDialog.ShowMessageBox(
                $"Are you sure you want to delete {target.FileName} from your downloaded scripts?",
                "Delete Script", "No", "Yes");

            if (result == null || result.Text != "Yes")
            {
                IsBusy = false;
                return;
            }
        }

        ProgressReportMessage = $"Deleting {target.FileName}.";
        await _getScriptsService.DeleteScriptAsync(target.Info);
        ProgressReportMessage = $"Deleted {target.FileName}.";
        target.Downloaded = false;
        target.LocalSize = 0;
        target.Info.CachedDownloaded = false;
        target.Info.CachedLocalSize = 0;
        OnPropertyChanged(nameof(DownloadedQuantity));
        OnPropertyChanged(nameof(OutdatedQuantity));
        OnPropertyChanged(nameof(ScriptQuantity));
        OnPropertyChanged(nameof(BotScriptQuantity));
        IsBusy = false;
    }

    [RelayCommand]
    private async Task Download(ScriptInfoViewModel? script = null, CancellationToken token = default)
    {
        ScriptInfoViewModel? target = script ?? SelectedItem;
        if (target is null)
            return;

        IsBusy = true;
        try
        {
            ProgressReportMessage = $"Downloading {target.FileName}...";
            await _getScriptsService.DownloadScriptAsync(target.Info, token);
            ProgressReportMessage = $"Downloaded {target.FileName}.";
            int localSize = File.Exists(target.LocalFile) ? (int)new FileInfo(target.LocalFile).Length : target.Info.Size;
            await target.RefreshDownloadStateAsync(true, localSize);
        }
        catch (OperationCanceledException)
        {
            ProgressReportMessage = "Download cancelled.";
        }
        catch (Exception ex)
        {
            ProgressReportMessage = $"Failed to download {target.FileName}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        OnPropertyChanged(nameof(DownloadedQuantity));
        OnPropertyChanged(nameof(OutdatedQuantity));
        OnPropertyChanged(nameof(ScriptQuantity));
        OnPropertyChanged(nameof(BotScriptQuantity));
    }

    [RelayCommand]
    private async Task UpdateAll(CancellationToken token)
    {
        IsBusy = true;
        try
        {
            IProgress<string> progress = new Progress<string>(ProgressHandler);
            progress.Report("Checking for script updates...");
            await _getScriptsService.RefreshScriptsAsync(progress, token);

            progress.Report("Downloading updated scripts...");
            int count = await _getScriptsService.DownloadAllWhereAsync(s => s.Outdated, progress, token);
            ProgressReportMessage = count > 0 ? $"Updated {count} scripts." : "All scripts are up to date.";
        }
        catch (OperationCanceledException)
        {
            ProgressReportMessage = "Update cancelled.";
        }
        catch (Exception ex)
        {
            ProgressReportMessage = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        await RefreshScriptsList();
    }

    [RelayCommand]
    private async Task DownloadAll(CancellationToken token)
    {
        IsBusy = true;
        try
        {
            IProgress<string> progress = new Progress<string>(ProgressHandler);
            progress.Report("Syncing script catalog...");
            await _getScriptsService.RefreshScriptsAsync(progress, token);

            progress.Report("Downloading scripts...");
            int count = await _getScriptsService.DownloadAllWhereAsync(s => !s.Downloaded || s.Outdated, progress, token);
            ProgressReportMessage = count > 0 ? $"Downloaded {count} scripts." : "All scripts are already downloaded.";
        }
        catch (OperationCanceledException)
        {
            ProgressReportMessage = "Download cancelled.";
        }
        catch (Exception ex)
        {
            ProgressReportMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        await RefreshScriptsList();
    }

    [RelayCommand]
    public void CancelTask()
    {
        bool wasRunning = false;

        if (_updateDatesCts != null)
        {
            _updateDatesCts.Cancel();
            wasRunning = true;
        }

        if (UpdateDatesCommand.IsRunning)
        {
            UpdateDatesCommand.Cancel();
            wasRunning = true;
        }

        if (RefreshScriptsCommand.IsRunning)
        {
            RefreshScriptsCommand.Cancel();
            wasRunning = true;
        }

        if (DownloadAllCommand.IsRunning)
        {
            DownloadAllCommand.Cancel();
            wasRunning = true;
        }

        if (UpdateAllCommand.IsRunning)
        {
            UpdateAllCommand.Cancel();
            wasRunning = true;
        }

        if (DownloadCommand.IsRunning)
        {
            DownloadCommand.Cancel();
            wasRunning = true;
        }

        if (DeleteCommand.IsRunning)
        {
            DeleteCommand.Cancel();
            wasRunning = true;
        }

        if (wasRunning)
            ProgressReportMessage = "Task cancelled.";
        else
            ProgressReportMessage = string.Empty;

        IsBusy = false;
    }

    // Matches in rank order. OrderBy is stable, so within a rank the list keeps its order: pinned first, then the
    // chosen sort.
    private List<ScriptInfoViewModel> FindMatches(string? text, string scope)
    {
        string query = (text ?? string.Empty).Trim();
        if (query.Length == 0)
            return _scripts.ToList();

        return _scripts.Select(s => (Script: s, Rank: s.SearchRank(query, scope)))
            .Where(m => m.Rank.HasValue)
            .OrderBy(m => m.Rank)
            .Select(m => m.Script)
            .ToList();
    }

    private void ApplySearchFilter()
    {
        List<ScriptInfoViewModel> matches = FindMatches(SearchText, SearchScope);

        FilteredScripts.ReplaceRange(matches);

        if (SelectedItem is not null && !matches.Contains(SelectedItem))
            SelectedItem = null;
    }
}
