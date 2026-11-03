using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using Skua.Core.Utils;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.ViewModels.Runtime;

public sealed partial class JunkItemsViewModel : BotControlViewModelBase
{
    private readonly IScriptInventory _inventory;
    private readonly IScriptBank _bank;
    private readonly IJunkService _junkService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settings;
    private string _searchText = string.Empty;
    private string _selectedCategory = "All Categories";
    private string _acFilter = "All Items";
    private string _goalQuantityInput = string.Empty;

    public JunkItemsViewModel(
        IScriptInventory inventory,
        IScriptBank bank,
        IJunkService junkService,
        IDialogService dialogService,
        ISettingsService settings)
       : base("Junk Items", 700, 500)
    {
        _inventory = inventory;
        _bank = bank;
        _junkService = junkService;
        _dialogService = dialogService;
        _settings = settings;
        Items = new RangedObservableCollection<JunkItemEntry>();
        FilteredItems = new ObservableCollection<JunkItemEntry>();
        CategoryOptions = new ObservableCollection<string> { "All Categories" };
        AcFilterOptions = new ObservableCollection<string> { "All Items", "AC Items", "Non-AC Items" };
        _skipSellWarning = _settings.Get("JunkSkipSellWarning", false);
    }

    public RangedObservableCollection<JunkItemEntry> Items { get; }
    public ObservableCollection<JunkItemEntry> FilteredItems { get; }
    public ObservableCollection<string> CategoryOptions { get; }
    public ObservableCollection<string> AcFilterOptions { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplySearch();
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
                ApplySearch();
        }
    }

    public string GoalQuantityInput
    {
        get => _goalQuantityInput;
        set => SetProperty(ref _goalQuantityInput, value);
    }

    public string AcFilter
    {
        get => _acFilter;
        set
        {
            if (SetProperty(ref _acFilter, value))
                ApplySearch();
        }
    }

    [ObservableProperty]
    private bool _skipSellWarning;

    public int TotalJunk => Items.Count(i => i.IsJunk);

    public int SelectedCount => Items.Count(i => i.IsSelected);

    // Drives the select-all check box above the list: true when every shown item is selected, false when none
    // is, null (indeterminate) in between. Checking it selects the shown items; unchecking clears the selection.
    public bool? SelectionState
    {
        get
        {
            int selected = SelectedCount;
            if (selected == 0)
                return false;
            return FilteredItems.Count > 0 && FilteredItems.All(i => i.IsSelected) ? true : null;
        }
        set
        {
            if (value == true)
                SelectAll();
            else
                ClearSelection();
        }
    }

    private void Entry_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JunkItemEntry.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectionState));
        }
        else if (e.PropertyName == nameof(JunkItemEntry.IsJunk))
        {
            OnPropertyChanged(nameof(TotalJunk));
        }
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        _ = Refresh();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        _junkService.Load();

        Dictionary<int, int> goals = _settings.Get("InventoryGoals", new Dictionary<int, int>());
        var junkIds = _junkService.JunkItems
            .GroupBy(j => j.ID)
            .Select(g => g.Key)
            .ToHashSet();

        var previousStates = Items
            .ToList()
            .ToDictionary<JunkItemEntry, (int ID, string Category), bool>(
                e => (e.ID, e.Category),
                e => e.IsSelected);

        List<JunkItemEntry> entries = await Task.Run(() =>
        {
            if (!_bank.Loaded)
                _bank.Load();

            List<InventoryItem> inventoryItems = _inventory.Items.ToList();
            List<InventoryItem> bankItems = _bank.Items.ToList();

            return inventoryItems.Concat(bankItems)
                .GroupBy(item => item.ID)
                .Select(group =>
                {
                    InventoryItem item = group.First();
                    int inventoryQuantity = inventoryItems.Where(candidate => candidate.ID == item.ID).Sum(candidate => candidate.Quantity);
                    int bankQuantity = bankItems.Where(candidate => candidate.ID == item.ID).Sum(candidate => candidate.Quantity);
                    return new JunkItemEntry(item.ID, item.Name, item.Category.ToString(), item.Coins, item.Equipped, item.MaxStack, inventoryQuantity, bankQuantity, junkIds.Contains(item.ID), _inventory.IsFavorited(item.ID), goals.TryGetValue(item.ID, out int goalQuantity) ? goalQuantity : 0);
                })
                .OrderBy(item => item.Name)
                .ToList();
        });

        foreach (JunkItemEntry entry in entries)
        {
            if (previousStates.TryGetValue((entry.ID, entry.Category), out bool isSelected))
                entry.IsSelected = isSelected;
        }

        foreach (JunkItemEntry old in Items)
            old.PropertyChanged -= Entry_PropertyChanged;
        foreach (JunkItemEntry entry in entries)
            entry.PropertyChanged += Entry_PropertyChanged;

        Items.ReplaceRange(entries);
        CategoryOptions.Clear();
        CategoryOptions.Add("All Categories");
        foreach (string category in entries.Select(entry => entry.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(category => category))
            CategoryOptions.Add(category);
        if (!CategoryOptions.Contains(SelectedCategory))
            SelectedCategory = "All Categories";
        ApplySearch();
        OnPropertyChanged(nameof(TotalJunk));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionState));
    }

    [RelayCommand]
    private void SetGoalForSelected()
    {
        if (!int.TryParse(GoalQuantityInput, out int quantity) || quantity <= 0)
            return;

        Dictionary<int, int> goals = _settings.Get("InventoryGoals", new Dictionary<int, int>());
        foreach (JunkItemEntry entry in Items.Where(item => item.IsSelected))
        {
            goals[entry.ID] = quantity;
            entry.GoalQuantity = quantity;
        }

        _settings.Set("InventoryGoals", goals);
        GoalQuantityInput = string.Empty;
    }

    [RelayCommand]
    private void ClearGoalForSelected()
    {
        Dictionary<int, int> goals = _settings.Get("InventoryGoals", new Dictionary<int, int>());
        foreach (JunkItemEntry entry in Items.Where(item => item.IsSelected))
        {
            goals.Remove(entry.ID);
            entry.GoalQuantity = 0;
        }

        _settings.Set("InventoryGoals", goals);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in FilteredItems)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in Items)
            item.IsSelected = false;
    }

    [RelayCommand]
    private void ToggleJunk()
    {
        var selected = Items.Where(e => e.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        bool allJunk = selected.All(e => e.IsJunk);
        foreach (var item in selected)
            item.IsJunk = !allJunk;

        PersistFromEntries(Items.ToList());
    }

    [RelayCommand]
    private void MarkAsJunk()
    {
        if (Items.Count == 0)
            return;

        var updated = Items.ToList();
        foreach (JunkItemEntry entry in updated.Where(e => e.IsSelected))
        {
            entry.IsJunk = true;
        }

        PersistFromEntries(updated);
    }

    [RelayCommand]
    private void UnmarkAsJunk()
    {
        if (Items.Count == 0)
            return;

        var updated = Items.ToList();
        foreach (JunkItemEntry entry in updated.Where(e => e.IsSelected))
        {
            entry.IsJunk = false;
            entry.IsSelected = false;
        }

        PersistFromEntries(updated);
    }

    [RelayCommand]
    private void UnmarkAllJunk()
    {
        if (Items.Count == 0)
            return;

        var updated = Items.ToList();
        foreach (JunkItemEntry entry in updated)
        {
            entry.IsJunk = false;
            entry.IsSelected = false;
        }

        PersistFromEntries(updated);
    }

    [RelayCommand]
    private async Task SellSelected()
    {
        var selected = Items.Where(e => e.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        if (!SkipSellWarning)
        {
            bool? confirm = _dialogService.ShowMessageBox(
                $"This will sell {selected.Count} selected items from your inventory. This cannot be undone. Continue?",
                "Sell Selected",
                yesAndNo: true);

            if (confirm != true)
                return;
        }

        foreach (var item in selected)
            item.IsJunk = true;
        PersistFromEntries(Items.ToList());

        _junkService.SellAllJunk();
        await Refresh();
    }

    [RelayCommand]
    private async Task SellAllJunk()
    {
        if (Items.Count == 0)
            return;

        if (TotalJunk == 0)
            return;

        if (!SkipSellWarning)
        {
            bool? confirm = _dialogService.ShowMessageBox(
                "This will sell all items marked as junk in your inventory. This cannot be undone. Continue?",
                "Sell All Junk",
                yesAndNo: true);

            if (confirm != true)
                return;
        }

        _junkService.SellAllJunk();
        await Refresh();
    }

    partial void OnSkipSellWarningChanged(bool value)
    {
        if (value)
        {
            bool? confirm = _dialogService.ShowMessageBox(
                "Are you sure you want to skip the confirmation when selling all junk?",
                "Skip Sell Warning",
                yesAndNo: true);

            if (confirm != true)
            {
                _skipSellWarning = false;
                OnPropertyChanged(nameof(SkipSellWarning));
                return;
            }
        }

        _settings.Set("JunkSkipSellWarning", value);
    }

    private void PersistFromEntries(List<JunkItemEntry> entries)
    {
        var configs = entries
            .Where(e => e.IsJunk)
            .Select(e => new JunkItemConfig
            {
                ID = e.ID,
                Name = e.Name,
                Category = e.Category,
                Meta = string.Empty
            })
            .ToList();

        _junkService.SetJunk(configs);
        ApplySearch();
        OnPropertyChanged(nameof(TotalJunk));
    }

    private void ApplySearch()
    {
        string filter = SearchText.Trim();
        FilteredItems.Clear();
        foreach (JunkItemEntry item in Items)
        {
            bool matchesSearch = string.IsNullOrEmpty(filter)
                || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || item.ID.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)
                || item.Category.Contains(filter, StringComparison.OrdinalIgnoreCase);
            bool matchesCategory = SelectedCategory == "All Categories"
                || item.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase);
            bool matchesAc = AcFilter == "All Items"
                || (AcFilter == "AC Items" && item.IsAc)
                || (AcFilter == "Non-AC Items" && !item.IsAc);

            if (matchesSearch && matchesCategory && matchesAc)
                FilteredItems.Add(item);
        }
        OnPropertyChanged(nameof(SelectionState));
    }
}

public sealed partial class JunkItemEntry : ObservableObject
{
    public JunkItemEntry(int id, string name, string category, bool isAc, bool isEquipped, int maxStack, int inventoryQuantity, int bankQuantity, bool isJunk, bool isFavorite, int goalQuantity)
    {
        ID = id;
        Name = name;
        Category = category;
        IsAc = isAc;
        IsEquipped = isEquipped;
        IsFavorite = isFavorite;
        MaxStack = maxStack;
        _goalQuantity = goalQuantity;
        InventoryQuantity = inventoryQuantity;
        BankQuantity = bankQuantity;
        _isJunk = isJunk;
    }

    public int ID { get; }
    public string Name { get; }
    public string Category { get; }
    public bool IsAc { get; }
    public bool IsEquipped { get; }
    public bool IsFavorite { get; }
    public int MaxStack { get; }
    public bool IsGoalTracked => GoalQuantity > 0;
    public string GoalProgressDisplay => $"{Math.Min(InventoryQuantity + BankQuantity, GoalQuantity):N0} / {GoalQuantity:N0}";
    public bool IsNearStackLimit => MaxStack > 0 && InventoryQuantity >= MaxStack * 0.8;
    public string StackDisplay => MaxStack > 0 ? $"{InventoryQuantity:N0} / {MaxStack:N0}" : InventoryQuantity.ToString("N0");
    public int InventoryQuantity { get; }
    public int BankQuantity { get; }
    public string QuantityDisplay => $"Inv: {InventoryQuantity}  |  Bank: {BankQuantity}";

    [ObservableProperty]
    private int _goalQuantity;

    partial void OnGoalQuantityChanged(int value)
    {
        OnPropertyChanged(nameof(IsGoalTracked));
        OnPropertyChanged(nameof(GoalProgressDisplay));
    }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isJunk;

    public string Display => $"[{ID}] {Name} ({Category})";
}