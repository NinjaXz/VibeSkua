using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.App.Avalonia.ViewModels.Dialogs;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace Skua.App.Avalonia.ViewModels.HotKeys;

public partial class HotKeysViewModel : BotControlViewModelBase, IManagedWindow
{
    public HotKeysViewModel(IHotKeyService hotKeyService, ISettingsService settingsService, IDialogService dialogService)
        : base("HotKeys", 480, 500)
    {
        _hotKeyService = hotKeyService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        HotKeys = new ObservableCollection<HotKeyItemViewModel>(_hotKeyService.GetHotKeys<HotKeyItemViewModel>());
        foreach (HotKeyItemViewModel hotKey in HotKeys)
            hotKey.Description = global::Skua.Core.AppStartup.HotKeys.GetDescription(hotKey.Binding);
        HotKeys.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilteredHotKeys));
        AddCustomCommand = new RelayCommand(AddCustomAction);
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
            recipient._cachedAvailableActions = null;
            recipient.OnPropertyChanged(nameof(AvailableCustomActions));
            global::Skua.Core.AppStartup.HotKeys.BroadcastReloadHotkeys();
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
                _cachedAvailableActions = _hotKeyService.GetAvailableHotKeyBindings()
                    .Where(k => !HotKeys.Any(h => string.Equals(h.Binding, k, StringComparison.OrdinalIgnoreCase)))
                    .Select(k => new HotKeyItemViewModel
                    {
                        Binding = k,
                        Title = global::Skua.Core.AppStartup.HotKeys.GetFormattedTitle(k),
                        Description = global::Skua.Core.AppStartup.HotKeys.GetDescription(k),
                        KeyGesture = "Unassigned"
                    })
                    .OrderBy(a => a.Title)
                    .ToList();
            }
            return _cachedAvailableActions;
        }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredHotKeys))]
    private string _filterHotKeysText = string.Empty;

    public IEnumerable<HotKeyItemViewModel> FilteredHotKeys =>
        string.IsNullOrWhiteSpace(FilterHotKeysText)
            ? HotKeys
            : HotKeys.Where(h => h.Title.Contains(FilterHotKeysText, StringComparison.OrdinalIgnoreCase)
                              || (h.Description != null && h.Description.Contains(FilterHotKeysText, StringComparison.OrdinalIgnoreCase))
                              || h.KeyGesture.Contains(FilterHotKeysText, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void ClearFilter()
    {
        FilterHotKeysText = string.Empty;
    }

    public List<HotKeyItemViewModel> FilteredCustomActions => AvailableCustomActions;

    private HotKeyItemViewModel? _selectedCustomAction;
    public HotKeyItemViewModel? SelectedCustomAction
    {
        get => _selectedCustomAction;
        set => SetProperty(ref _selectedCustomAction, value);
    }

    public IRelayCommand AddCustomCommand { get; }

    private void AddCustomAction()
    {
        if (SelectedCustomAction == null && !string.IsNullOrWhiteSpace(SearchText))
        {
            var trimmed = SearchText.Trim();
            string[] terms = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            SelectedCustomAction = AvailableCustomActions.FirstOrDefault(a => a.Title.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                                ?? AvailableCustomActions.FirstOrDefault(a => a.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                                ?? AvailableCustomActions.FirstOrDefault(a => terms.All(t => $"{a.Title} {a.Description} {a.Binding}".Contains(t, StringComparison.OrdinalIgnoreCase)));
        }

        if (SelectedCustomAction == null)
            return;

        var hk = new HotKeyItemViewModel
        {
            Binding = SelectedCustomAction.Binding,
            Title = SelectedCustomAction.Title,
            Description = SelectedCustomAction.Description,
            KeyGesture = "Unassigned"
        };
        StrongReferenceMessenger.Default.Register<HotKeyItemViewModel, HotKeyErrorMessage>(hk, HandleError);
        HotKeys.Add(hk);
        Save();
        _hotKeyService.Reload();
        global::Skua.Core.AppStartup.HotKeys.BroadcastReloadHotkeys();

        SelectedCustomAction = null;
        _cachedAvailableActions = null;
        SearchText = string.Empty;
        OnPropertyChanged(nameof(AvailableCustomActions));
        OnPropertyChanged(nameof(FilteredHotKeys));
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
            HotKeyItemViewModel? hk = recipient.HotKeys.FirstOrDefault(h =>
                string.Equals(h.Title, message.Title, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(h.Binding, message.Title, StringComparison.OrdinalIgnoreCase));
            if (hk != null)
            {
                hk.KeyGesture = diag.KeyGesture;
                recipient.Save();
                recipient._hotKeyService.Reload();
                global::Skua.Core.AppStartup.HotKeys.BroadcastReloadHotkeys();
            }
        }
    }
}