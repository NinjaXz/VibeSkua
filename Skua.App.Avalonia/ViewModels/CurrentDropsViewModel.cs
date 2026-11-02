using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models.Items;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Skua.App.Avalonia.ViewModels;

public partial class CurrentDropsViewModel : BotControlViewModelBase
{
    public CurrentDropsViewModel(IScriptDrop drops, IScriptPlayer player)
        : base("Current Drops", 500, 400)
    {
        _drops = drops;
        _player = player;
        FilteredCurrentDrops = new ObservableCollection<ItemBase>();
    }

    protected override void OnActivated()
    {
        StrongReferenceMessenger.Default.Register<CurrentDropsViewModel, ItemDroppedMessage, int>(this, (int)MessageChannels.GameEvents, CurrentDropsChanged);
        StrongReferenceMessenger.Default.Register<CurrentDropsViewModel, DropPickupResultMessage, int>(this, (int)MessageChannels.GameEvents, PickupResultReceived);
        RefreshDrops();
    }

    protected override void OnDeactivated()
    {
        StrongReferenceMessenger.Default.UnregisterAll(this);
    }

    private readonly IScriptPlayer _player;
    private readonly IScriptDrop _drops;
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
    private ItemBase? _selectedDrop;

    public ObservableCollection<ItemBase> FilteredCurrentDrops { get; }

    public List<ItemBase> CurrentDrops
    {
        get
        {
            List<ItemBase> drops = new();
            foreach (ItemBase drop in _drops.CurrentDropInfos)
                drops.Add(drop);
            return drops;
        }
    }

    [RelayCommand]
    private void Pickup()
    {
        if (SelectedDrop is null || !_player.Playing)
            return;

        _drops.Pickup(SelectedDrop.ID);
    }

    [RelayCommand]
    private void PickupSelected(IList<object>? items)
    {
        if (items is null || items.Count == 0 || !_player.Playing)
            return;

        int[] dropIds = new int[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is ItemBase ib)
                dropIds[i] = ib.ID;
        }
        // Pickup(int[]) waits for the server's reply, which arrives on the UI thread: don't block it.
        Task.Run(() => _drops.Pickup(dropIds));
    }

    [RelayCommand]
    private void PickupAll()
    {
        _drops.PickupAll(true);
    }

    [RelayCommand]
    private void PickupAC()
    {
        Task.Run(() => _drops.PickupACItems());
    }

    private void CurrentDropsChanged(CurrentDropsViewModel recipient, ItemDroppedMessage message)
    {
        recipient.RefreshDrops();
    }

    private void PickupResultReceived(CurrentDropsViewModel recipient, DropPickupResultMessage message)
    {
        // Posted so the list is read after ScriptDrop has handled the same message.
        Dispatcher.UIThread.Post(recipient.RefreshDrops);
    }

    private void RefreshDrops()
    {
        OnPropertyChanged(nameof(CurrentDrops));
        ApplySearch();
    }

    private void ApplySearch()
    {
        string filter = SearchText.Trim();
        FilteredCurrentDrops.Clear();
        foreach (ItemBase drop in CurrentDrops)
        {
            if (string.IsNullOrEmpty(filter) ||
                drop.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                drop.ID.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredCurrentDrops.Add(drop);
            }
        }
    }
}