using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Linq;
using Skua.Shared.Avalonia.ViewModels.Options;

namespace Skua.Shared.Avalonia.Controls.Options;

// One option row for the options windows: title (and description) on the left, its control on the right. Text
// options stack the field under the title. The row's border and padding come from the list (Border.option-row).
public partial class OptionItemControl : UserControl
{
    private const double StandardOptionHeight = 32;
    private const double NumberFieldWidth = 124;

    // Text options inline at a fixed width instead of stacked under the title (for grids of equal-height cells)
    public static readonly StyledProperty<bool> CompactTextProperty =
        AvaloniaProperty.Register<OptionItemControl, bool>(nameof(CompactText));

    public bool CompactText
    {
        get => GetValue(CompactTextProperty);
        set => SetValue(CompactTextProperty, value);
    }

    public OptionItemControl()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RebuildContent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CompactTextProperty)
            RebuildContent();
    }

    private void RebuildContent()
    {
        if (DataContext is not DisplayOptionItemViewModelBase vm)
        {
            OptionHost.Content = null;
            return;
        }

        OptionHost.Content = CreateDefaultContent(vm, CompactText);
    }

    public static Control CreateDefaultContent(DisplayOptionItemViewModelBase vm, bool compactText = false)
    {
        if (vm is CBOBoolChoiceOptionItemViewModel boolChoiceVm)
            return BuildCBOBoolChoiceItem(boolChoiceVm, compactText);

        if (vm is CBOChoiceOptionItemViewModel choiceVm)
            return BuildCBOChoiceItem(choiceVm, compactText);

        return vm.Tag switch
        {
            "1" => BuildBooleanItem(vm, compactText),
            "2" => BuildTextItem(vm, compactText),
            "3" => BuildNumberItem(vm, compactText),
            "4" => BuildActionItem(vm, compactText),
            _ => BuildFallbackItem(vm, compactText)
        };
    }

    // Title line (with a "restart" chip when the option needs one) and the description, when it says more than the title
    private static Control BuildLabel(DisplayOptionItemViewModelBase vm, bool compactText = false)
    {
        StackPanel label = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };

        StackPanel titleLine = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        titleLine.Children.Add(new TextBlock { Text = vm.Content, Classes = { "option-title" } });
        if (vm.RequiresRestart)
        {
            Border chip = new() { Classes = { "chip", "warning" }, Child = new TextBlock { Text = "restart" } };
            ToolTip.SetTip(chip, "Takes effect after a restart");
            titleLine.Children.Add(chip);
        }
        label.Children.Add(titleLine);

        if (!compactText && !string.IsNullOrWhiteSpace(vm.Description) && vm.Description != vm.Content)
            label.Children.Add(new TextBlock { Text = vm.Description, Classes = { "option-description" } });

        if (!string.IsNullOrWhiteSpace(vm.Description))
            ToolTip.SetTip(label, vm.Description);

        return label;
    }

    private static Grid BuildRow(DisplayOptionItemViewModelBase vm, Control control, bool compactText = false)
    {
        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        Control label = BuildLabel(vm, compactText);
        grid.Children.Add(label);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        Grid.SetColumn(control, 1);
        if (!string.IsNullOrWhiteSpace(vm.Description))
            ToolTip.SetTip(grid, vm.Description);
        return grid;
    }

    private static Control BuildCBOBoolChoiceItem(CBOBoolChoiceOptionItemViewModel vm, bool compactText = false)
    {
        ToggleButton firstButton = new()
        {
            Content = vm.FirstChoice,
            Classes = { "option-chip" },
            Height = StandardOptionHeight
        };

        ToggleButton secondButton = new()
        {
            Content = vm.SecondChoice,
            Classes = { "option-chip" },
            Height = StandardOptionHeight
        };

        // The pair behaves like radio buttons: exactly one is checked, and clicking the checked one keeps it.
        void SyncChoice()
        {
            bool first = vm.Value is true;
            firstButton.IsChecked = first;
            secondButton.IsChecked = !first;
        }
        SyncChoice();
        firstButton.Click += (_, _) => { vm.Value = true; SyncChoice(); };
        secondButton.Click += (_, _) => { vm.Value = false; SyncChoice(); };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DisplayOptionItemViewModelBase.Value))
                SyncChoice();
        };

        StackPanel choices = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
        choices.Children.Add(firstButton);
        choices.Children.Add(secondButton);
        return BuildRow(vm, choices, compactText);
    }

    private static Control BuildCBOChoiceItem(CBOChoiceOptionItemViewModel vm, bool compactText = false)
    {
        ComboBox comboBox = new()
        {
            ItemsSource = vm.Options,
            Width = 160,
            Height = StandardOptionHeight
        };
        comboBox[!SelectingItemsControl.SelectedIndexProperty] = new Binding(nameof(DisplayOptionItemViewModelBase.Value))
        {
            Source = vm,
            Mode = BindingMode.TwoWay
        };

        return BuildRow(vm, comboBox, compactText);
    }

    public static ToggleButton CreateBoundToggleButton(DisplayOptionItemViewModelBase vm, object? content = null)
    {
        ToggleButton toggleButton = new()
        {
            Content = content ?? vm.Content
        };
        BindValue(toggleButton, vm);

        if (vm is CommandOptionItemViewModel commandVm)
        {
            toggleButton.IsCheckedChanged += (_, _) => commandVm.Command.Execute(toggleButton.IsChecked == true);
        }

        return toggleButton;
    }

    public static TextBox CreateBoundTextBox(DisplayOptionItemViewModelBase vm, bool numericOnly = false)
    {
        TextBox textBox = new();
        BindValue(textBox, vm);

        if (numericOnly)
        {
            textBox.TextInput += (_, e) =>
            {
                if (!char.IsDigit(e.Text?[0] ?? '\0'))
                    e.Handled = true;
            };
            textBox.TextChanged += (_, _) =>
            {
                string original = textBox.Text ?? string.Empty;
                string sanitized = SanitizeDigits(original);
                if (!string.Equals(original, sanitized, StringComparison.Ordinal))
                {
                    textBox.Text = sanitized;
                    textBox.CaretIndex = sanitized.Length;
                }
            };
        }

        return textBox;
    }

    public static Button CreateBoundButton(DisplayOptionItemViewModelBase vm, object? content = null, Func<string?>? commandArgumentFactory = null)
    {
        Button button = new()
        {
            Content = content ?? vm.Content
        };
        ToolTip.SetTip(button, vm.Content);
        button.Classes.Add("skua-button");

        if (vm is CommandOptionItemViewModel commandVm)
        {
            if (commandArgumentFactory is null)
            {
                button.Command = commandVm.Command;
            }
            else
            {
                button.Click += (_, _) => commandVm.Command.Execute(commandArgumentFactory());
            }
        }

        return button;
    }

    private static Control BuildBooleanItem(DisplayOptionItemViewModelBase vm, bool compactText = false)
    {
        ToggleSwitch toggle = new()
        {
            OnContent = string.Empty,
            OffContent = string.Empty,
            MinWidth = 0
        };
        BindValue(toggle, vm);
        if (vm is CommandOptionItemViewModel commandVm)
            toggle.IsCheckedChanged += (_, _) => commandVm.Command.Execute(toggle.IsChecked == true);

        return BuildRow(vm, toggle, compactText);
    }

    // Text options: the field gets the row's full width under the title, or sits inline when compact
    private static Control BuildTextItem(DisplayOptionItemViewModelBase vm, bool compact = false)
    {
        Border field = BuildSetField(vm, numeric: false);
        if (compact)
        {
            field.Width = 160;
            return BuildRow(vm, field, compact);
        }
        field.HorizontalAlignment = HorizontalAlignment.Stretch;

        StackPanel panel = new() { Spacing = 6, Margin = new Thickness(0, 3) };
        panel.Children.Add(BuildLabel(vm, compact));
        panel.Children.Add(field);
        return panel;
    }

    private static Control BuildNumberItem(DisplayOptionItemViewModelBase vm, bool compactText = false)
    {
        Border field = BuildSetField(vm, numeric: true);
        field.Width = NumberFieldWidth;
        return BuildRow(vm, field, compactText);
    }

    // A field with Set attached. Typing only edits the field; Set (or Enter) applies it. Set is accent-coloured while
    // the field holds an unapplied edit, and shows a check once applied.
    private static Border BuildSetField(DisplayOptionItemViewModelBase vm, bool numeric)
    {
        TextBox textBox = CreateBoundTextBox(vm, numericOnly: numeric);
        textBox.Classes.Add("option-pill-input");
        if (numeric)
            textBox.TextAlignment = TextAlignment.Right;

        Button set = new() { Classes = { "option-set" }, Content = "Set" };
        ToolTip.SetTip(set, "Apply this value (Enter also applies it)");

        string Current() => vm.Value?.ToString() ?? string.Empty;
        void ShowState(bool applied)
        {
            bool pending = (textBox.Text ?? string.Empty) != Current();
            set.Classes.Set("pending", pending);
            set.Classes.Set("applied", applied && !pending);
            set.Content = applied && !pending ? "✓ Set" : "Set";
        }
        void Apply()
        {
            string text = textBox.Text ?? string.Empty;
            object? value = text;
            if (numeric)
            {
                try
                {
                    value = Convert.ChangeType(text, vm.DisplayType);
                }
                catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
                {
                    // Not a number this option can hold: put the current value back
                    textBox.Text = Current();
                    ShowState(applied: false);
                    return;
                }
            }

            vm.Value = value;
            if (vm is CommandOptionItemViewModel commandVm)
                commandVm.Command.Execute(text);
            textBox.Text = Current();
            ShowState(applied: true);
        }
        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Apply();
                e.Handled = true;
            }
        };
        textBox.TextChanged += (_, _) => ShowState(applied: set.Classes.Contains("applied"));
        set.Click += (_, _) => Apply();

        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        grid.Children.Add(textBox);
        if (!string.IsNullOrWhiteSpace(vm.SuffixText))
        {
            TextBlock suffix = new()
            {
                Text = vm.SuffixText,
                Classes = { "option-pill-suffix" },
                Margin = new Thickness(0, 0, 8, 0)
            };
            grid.Children.Add(suffix);
            Grid.SetColumn(suffix, 1);
        }
        grid.Children.Add(set);
        Grid.SetColumn(set, 2);

        return new Border { Classes = { "option-field" }, Child = grid };
    }

    private static Control BuildActionItem(DisplayOptionItemViewModelBase vm, bool compactText = false)
    {
        Button button = CreateBoundButton(vm, vm.ActionText ?? "Run");
        button.Classes.Add("small");
        button.Classes.Add("w84");
        return BuildRow(vm, button, compactText);
    }

    private static Control BuildFallbackItem(DisplayOptionItemViewModelBase vm, bool compactText)
    {
        if (vm.DisplayType == typeof(bool))
            return BuildBooleanItem(vm);

        if (vm.DisplayType == typeof(string))
            return BuildTextItem(vm, compactText);

        if (IsNumericDisplayType(vm.DisplayType))
            return BuildNumberItem(vm);

        return BuildActionItem(vm);
    }

    // One way: an edit reaches the option only through Set (BuildSetField)
    private static void BindValue(TextBox textBox, DisplayOptionItemViewModelBase vm)
    {
        textBox[!TextBox.TextProperty] = new Binding(nameof(DisplayOptionItemViewModelBase.Value))
        {
            Source = vm,
            Mode = BindingMode.OneWay
        };
        textBox.GotFocus += (_, _) => textBox.SelectAll();
    }

    private static void BindValue(ToggleButton toggleButton, DisplayOptionItemViewModelBase vm)
    {
        toggleButton[!ToggleButton.IsCheckedProperty] = new Binding(nameof(DisplayOptionItemViewModelBase.Value))
        {
            Source = vm,
            Mode = BindingMode.TwoWay
        };
    }

    private static bool IsNumericDisplayType(Type displayType)
    {
        return displayType == typeof(byte)
            || displayType == typeof(sbyte)
            || displayType == typeof(short)
            || displayType == typeof(ushort)
            || displayType == typeof(int)
            || displayType == typeof(uint)
            || displayType == typeof(long)
            || displayType == typeof(ulong);
    }

    private static string SanitizeDigits(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return new string(value.Where(char.IsDigit).ToArray());
    }
}
