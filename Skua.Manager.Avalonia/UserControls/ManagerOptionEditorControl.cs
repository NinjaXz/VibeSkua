using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Skua.Shared.Avalonia.ViewModels.Options;
using System;
using System.Globalization;

namespace Skua.Manager.Avalonia.UserControls;

/// <summary>
/// Displays the correct editor for a Manager option and executes its command when the value changes.
/// </summary>
public sealed class ManagerOptionEditorControl : ContentControl
{
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        RebuildEditor();
    }

    private void RebuildEditor()
    {
        if (DataContext is not CommandOptionItemViewModel option)
        {
            Content = null;
            return;
        }

        if (option.DisplayType == typeof(bool))
        {
            ToggleSwitch toggle = new()
            {
                IsChecked = option.Value is bool value && value,
                OnContent = string.Empty,
                OffContent = string.Empty,
                VerticalAlignment = VerticalAlignment.Center
            };
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (toggle.IsChecked is bool value && option.Command.CanExecute(value))
                    option.Command.Execute(value);
            };
            Content = toggle;
            return;
        }

        if (option.DisplayType == typeof(int))
        {
            bool isPercentage = option.Tag == "ServerLatencyNearThresholdPercent";
            int minimum = isPercentage ? 50 : 50;
            int maximum = isPercentage ? 95 : 2000;
            string suffix = isPercentage ? "%" : "ms";

            TextBox input = new()
            {
                Text = Convert.ToString(option.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                Watermark = $"{minimum}–{maximum}",
                Width = 104,
                Height = 34,
                Padding = new Thickness(8, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                ToolTip = $"Allowed range: {minimum} to {maximum} {suffix}"
            };
            input.TextChanged += (_, _) =>
            {
                if (!input.IsKeyboardFocusWithin ||
                    !int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
                    value < minimum || value > maximum)
                    return;

                string commandValue = value.ToString(CultureInfo.InvariantCulture);
                if (option.Command.CanExecute(commandValue))
                    option.Command.Execute(commandValue);
            };
            input.LostFocus += (_, _) =>
            {
                if (!int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
                    value < minimum || value > maximum)
                    input.Text = Convert.ToString(option.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            };

            StackPanel editor = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    input,
                    new TextBlock
                    {
                        Text = suffix,
                        MinWidth = 22,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = (IBrush?)Application.Current?.FindResource("SkuaMutedTextBrush")
                    }
                }
            };
            Content = editor;
            return;
        }

        Content = null;
    }
}
