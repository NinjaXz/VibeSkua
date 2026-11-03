using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Skua.App.Avalonia.ViewModels.Packets;
using Skua.App.Avalonia.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Views;

public partial class PacketLoggerView : UserControl
{
    private PacketLoggerViewModel? _vm;
    private ObservableCollection<string>? _packets;

    public PacketLoggerView()
    {
        InitializeComponent();
        GrandpaModeState.Changed += ApplyGrandpaMode;
        ApplyGrandpaMode(GrandpaModeState.Enabled);
        DataContextChanged += (_, _) => Subscribe(DataContext as PacketLoggerViewModel);
    }

    private void ApplyGrandpaMode(bool enabled)
    {
        PacketsListBox.FontSize = enabled ? 15 : 11;
        PacketsListBox.Padding = new global::Avalonia.Thickness(enabled ? 7 : 4);
        CountText.FontSize = enabled ? 13 : 11;
    }

    private void Subscribe(PacketLoggerViewModel? vm)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= Vm_PropertyChanged;
        _vm = vm;
        if (_vm is not null)
            _vm.PropertyChanged += Vm_PropertyChanged;
        WatchPackets();
    }

    // The search replaces FilteredPacketLogs, so the count follows whichever collection is current.
    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PacketLoggerViewModel.FilteredPacketLogs))
            WatchPackets();
    }

    private void WatchPackets()
    {
        if (_packets is not null)
            _packets.CollectionChanged -= Packets_CollectionChanged;
        _packets = _vm?.FilteredPacketLogs;
        if (_packets is not null)
            _packets.CollectionChanged += Packets_CollectionChanged;
        UpdateCountText();
    }

    private void Packets_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // After the ListBox has handled the change, so a cleared list also clears the selected count.
        Dispatcher.UIThread.Post(UpdateCountText, DispatcherPriority.Background);
    }

    private void PacketsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateCountText();
    }

    private void UpdateCountText()
    {
        int packets = _packets?.Count ?? 0;
        int selected = PacketsListBox.SelectedItems?.Count ?? 0;
        string text = packets == 1 ? "1 packet" : $"{packets:N0} packets";
        CountText.Text = selected > 0 ? $"{text} · {selected} selected" : text;
        UnselectButton.IsVisible = selected > 0;
    }

    private void UnselectAll_Click(object? sender, RoutedEventArgs e)
    {
        PacketsListBox.UnselectAll();
    }

    private async void PacketsListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.C)
        {
            await CopyAsync();
            e.Handled = true;
        }
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        await CopyAsync();
    }

    // The selected packets, or every shown packet when none are selected.
    private async Task CopyAsync()
    {
        var packets = PacketsListBox.SelectedItems is { Count: > 0 } items
            ? items.Cast<string>()
            : _packets ?? Enumerable.Empty<string>();
        string text = string.Join(Environment.NewLine, packets);
        if (text.Length > 0 && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
