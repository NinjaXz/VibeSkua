using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Skua.Core.Interfaces;
using Skua.Shared.Avalonia.Controls.Options;

namespace ButlerV5;

/// <summary>Native Avalonia settings and live-log window.</summary>
public sealed class SettingsWindow : Window
{
    private readonly ButlerV5Plugin _plugin;
    private readonly TextBlock _descTitle;
    private readonly TextBlock _descBody;
    private readonly CheckBox _followTail;
    private readonly StackPanel _logRows;
    private readonly ScrollViewer _logScroll;
    private readonly DispatcherTimer _logTimer;
    private readonly List<LogLine> _renderedLogs = new();
    private readonly List<TextBlock> _renderedLogControls = new();
    private Border? _selectedRow;

    public SettingsWindow(ButlerV5Plugin plugin)
    {
        _plugin = plugin;
        Title = "ButlerV5 Avalonia - Settings";
        Width = 900;
        Height = 630;
        MinWidth = 720;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AvaloniaUi.PrepareWindow(this);

        Grid root = new()
        {
            Margin = new Thickness(12),
            ColumnDefinitions = new ColumnDefinitions("2*,6,3*"),
        };

        Grid left = new() { RowDefinitions = new RowDefinitions("2*,*") };
        StackPanel settingsPanel = new();
        IEnumerable<IOption> options = _plugin.OptionContainer?.Options ?? Enumerable.Empty<IOption>();
        bool first = true;
        foreach (IOption option in options)
        {
            if (!first)
                settingsPanel.Children.Add(MakeSeparator());
            first = false;
            settingsPanel.Children.Add(BuildSettingRow(option));
        }

        Border settingsBox = Panel(new ScrollViewer
        {
            Content = settingsPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        settingsBox.Margin = new Thickness(0, 0, 0, 6);
        left.Children.Add(settingsBox);

        StackPanel description = new();
        _descTitle = AvaloniaUi.TextBlock(14);
        _descTitle.FontWeight = FontWeight.SemiBold;
        _descTitle.Text = "Click a setting";
        _descBody = AvaloniaUi.TextBlock(12, AvaloniaUi.SubText);
        _descBody.TextWrapping = TextWrapping.Wrap;
        _descBody.Margin = new Thickness(0, 6, 0, 0);
        _descBody.Text = "Its description appears here. Changes save immediately and other logged-in butlers reload them automatically.";
        description.Children.Add(_descTitle);
        description.Children.Add(_descBody);

        Version? version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        TextBlock meta = AvaloniaUi.TextBlock(11, AvaloniaUi.SubText);
        meta.Margin = new Thickness(0, 16, 0, 0);
        meta.Text = $"Author: l0newolf12\nVersion: {(version == null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}")}";
        description.Children.Add(meta);

        Border descriptionBox = Panel(new ScrollViewer
        {
            Content = description,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        descriptionBox.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(descriptionBox, 1);
        left.Children.Add(descriptionBox);
        root.Children.Add(left);

        GridSplitter splitter = new()
        {
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
        };
        Grid.SetColumn(splitter, 1);
        root.Children.Add(splitter);

        Grid right = new()
        {
            Margin = new Thickness(8, 0, 0, 0),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };
        Grid logHeader = new()
        {
            Margin = new Thickness(0, 0, 0, 6),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        TextBlock logTitle = AvaloniaUi.TextBlock(13);
        logTitle.FontWeight = FontWeight.SemiBold;
        logTitle.Text = $"Logs - {_plugin.OwnUsername ?? "this account"}";
        logHeader.Children.Add(logTitle);
        _followTail = new CheckBox
        {
            Content = "Follow tail",
            IsChecked = true,
            Foreground = AvaloniaUi.SubText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_followTail, 1);
        logHeader.Children.Add(_followTail);
        right.Children.Add(logHeader);

        _logRows = new StackPanel { Margin = new Thickness(8, 6) };
        _logScroll = new ScrollViewer
        {
            Content = _logRows,
            Background = AvaloniaUi.PanelRaised,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Border logBox = new()
        {
            Child = _logScroll,
            BorderBrush = AvaloniaUi.Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };
        Grid.SetRow(logBox, 1);
        right.Children.Add(logBox);

        Grid logButtons = new()
        {
            Margin = new Thickness(0, 6, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        StackPanel leftButtons = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        leftButtons.Children.Add(SmallButton("Copy", "Copy all log lines to the clipboard.", CopyLog));
        leftButtons.Children.Add(SmallButton("Save", "Write the current log to a timestamped file.", SaveLog));
        leftButtons.Children.Add(SmallButton("Clear", "Clear the in-memory log view.", ClearLog));
        logButtons.Children.Add(leftButtons);
        Button openFolder = SmallButton("Open folder", "Open the ButlerV5 log folder.", OpenLogFolder);
        Grid.SetColumn(openFolder, 1);
        logButtons.Children.Add(openFolder);
        Grid.SetRow(logButtons, 2);
        right.Children.Add(logButtons);

        Grid.SetColumn(right, 2);
        root.Children.Add(right);
        Content = root;

        RefreshLog();
        _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _logTimer.Tick += (_, _) => RefreshLog();
        _logTimer.Start();
        Closed += (_, _) => _logTimer.Stop();
    }

    private static Border Panel(Control child) => new()
    {
        Child = child,
        Background = AvaloniaUi.PanelRaised,
        BorderBrush = AvaloniaUi.Divider,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(8, 6),
    };

    private Button SmallButton(string text, string tooltip, Action action)
    {
        Button button = AvaloniaUi.Button(text, action);
        button.Padding = new Thickness(10, 4);
        AvaloniaUi.SetToolTip(button, tooltip);
        return button;
    }

    private Border BuildSettingRow(IOption option)
    {
        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        TextBlock label = AvaloniaUi.TextBlock(13);
        label.Text = option.DisplayName;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextWrapping = TextWrapping.Wrap;
        label.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(label);

        Control control = BuildControl(option);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.GotFocus += (_, _) => SelectOption(option);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        Border row = new()
        {
            Child = grid,
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
        };
        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
                SelectOption(option, row);
        };
        return row;
    }

    private static Border MakeSeparator() => new()
    {
        Height = 1,
        Margin = new Thickness(4, 1),
        Background = AvaloniaUi.Divider,
    };

    private Control BuildControl(IOption option)
    {
        IOptionContainer container = _plugin.OptionContainer!;
        string raw = SafeGetDirect(container, option);

        if (option.Type == typeof(bool))
        {
            bool restoring = false;
            CheckBox checkBox = new()
            {
                IsChecked = raw.Equals("True", StringComparison.OrdinalIgnoreCase) || raw == "1",
            };
            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (restoring)
                    return;
                if (checkBox.IsChecked == true)
                {
                    _plugin.SaveSetting(option.Name, true);
                }
                else if (option.Name != "enabled" || _plugin.ConfirmDisablePlugin())
                {
                    _plugin.SaveSetting(option.Name, false);
                }
                else
                {
                    restoring = true;
                    checkBox.IsChecked = true;
                    restoring = false;
                }
            };
            return checkBox;
        }

        if (option.Type.IsEnum && option.Searchable)
        {
            // As the script options window: a dropdown that filters as you type (IOption.Searchable).
            object current = (Enum.TryParse(option.Type, raw, ignoreCase: false, out object? exact) ? exact : null)
                ?? TryParseEnum(option.Type, raw) ?? option.DefaultValue ?? Enum.GetValues(option.Type).GetValue(0)!;
            SearchComboBox search = new()
            {
                MinWidth = 160,
                ItemsSource = Enum.GetNames(option.Type),
                Value = Enum.GetName(option.Type, current) ?? current.ToString(),
            };
            search.PropertyChanged += (_, e) =>
            {
                // Exact case: the list's names, and enums may have names that differ only in case.
                if (e.Property == SearchComboBox.ValueProperty && search.Value is { } name
                    && Enum.TryParse(option.Type, name, ignoreCase: false, out object? value))
                    _plugin.SaveSetting(option.Name, value!);
            };
            return search;
        }

        if (option.Type.IsEnum)
        {
            List<object> values = Enum.GetValues(option.Type).Cast<object>().ToList();
            ComboBox combo = new()
            {
                MinWidth = 125,
                ItemsSource = values,
                SelectedItem = values.FirstOrDefault(value => value.Equals(TryParseEnum(option.Type, raw) ?? option.DefaultValue))
                    ?? values.FirstOrDefault(),
            };
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem != null)
                    _plugin.SaveSetting(option.Name, combo.SelectedItem);
            };
            return combo;
        }

        bool isInt = option.Type == typeof(int);
        TextBox textBox = new()
        {
            Text = raw,
            MinWidth = isInt ? 85 : 160,
            MaxWidth = 240,
        };
        textBox.LostFocus += (_, _) =>
        {
            string value = textBox.Text ?? "";
            if (isInt)
            {
                if (int.TryParse(value.Trim(), out int number))
                    _plugin.SaveSetting(option.Name, number);
                else
                    textBox.Text = SafeGetDirect(container, option);
            }
            else
            {
                _plugin.SaveSetting(option.Name, value);
            }
        };
        return textBox;
    }

    private void SelectOption(IOption option, Border? row = null)
    {
        _descTitle.Text = option.DisplayName;
        _descBody.Text = string.IsNullOrWhiteSpace(option.Description) ? "(no description)" : option.Description;
        if (row == null)
            return;
        if (_selectedRow != null)
            _selectedRow.Background = Brushes.Transparent;
        row.Background = AvaloniaUi.Divider;
        _selectedRow = row;
    }

    private void RefreshLog()
    {
        try
        {
            List<LogLine> snapshot = DebugLog.Snapshot();
            if (snapshot.Count == _renderedLogs.Count &&
                (snapshot.Count == 0 || (snapshot[^1].Seq == _renderedLogs[^1].Seq && snapshot[^1].Text == _renderedLogs[^1].Text)))
                return;

            bool canAppend = _renderedLogs.Count <= snapshot.Count;
            for (int index = 0; canAppend && index < _renderedLogs.Count; index++)
                canAppend = _renderedLogs[index].Seq == snapshot[index].Seq;

            if (!canAppend)
            {
                _renderedLogs.Clear();
                _renderedLogControls.Clear();
                _logRows.Children.Clear();
            }

            int sharedCount = _renderedLogs.Count;
            for (int index = 0; index < sharedCount; index++)
            {
                if (_renderedLogs[index].Text == snapshot[index].Text)
                    continue;
                _renderedLogs[index].Text = snapshot[index].Text;
                _renderedLogControls[index].Text = snapshot[index].Text;
            }

            for (int index = sharedCount; index < snapshot.Count; index++)
            {
                LogLine copy = snapshot[index].Copy();
                TextBlock line = new()
                {
                    Text = copy.Text,
                    Foreground = LogBrush(copy.Level),
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.NoWrap,
                };
                _renderedLogs.Add(copy);
                _renderedLogControls.Add(line);
                _logRows.Children.Add(line);
            }

            if (_followTail.IsChecked == true && _logRows.Children.Count > 0)
                _logRows.Children[^1].BringIntoView();
        }
        catch
        {
        }
    }

    private static IBrush LogBrush(LogLevel level) => level switch
    {
        LogLevel.Success => AvaloniaUi.Green,
        LogLevel.Warning => AvaloniaUi.Orange,
        LogLevel.Error => AvaloniaUi.Red,
        _ => AvaloniaUi.SubText,
    };

    private async void CopyLog()
    {
        try
        {
            string text = string.Join(Environment.NewLine, _renderedLogs.Select(entry => entry.Text));
            if (text.Length == 0)
                return;
            IClipboard? clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
                throw new InvalidOperationException("The clipboard is not available.");
            await clipboard.SetTextAsync(text);
            DebugLog.Log("UI", $"copied {_renderedLogs.Count} log lines to clipboard", LogLevel.Success);
        }
        catch (Exception ex)
        {
            DebugLog.Log("UI", $"copy failed: {ex.Message}", LogLevel.Error);
        }
    }

    private void SaveLog()
    {
        string? path = DebugLog.SaveExport(_plugin.OwnUsername);
        DebugLog.Log("UI", path != null ? $"saved log to {Path.GetFileName(path)}" : "save log failed",
            path != null ? LogLevel.Success : LogLevel.Error);
    }

    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(DebugLog.LogDir);
            Process.Start(new ProcessStartInfo { FileName = DebugLog.LogDir, UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            DebugLog.Log("UI", $"open folder failed: {ex.Message}", LogLevel.Error);
        }
    }

    private void ClearLog()
    {
        DebugLog.ClearMemory();
        _renderedLogs.Clear();
        _renderedLogControls.Clear();
        _logRows.Children.Clear();
    }

    private static string SafeGetDirect(IOptionContainer container, IOption option)
    {
        try { return container.GetDirect(option) ?? option.DefaultValue?.ToString() ?? ""; }
        catch { return option.DefaultValue?.ToString() ?? ""; }
    }

    private static object? TryParseEnum(Type type, string raw)
    {
        try { return Enum.Parse(type, raw, ignoreCase: true); }
        catch { return null; }
    }
}
