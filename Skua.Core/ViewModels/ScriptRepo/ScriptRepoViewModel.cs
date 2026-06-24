using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models.GitHub;
using Skua.Core.Models;
using Skua.Core.Utils;

namespace Skua.Core.ViewModels;

public partial class ScriptRepoViewModel : BotControlViewModelBase
{
    public ScriptRepoViewModel(IGetScriptsService getScripts, IProcessService processService)
        : base("Search Scripts", 969, 500)
    {
        _getScriptsService = getScripts;
        _processService = processService;
        OpenScriptFolderCommand = new RelayCommand(_processService.OpenVSC);
    }

    protected override void OnActivated()
    {
        _getScriptsService.PropertyChanged += GetScriptsService_PropertyChanged;
        
        if (_scripts.Count == 0 || _getScriptsService.Scripts.Count == 0)
        {
            _ = RefreshScripts(default);
        }
        else
        {
            _ = RefreshScriptsList();
        }
    }

    protected override void OnDeactivated()
    {
        _getScriptsService.PropertyChanged -= GetScriptsService_PropertyChanged;
    }

    private void GetScriptsService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IGetScriptsService.Scripts))
        {
            _ = RefreshScriptsList();
        }
    }

    private readonly IGetScriptsService _getScriptsService;
    private readonly IProcessService _processService;

    [ObservableProperty]
    private bool _isManagerMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadedQuantity), nameof(OutdatedQuantity), nameof(ScriptQuantity), nameof(BotScriptQuantity))]
    private RangedObservableCollection<ScriptInfoViewModel> _scripts = new();

    [ObservableProperty]
    private ScriptInfoViewModel? _selectedItem;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _progressReportMessage = string.Empty;

    [ObservableProperty]
    private string _sortBy = "Name";

    [ObservableProperty]
    private bool _sortDescending = false;

    [ObservableProperty]
    private string _filterBy = "All";

    public List<string> SortOptions { get; } = new() { "Name", "Date Created" };
    
    public List<string> FilterOptions { get; } = new() { "All", "Army", "Classes", "Dailies", "Evil", "Farm", "Good", "Legion", "Local", "Nation", "Other", "Rep", "Seasonal", "Story", "Ultras" };

    partial void OnSortByChanged(string value)
    {
        ApplySort();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        ApplySort();
    }

    partial void OnFilterByChanged(string value)
    {
        _ = RefreshScriptsList();
    }

    private void ApplySort()
    {
        if (_scripts.Count == 0) return;

        IEnumerable<ScriptInfoViewModel> sorted;
        if (SortBy == "Date Created")
            sorted = SortDescending ? _scripts.OrderByDescending(x => x.Info.CreationDate ?? DateTime.MinValue) : _scripts.OrderBy(x => x.Info.CreationDate ?? DateTime.MinValue);
        else
            sorted = SortDescending ? _scripts.OrderByDescending(x => x.FileName) : _scripts.OrderBy(x => x.FileName);

        _scripts.ReplaceRange(sorted.ToList());
    }

    public int DownloadedQuantity => _getScriptsService?.Downloaded ?? 0;
    public int OutdatedQuantity => _getScriptsService?.Outdated ?? 0;
    public int ScriptQuantity => _getScriptsService?.Total ?? 0;
    public int BotScriptQuantity => _scripts.Count;
    public IRelayCommand OpenScriptFolderCommand { get; }

    [RelayCommand]
    private void AddCustomFolder()
    {
        var fileDialog = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<IFileDialogService>();
        string folder = fileDialog.OpenFolder(ClientFileSources.SkuaScriptsDIR);
        if (!string.IsNullOrEmpty(folder))
        {
            CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<ISettingsService>().Set("UserCustomScriptsFolder", folder);
            _ = RefreshScriptsList();
        }
    }

    [RelayCommand]
    private void ClearCustomFolder()
    {
        CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<ISettingsService>().Set("UserCustomScriptsFolder", string.Empty);
        _ = RefreshScriptsList();
    }

    [RelayCommand]
    private void LoadLocalScript()
    {
        var fileDialog = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<IFileDialogService>();
        string path = fileDialog.OpenFile(ClientFileSources.SkuaScriptsDIR, "Skua Scripts (*.cs)|*.cs");
        if (!string.IsNullOrEmpty(path))
        {
            var settings = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<ISettingsService>();
            var list = settings.Get<System.Collections.Specialized.StringCollection>("UserCustomScriptsList");
            if (list == null)
            {
                list = new System.Collections.Specialized.StringCollection();
            }
            if (!list.Contains(path))
            {
                list.Add(path);
                settings.Set("UserCustomScriptsList", list);
                _ = RefreshScriptsList();
            }

            CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger.Default.Send<Skua.Core.Messaging.LoadScriptMessage, int>(new(path), (int)Skua.Core.Messaging.MessageChannels.ScriptStatus);
        }
    }

    [RelayCommand]
    private void OpenScript()
    {
        if (SelectedItem is null || !SelectedItem.Downloaded)
            return;

        _processService.OpenVSC(SelectedItem.LocalFile);
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
                await _getScriptsService.GetScriptsAsync(progress, token);
            }, token);
        }
        catch { }
        await RefreshScriptsList();
    }

    [RelayCommand]
    private async Task UpdateDates(CancellationToken token)
    {
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(s => ProgressReportMessage = s);
            await Task.Run(async () =>
            {
                await _getScriptsService.UpdateScriptDatesAsync(progress, token);
            }, token);
        }
        catch { }
        finally
        {
            IsBusy = false;
        }
        await RefreshScriptsList();
    }

    private async Task RefreshScriptsList()
    {
        CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<ISettingsService>().ReloadSettings();

        if (_getScriptsService?.Scripts != null)
        {
            List<ScriptInfoViewModel> scriptViewModels = await Task.Run(() =>
            {
                List<ScriptInfoViewModel> viewModels = new();
                foreach (ScriptInfo script in _getScriptsService.Scripts)
                {
                    if (FilterBy != "All")
                    {
                        bool matches = false;
                        foreach (var part in script.FilePath.Split('/'))
                        {
                            if (part.StartsWith(FilterBy, StringComparison.OrdinalIgnoreCase))
                            {
                                matches = true;
                                break;
                            }
                        }
                        if (!matches) continue;
                    }
                        
                    if (script?.Name != null && !script.Name.Equals("null"))
                    {
                        if (script.Description?.Equals("null") == true)
                            script.Description = "No description provided.";

                        if (script.Tags?.Contains("null") == true && (script.Tags.Length == 1))
                            script.Tags = new[] { "no-tags" };
                        else script.Tags ??= new[] { "no-tags" };

                        viewModels.Add(new(script));
                    }
                }

                try
                {
                    var settings = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<ISettingsService>();
                    string localFolder = settings.Get<string>("UserCustomScriptsFolder");
                    
                    if (!string.IsNullOrEmpty(localFolder) && Directory.Exists(localFolder))
                    {
                        var files = Directory.GetFiles(localFolder, "*.cs", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
                        foreach (var file in files)
                        {
                            if (FilterBy != "All" && FilterBy != "Local") continue;
                            
                            var info = new ScriptInfo
                            {
                                Name = Path.GetFileNameWithoutExtension(file),
                                FileName = Path.GetFileName(file),
                                FilePath = file,
                                Description = "Local Custom Script",
                                Tags = new[] { "Local" },
                                CreationDate = File.GetCreationTime(file),
                                Size = (int)new FileInfo(file).Length,
                                Sha256 = null
                            };
                            viewModels.Add(new ScriptInfoViewModel(info) { Downloaded = true });
                        }
                    }

                    var scriptList = settings.Get<System.Collections.Specialized.StringCollection>("UserCustomScriptsList");
                    if (scriptList != null)
                    {
                        foreach (var file in scriptList)
                        {
                            if (!File.Exists(file)) continue;
                            if (FilterBy != "All" && FilterBy != "Local") continue;

                            var info = new ScriptInfo
                            {
                                Name = Path.GetFileNameWithoutExtension(file),
                                FileName = Path.GetFileName(file),
                                FilePath = file,
                                Description = "Local Single Script",
                                Tags = new[] { "Local", "Single" },
                                CreationDate = File.GetCreationTime(file),
                                Size = (int)new FileInfo(file).Length,
                                Sha256 = null
                            };
                            
                            // Prevent duplicates if it's already in the custom folder
                            if (!viewModels.Any(v => v.Info.FilePath == file))
                            {
                                viewModels.Add(new ScriptInfoViewModel(info) { Downloaded = true });
                            }
                        }
                    }
                }
                catch { }

                if (SortBy == "Date Created")
                    return SortDescending ? viewModels.OrderByDescending(x => x.Info.CreationDate ?? DateTime.MinValue).ToList() : viewModels.OrderBy(x => x.Info.CreationDate ?? DateTime.MinValue).ToList();
                else
                    return SortDescending ? viewModels.OrderByDescending(x => x.FileName).ToList() : viewModels.OrderBy(x => x.FileName).ToList();
            });
            _scripts.ReplaceRange(scriptViewModels);
        }

        OnPropertyChanged(nameof(Scripts));
        OnPropertyChanged(nameof(DownloadedQuantity));
        OnPropertyChanged(nameof(OutdatedQuantity));
        OnPropertyChanged(nameof(ScriptQuantity));
        OnPropertyChanged(nameof(BotScriptQuantity));
        IsBusy = false;
    }

    public void ProgressHandler(string message)
    {
        ProgressReportMessage = message;
    }

    [RelayCommand]
    private async Task Delete()
    {
        IsBusy = true;
        if (_selectedItem is null)
            return;

        if (_selectedItem.Info.Tags.Contains("Local"))
        {
            var dialogService = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<IDialogService>();
            
            if (_selectedItem.Info.Tags.Contains("Single"))
            {
                var result = dialogService.ShowMessageBox($"Do you want to permanently delete {_selectedItem.FileName} from your computer, or just remove it from the Custom Scripts list?", "Remove Custom Script", "Cancel", "Remove from List", "Delete File");
                if (result == null || result.Text == "Cancel" || result.Text == "")
                {
                    IsBusy = false;
                    return;
                }

                if (result.Text == "Delete File")
                {
                    await _getScriptsService.DeleteScriptAsync(_selectedItem.Info);
                }

                var settings = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<ISettingsService>();
                var list = settings.Get<System.Collections.Specialized.StringCollection>("UserCustomScriptsList");
                if (list != null && list.Contains(_selectedItem.Info.FilePath))
                {
                    list.Remove(_selectedItem.Info.FilePath);
                    settings.Set("UserCustomScriptsList", list);
                }
            }
            else
            {
                var result = dialogService.ShowMessageBox($"This script is inside your Custom Scripts Folder.\r\nDo you want to permanently delete {_selectedItem.FileName} from your computer?", "Delete Local Script", "No", "Yes");
                if (result != null && result.Text == "Yes")
                {
                    await _getScriptsService.DeleteScriptAsync(_selectedItem.Info);
                }
                else
                {
                    IsBusy = false;
                    return;
                }
            }

            _selectedItem.Downloaded = false;
            await RefreshScriptsList();
            IsBusy = false;
            return;
        }

        ProgressReportMessage = $"Deleting {_selectedItem.FileName}.";
        await _getScriptsService.DeleteScriptAsync(_selectedItem.Info);
        ProgressReportMessage = $"Deleted {_selectedItem.FileName}.";
        _selectedItem.Downloaded = false;
        OnPropertyChanged(nameof(DownloadedQuantity));
        OnPropertyChanged(nameof(OutdatedQuantity));
        OnPropertyChanged(nameof(ScriptQuantity));
        OnPropertyChanged(nameof(BotScriptQuantity));
        IsBusy = false;
    }

    [RelayCommand]
    private async Task Download()
    {
        IsBusy = true;
        if (_selectedItem is null)
            return;
        ProgressReportMessage = $"Downloading {_selectedItem.FileName}.";
        await _getScriptsService.DownloadScriptAsync(_selectedItem.Info);
        ProgressReportMessage = $"Downloaded {_selectedItem.FileName}.";
        _selectedItem.Downloaded = true;
        OnPropertyChanged(nameof(DownloadedQuantity));
        OnPropertyChanged(nameof(OutdatedQuantity));
        OnPropertyChanged(nameof(ScriptQuantity));
        OnPropertyChanged(nameof(BotScriptQuantity));
        IsBusy = false;
    }

    [RelayCommand]
    private async Task UpdateAll()
    {
        IsBusy = true;
        ProgressReportMessage = "Updating scripts...";
        int count = await _getScriptsService.DownloadAllWhereAsync(s => s.Outdated);
        ProgressReportMessage = $"Updated {count} scripts.";
        await RefreshScriptsList();
    }

    [RelayCommand]
    private async Task DownloadAll()
    {
        IsBusy = true;
        ProgressReportMessage = "Downloading outdated/missing scripts...";
        int count = await Task.Run(async () => await _getScriptsService.DownloadAllWhereAsync(s => !s.Downloaded || s.Outdated));
        ProgressReportMessage = $"Downloaded {count} scripts.";
        await RefreshScriptsList();
    }

    [RelayCommand]
    public void CancelTask()
    {
        if (RefreshScriptsCommand.IsRunning)
            RefreshScriptsCommand.Cancel();
        else if (DownloadAllCommand.IsRunning)
            DownloadAllCommand.Cancel();
        else if (UpdateAllCommand.IsRunning)
            UpdateAllCommand.Cancel();
        else if (DownloadCommand.IsRunning)
            DownloadCommand.Cancel();
        else if (DeleteCommand.IsRunning)
            DeleteCommand.Cancel();
        else
            ProgressReportMessage = string.Empty;
    }
}