using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Linq;

namespace Skua.Core.ViewModels;

public partial class HotKeysViewModel : BotControlViewModelBase, IManagedWindow
{
    public HotKeysViewModel(IHotKeyService hotKeyService, ISettingsService settingsService, IDialogService dialogService)
        : base("HotKeys", 480, 500)
    {
        _hotKeyService = hotKeyService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        HotKeys = new ObservableCollection<HotKeyItemViewModel>(_hotKeyService.GetHotKeys<HotKeyItemViewModel>());
        AddCustomCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(AddCustomAction);
        _hotKeyService.Reload();
    }

    private readonly IHotKeyService _hotKeyService;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;

    protected override void OnActivated()
    {
        StrongReferenceMessenger.Default.Register<HotKeysViewModel, EditHotKeyMessage>(this, EditHotKey);
        StrongReferenceMessenger.Default.Register<HotKeysViewModel, RemoveHotKeyMessage>(this, RemoveHotKey);
        foreach (HotKeyItemViewModel hk in HotKeys)
            StrongReferenceMessenger.Default.Register<HotKeyItemViewModel, HotKeyErrorMessage>(hk, HandleError);
    }

    private void RemoveHotKey(HotKeysViewModel recipient, RemoveHotKeyMessage message)
    {
        var hk = recipient.HotKeys.FirstOrDefault(h => h.Binding == message.Binding);
        if (hk != null)
        {
            recipient.HotKeys.Remove(hk);
            recipient.Save();
            recipient._hotKeyService.Reload();
            recipient._cachedAvailableActions = null; // Invalidate cache
            recipient.OnPropertyChanged(nameof(AvailableCustomActions));
            recipient.OnPropertyChanged(nameof(FilteredCustomActions));
        }
    }

    private void HandleError(HotKeyItemViewModel recipient, HotKeyErrorMessage message)
    {
        if (message.Binding == recipient.Binding)
            recipient.KeyGesture = "Failed to bind";
    }

    protected override void OnDeactivated()
    {
        base.OnDeactivated();
        StrongReferenceMessenger.Default.UnregisterAll(this);
        foreach (HotKeyItemViewModel hk in HotKeys)
            StrongReferenceMessenger.Default.UnregisterAll(hk);
    }

    public ObservableCollection<HotKeyItemViewModel> HotKeys { get; }

    private List<HotKeyItemViewModel>? _cachedAvailableActions;
    public List<HotKeyItemViewModel> AvailableCustomActions
    {
        get
        {
            if (_cachedAvailableActions == null)
            {
                _cachedAvailableActions = Skua.Core.AppStartup.HotKeys.GetRegistry().Keys
                    .Where(k => !HotKeys.Any(h => h.Binding == k))
                    .Select(k => new HotKeyItemViewModel 
                    { 
                        Binding = k, 
                        Title = Skua.Core.AppStartup.HotKeys.GetFormattedTitle(k), 
                        Description = Skua.Core.AppStartup.HotKeys.GetDescription(k),
                        KeyGesture = "Unassigned"
                    }).ToList();
            }
            return _cachedAvailableActions;
        }
    }
    
    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                OnPropertyChanged(nameof(FilteredCustomActions));
        }
    }

    public List<HotKeyItemViewModel> FilteredCustomActions =>
        string.IsNullOrWhiteSpace(SearchText)
            ? AvailableCustomActions
            : AvailableCustomActions.Where(a => a.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
    
    private HotKeyItemViewModel? _selectedCustomAction;
    public HotKeyItemViewModel? SelectedCustomAction
    {
        get => _selectedCustomAction;
        set => SetProperty(ref _selectedCustomAction, value);
    }

    public CommunityToolkit.Mvvm.Input.IRelayCommand AddCustomCommand { get; }

    private void AddCustomAction()
    {
        if (SelectedCustomAction == null) return;

        var hk = new HotKeyItemViewModel { Binding = SelectedCustomAction.Binding, Title = SelectedCustomAction.Title, Description = SelectedCustomAction.Description, KeyGesture = "Unassigned" };
        StrongReferenceMessenger.Default.Register<HotKeyItemViewModel, HotKeyErrorMessage>(hk, HandleError);
        HotKeys.Add(hk);
        Save();
        _hotKeyService.Reload();

        SelectedCustomAction = null;
        _cachedAvailableActions = null; // Invalidate cache
        SearchText = string.Empty; // Reset search
        OnPropertyChanged(nameof(AvailableCustomActions));
        OnPropertyChanged(nameof(FilteredCustomActions));
    }

    private void Save()
    {
        StringCollection hotkeys = new();
        foreach (HotKeyItemViewModel hk in HotKeys)
            hotkeys.Add($"{hk.Binding}|{hk.KeyGesture}");

        _settingsService.Set("HotKeys", hotkeys);
    }

    private void EditHotKey(HotKeysViewModel recipient, EditHotKeyMessage message)
    {
        HotKey? hotKey = recipient._hotKeyService.ParseToHotKey(message.KeyGesture);
        AssignHotKeyDialogViewModel diag = hotKey is null ? new(message.Title) : new(message.Title, hotKey);
        
        diag.UsedGestures = recipient.HotKeys
            .Where(h => h.Title != message.Title && !string.IsNullOrEmpty(h.KeyGesture) && h.KeyGesture != "Unassigned" && h.KeyGesture != "Failed to bind")
            .Select(h => h.KeyGesture)
            .ToList();

        if (recipient._dialogService.ShowDialog(diag) == true)
        {
            HotKeyItemViewModel? hk = recipient.HotKeys.FirstOrDefault(h => h.Title == message.Title);
            if (hk != null)
            {
                hk.KeyGesture = diag.KeyGesture;
                recipient.Save();
                recipient._hotKeyService.Reload();
            }
        }
    }
}