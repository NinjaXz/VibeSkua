using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models.Quests;
using Skua.Core.Utils;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.ViewModels;

public partial class LoaderViewModel : BotControlViewModelBase, IManagedWindow
{
    public LoaderViewModel(IScriptShop shops, IScriptQuest quests, IQuestDataLoaderService questLoader, IClipboardService clipboardService)
        : base("Loader", 720, 520)
    {
        _shops = shops;
        _quests = quests;
        _questLoader = questLoader;
        _clipboardService = clipboardService;
        FilteredQuestIDs = new ObservableCollection<QuestData>();

        _ = GetQuestsAsync();
    }

    private CancellationTokenSource? _loaderCTS;
    private readonly IScriptShop _shops;
    private readonly IScriptQuest _quests;
    private readonly IQuestDataLoaderService _questLoader;
    private readonly IClipboardService _clipboardService;
    private string _searchText = string.Empty;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplySearch();
        }
    }

    [ObservableProperty]
    private string _progressReport = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _probePastCeiling;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    private string _inputIDs = string.Empty;

    [ObservableProperty]
    private string _questRange = string.Empty;

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private RangedObservableCollection<QuestData> _questIDs = new();

    public ObservableCollection<QuestData> FilteredQuestIDs { get; }

    [ObservableProperty]
    private QuestData? _selectedQuest;

    [RelayCommand(CanExecute = nameof(AllDigits))]
    private void Load()
    {
        if (SelectedIndex == 0 && int.TryParse(InputIDs, out int id))
        {
            Task.Factory.StartNew(() => _shops.Load(id));
            return;
        }
        if (SelectedIndex == 1)
        {
            string[] parts = InputIDs.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int[] questIds = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int qId))
                    questIds[i] = qId;
            }
            Task.Factory.StartNew(() => _quests.Load(questIds));
        }
    }

    private bool AllDigits()
    {
        if (string.IsNullOrWhiteSpace(InputIDs))
            return false;

        string normalized = InputIDs.Replace(",", "").Replace(" ", "");
        return normalized.Length > 0 && normalized.All(char.IsDigit);
    }

    [RelayCommand]
    private void LoadQuests(IList<object>? items)
    {
        if (items is null || items.Count == 0)
        {
            if (SelectedQuest != null)
                _quests.Load(SelectedQuest.ID);
            return;
        }

        int[] questIds = new int[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is QuestData qd)
                questIds[i] = qd.ID;
        }
        _quests.Load(questIds);
    }

    [RelayCommand]
    private void CopyQuestsNames(IList<object>? items)
    {
        if (items is null || items.Count == 0)
        {
            if (SelectedQuest != null)
                _clipboardService.SetText(SelectedQuest.Name);
            return;
        }

        List<string> names = new(items.Count);
        foreach (object item in items)
        {
            if (item is QuestData qd)
                names.Add(qd.Name);
        }
        _clipboardService.SetText(string.Join(",", names));
    }

    [RelayCommand]
    private void CopyQuestsIDs(IList<object>? items)
    {
        if (items is null || items.Count == 0)
        {
            if (SelectedQuest != null)
                _clipboardService.SetText(SelectedQuest.ID.ToString());
            return;
        }

        List<int> ids = new(items.Count);
        foreach (object item in items)
        {
            if (item is QuestData qd)
                ids.Add(qd.ID);
        }
        _clipboardService.SetText(string.Join(",", ids));
    }

    [RelayCommand]
    private void CopyQuestsNamesAndIDs(IList<object>? items)
    {
        if (items is null || items.Count == 0)
        {
            if (SelectedQuest != null)
                _clipboardService.SetText($"// {SelectedQuest.Name} | {SelectedQuest.ID}");
            return;
        }

        List<string> lines = new(items.Count);
        foreach (object item in items)
        {
            if (item is QuestData quest)
                lines.Add($"// {quest.Name} | {quest.ID}");
        }
        _clipboardService.SetText(string.Join(Environment.NewLine, lines));
    }

    [RelayCommand]
    private async Task UpdateQuests(bool getAll)
    {
        _loaderCTS = new();
        QuestIDs.Clear();
        Progress<string> progress = new(p =>
        {
            IsLoading = true;
            ProgressReport = p;
        });
        try
        {
            List<QuestData> questData = await _questLoader.UpdateAsync("QuestData.json", getAll, progress, _loaderCTS.Token, ProbePastCeiling);
            QuestIDs.Clear();
            QuestIDs.AddRange(questData);
            ApplySearch();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoading = false;
            ProgressReport = string.Empty;
            _loaderCTS.Dispose();
            _loaderCTS = null;
        }
    }

    [RelayCommand]
    private async Task GetQuestsAsync()
    {
        IsLoading = true;
        ProgressReport = "Getting quests";

        try
        {
            List<QuestData> questData = await _questLoader.GetFromFileAsync("QuestData.json");
            QuestIDs.Clear();
            QuestIDs.AddRange(questData);
            ApplySearch();
            ProgressReport = string.Empty;
        }
        catch (Exception ex)
        {
            ProgressReport = $"Failed to load quests: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task UpdateQuestsRange()
    {
        string[] parts = QuestRange.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2 || !int.TryParse(parts[0], out int startId) || !int.TryParse(parts[1], out int endId))
        {
            ProgressReport = "Invalid format. Use: startID, endID";
            await Task.Delay(2000);
            ProgressReport = string.Empty;
            return;
        }

        if (startId > endId)
        {
            ProgressReport = "Start ID must be less than or equal to End ID";
            await Task.Delay(2000);
            ProgressReport = string.Empty;
            return;
        }

        _loaderCTS = new();
        QuestIDs.Clear();
        Progress<string> progress = new(p =>
        {
            IsLoading = true;
            ProgressReport = p;
        });
        try
        {
            List<QuestData> questData = await _questLoader.UpdateRangeAsync("QuestData.json", startId, endId, progress, _loaderCTS.Token);
            QuestIDs.Clear();
            QuestIDs.AddRange(questData);
            ApplySearch();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoading = false;
            ProgressReport = string.Empty;
            _loaderCTS.Dispose();
            _loaderCTS = null;
        }
    }

    [RelayCommand]
    private void CancelQuestLoad()
    {
        if (_loaderCTS is not null)
        {
            _loaderCTS.Cancel();
            ProgressReport = "Cancelling task...";
        }
    }

    [RelayCommand]
    private async Task UpdateSelectedQuest(object? item)
    {
        QuestData? targetQuest = item as QuestData ?? SelectedQuest;
        if (targetQuest is null)
            return;

        try
        {
            IsLoading = true;
            ProgressReport = $"Fake completing quest: {targetQuest.Name}.";
            await Task.Run(() => _quests.UpdateQuest(targetQuest.ID));
            IsLoading = false;
            ProgressReport = string.Empty;
        }
        catch (Exception ex)
        {
            ProgressReport = $"Failed to update quest: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplySearch()
    {
        string query = SearchText.Trim();
        FilteredQuestIDs.Clear();
        foreach (QuestData quest in QuestIDs)
        {
            if (string.IsNullOrEmpty(query) ||
                quest.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                quest.ID.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredQuestIDs.Add(quest);
            }
        }
    }
}
