using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Skua.App.Avalonia.ViewModels.AppLogs;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.UserControls;

public partial class LogTabUserControl : UserControl
{
    // The tab whose Logs we listen to. The view is reused across log tabs, so the old tab has to be
    // unsubscribed when the DataContext changes, not the new one.
    private LogTabViewModel? _subscribed;

    // For narrow hosts (the Script Loader): wrap long lines, and drop the copy hint that doesn't fit
    public static readonly StyledProperty<bool> WrapLinesProperty =
        AvaloniaProperty.Register<LogTabUserControl, bool>(nameof(WrapLines));

    public static readonly StyledProperty<bool> ShowCopyHintProperty =
        AvaloniaProperty.Register<LogTabUserControl, bool>(nameof(ShowCopyHint), defaultValue: true);

    public bool WrapLines
    {
        get => GetValue(WrapLinesProperty);
        set => SetValue(WrapLinesProperty, value);
    }

    public bool ShowCopyHint
    {
        get => GetValue(ShowCopyHintProperty);
        set => SetValue(ShowCopyHintProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WrapLinesProperty)
            LogsListBox.Classes.Set("wrap", WrapLines);
        else if (change.Property == ShowCopyHintProperty)
            CopyHint.IsVisible = ShowCopyHint;
    }

    public LogTabUserControl()
    {
        InitializeComponent();
        this.AttachedToVisualTree += (_, _) => Subscribe(DataContext as LogTabViewModel);
        this.DetachedFromVisualTree += (_, _) => Subscribe(null);
        this.DataContextChanged += (_, _) =>
        {
            if (this.IsAttachedToVisualTree())
                Subscribe(DataContext as LogTabViewModel);
        };
    }

    private void Subscribe(LogTabViewModel? vm)
    {
        if (ReferenceEquals(_subscribed, vm))
            return;

        if (_subscribed is not null)
            _subscribed.Logs.CollectionChanged -= Logs_CollectionChanged;

        _subscribed = vm;
        if (vm is null)
        {
            UpdateCountText();
            return;
        }

        vm.Logs.CollectionChanged += Logs_CollectionChanged;
        UpdateCountText();
        if (vm.Logs.Count > 0)
            Dispatcher.UIThread.Post(() => ScrollToEnd(vm), DispatcherPriority.Loaded);
    }

    private void Logs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // After the ListBox has handled the change, so a cleared log also clears the selected count.
        Dispatcher.UIThread.Post(UpdateCountText, DispatcherPriority.Background);
        LogTabViewModel? vm = _subscribed;
        if (vm is null || vm.Logs.Count == 0)
            return;

        // Follow new lines only while the list is already at the bottom, so older lines stay readable
        // while a script is logging. Layout hasn't run yet, so these are the pre-insert sizes.
        if (LogsListBox.Scroll is ScrollViewer scroll && scroll.Extent.Height > scroll.Viewport.Height
            && scroll.Offset.Y + scroll.Viewport.Height < scroll.Extent.Height - 24)
            return;

        Dispatcher.UIThread.Post(() => ScrollToEnd(vm));
    }

    private void ScrollToEnd(LogTabViewModel vm)
    {
        if (ReferenceEquals(_subscribed, vm) && vm.Logs.Count > 0)
            LogsListBox.ScrollIntoView(vm.Logs.Count - 1);
    }

    private async void LogsListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.C)
        {
            await CopySelectedLogsAsync();
            e.Handled = true;
        }
    }

    // Called by the Copy button on the Logs tab row: the selected lines, or the whole log when none are selected.
    public async Task CopySelectedOrAllAsync()
    {
        if (LogsListBox.SelectedItems is { Count: > 0 })
            await CopySelectedLogsAsync();
        else if (DataContext is LogTabViewModel vm)
            vm.CopyLogCommand.Execute(null);
    }

    public void DeselectAll()
    {
        LogsListBox.SelectedItems?.Clear();
    }

    private void LogsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateCountText();
    }

    private void UpdateCountText()
    {
        int lines = _subscribed?.Logs.Count ?? 0;
        int selected = LogsListBox.SelectedItems?.Count ?? 0;
        string text = lines == 1 ? "1 line" : $"{lines:N0} lines";
        CountText.Text = selected > 0 ? $"{text} · {selected} selected" : text;
    }

    private async void CopySelected_Click(object? sender, RoutedEventArgs e)
    {
        await CopySelectedLogsAsync();
    }

    // Every copy handler is async void, so a clipboard failure (another app holding it open) is caught here and
    // shown in the count line instead of escaping as an unhandled UI exception.
    private async Task CopySelectedLogsAsync()
    {
        if (LogsListBox.SelectedItems is { Count: > 0 } items)
        {
            string text = string.Join(Environment.NewLine, items.Cast<object>().Select(x => x.ToString()));
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                try
                {
                    await clipboard.SetTextAsync(text);
                    UpdateCountText();
                }
                catch (Exception)
                {
                    CountText.Text = "Couldn't copy: the clipboard is busy, try again";
                }
            }
        }
    }
}
