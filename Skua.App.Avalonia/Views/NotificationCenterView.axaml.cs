using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Skua.App.Avalonia.ViewModels;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Views;

/// <summary>
/// Copy and Deselect work on the list's selection, so they live here like the Logs window's; Save and Clear are
/// view-model commands.
/// </summary>
public partial class NotificationCenterView : UserControl
{
    private NotificationCenterViewModel? _vm;

    public NotificationCenterView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as NotificationCenterViewModel);
        AttachedToVisualTree += (_, _) => Watch(DataContext as NotificationCenterViewModel);
        DetachedFromVisualTree += (_, _) => Watch(null);
    }

    private void Watch(NotificationCenterViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm))
            return;
        if (_vm is not null)
            _vm.FilteredNotifications.CollectionChanged -= OnEntriesChanged;
        _vm = vm;
        if (_vm is not null)
            _vm.FilteredNotifications.CollectionChanged += OnEntriesChanged;
        UpdateCountText();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateCountText();

    private void FeedList_SelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateCountText();

    private async void FeedList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.C)
        {
            e.Handled = true;
            await CopyAsync();
        }
    }

    private async void CopyButton_Click(object? sender, RoutedEventArgs e) => await CopyAsync();

    private void DeselectButton_Click(object? sender, RoutedEventArgs e) => FeedList.SelectedItems?.Clear();

    // The selected events, or every shown event when none are selected. A busy clipboard (another app holding it)
    // shows in the count line instead of escaping this async void handler.
    private async Task CopyAsync()
    {
        if (_vm is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        NotificationEntry[] entries = FeedList.SelectedItems is { Count: > 0 } selected
            ? selected.OfType<NotificationEntry>().ToArray()
            : _vm.FilteredNotifications.ToArray();
        if (entries.Length == 0)
            return;

        try
        {
            await clipboard.SetTextAsync(NotificationCenterViewModel.ToText(entries));
            CountText.Text = entries.Length == 1 ? "Copied 1 event" : $"Copied {entries.Length:N0} events";
        }
        catch (Exception)
        {
            CountText.Text = "Couldn't copy: the clipboard is busy, try again";
        }
    }

    private void UpdateCountText()
    {
        int shown = _vm?.FilteredNotifications.Count ?? 0;
        int selected = FeedList.SelectedItems?.Count ?? 0;
        string text = shown == 1 ? "1 event" : $"{shown:N0} events";
        CountText.Text = selected > 0 ? $"{text} · {selected} selected" : text;
    }
}
