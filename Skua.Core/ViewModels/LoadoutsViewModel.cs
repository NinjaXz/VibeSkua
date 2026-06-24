using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Skua.Core.ViewModels;

public partial class LoadoutsViewModel : BotControlViewModelBase
{
    private readonly ILoadoutService _loadoutService;
    private readonly IDialogService _dialogService;
    private readonly IDispatcherService _dispatcherService;

    [ObservableProperty]
    private ObservableCollection<LoadoutProfile> _loadouts;

    [ObservableProperty]
    private LoadoutProfile? _selectedLoadout;

    public LoadoutsViewModel(ILoadoutService loadoutService, IDialogService dialogService, IDispatcherService dispatcherService)
        : base("Loadouts", 600, 500)
    {
        _loadoutService = loadoutService;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;

        _loadoutService.LoadoutsChanged += () =>
        {
            _dispatcherService.Invoke(() => 
            {
                Loadouts.Clear();
                foreach (var l in _loadoutService.Loadouts)
                {
                    Loadouts.Add(l);
                }
            });
        };

        Loadouts = new ObservableCollection<LoadoutProfile>(_loadoutService.Loadouts);
    }

    [RelayCommand]
    private void Refresh()
    {
        _loadoutService.Refresh();
    }

    [RelayCommand]
    private async Task EquipLoadoutAsync()
    {
        if (!_loadoutService.IsLoggedIn)
        {
            _dialogService.ShowMessageBox("You must be logged in to equip loadouts.", "Action Failed");
            return;
        }

        if (SelectedLoadout == null)
            return;

        var missingItems = await _loadoutService.EquipLoadoutAsync(SelectedLoadout);
        
        if (missingItems != null && missingItems.Count > 0)
        {
            string missingList = string.Join("\n• ", missingItems);
            _dialogService.ShowMessageBox($"Loadout '{SelectedLoadout.Name}' was partially equipped.\nThe following items were missing or failed to equip:\n• {missingList}", "Equip Incomplete");
        }
        else
        {
            _dialogService.ShowMessageBox($"Successfully equipped loadout: {SelectedLoadout.Name}", "Loadout Equipped");
        }
    }

    [RelayCommand]
    private void SaveCurrent()
    {
        if (!_loadoutService.IsLoggedIn)
        {
            _dialogService.ShowMessageBox("You must be logged in to save loadouts.", "Action Failed");
            return;
        }

        InputDialogViewModel dialog = new("Save Loadout", "Enter Loadout Name:", false);
        if (_dialogService.ShowDialog(dialog) != true || string.IsNullOrWhiteSpace(dialog.DialogTextInput))
            return;

        string name = dialog.DialogTextInput.Trim();

        var profile = _loadoutService.CreateFromCurrentEquipped(name);
        
        if (!_loadoutService.SaveLoadout(profile))
        {
            _dialogService.ShowMessageBox("You must be logged in to save loadouts.", "Save Failed");
            return;
        }
        
        // Force synchronous update of the collection to prevent ComboBox selection race condition
        Loadouts.Clear();
        foreach (var l in _loadoutService.Loadouts)
        {
            Loadouts.Add(l);
        }
        
        SelectedLoadout = Loadouts.FirstOrDefault(l => l.Name == profile.Name);
    }

    [RelayCommand]
    private void DeleteLoadout()
    {
        if (!_loadoutService.IsLoggedIn)
        {
            _dialogService.ShowMessageBox("You must be logged in to delete loadouts.", "Action Failed");
            return;
        }

        if (SelectedLoadout == null || SelectedLoadout.Name == "Currently Equipped")
            return;

        if (_dialogService.ShowMessageBox($"Are you sure you want to delete the '{SelectedLoadout.Name}' loadout?", "Confirm Deletion", true) != true)
            return;

        _loadoutService.DeleteLoadout(SelectedLoadout);
        Loadouts.Remove(SelectedLoadout);
        SelectedLoadout = null;
    }

    partial void OnSelectedLoadoutChanged(LoadoutProfile? value)
    {
        OnPropertyChanged(nameof(SelectedLoadoutName));
    }

    public string SelectedLoadoutName
    {
        get => SelectedLoadout?.Name ?? string.Empty;
        set
        {
            if (SelectedLoadout != null && SelectedLoadout.Name != value && SelectedLoadout.Name != "Currently Equipped")
            {
                if (_dialogService.ShowMessageBox($"Are you sure you want to rename '{SelectedLoadout.Name}' to '{value}'?", "Confirm Rename", true) != true)
                {
                    OnPropertyChanged(nameof(SelectedLoadoutName));
                    return;
                }

                SelectedLoadout.Name = value;
                if (!_loadoutService.SaveLoadout(SelectedLoadout))
                {
                    _dialogService.ShowMessageBox("You must be logged in to rename loadouts.", "Rename Failed");
                    return;
                }
                
                // No manual rebuilding needed here since LoadoutsChanged will be fired by the service
                OnPropertyChanged();
            }
        }
    }

    [RelayCommand]
    private void ShowCurrent()
    {
        if (!_loadoutService.IsLoggedIn)
        {
            _dialogService.ShowMessageBox("You must be logged in to view your currently equipped items.", "Action Failed");
            return;
        }

        var current = _loadoutService.CreateFromCurrentEquipped("Currently Equipped");
        var existing = Loadouts.FirstOrDefault(l => l.Name == "Currently Equipped");
        if (existing != null)
            Loadouts.Remove(existing);
            
        Loadouts.Insert(0, current);
        SelectedLoadout = current;
    }
}
