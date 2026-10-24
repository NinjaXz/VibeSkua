using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Skua.Shared.Avalonia.ViewModels.Options;
using System;

namespace Skua.Shared.Avalonia.Controls.Options;

public class OptionValueEditorControl : ContentControl
{
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        RebuildEditor();
    }

    private void RebuildEditor()
    {
        if (DataContext is not OptionContainerItemViewModel item)
        {
            Content = null;
            return;
        }

        if (item.IsBool)
        {
            CheckBox checkBox = new()
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            checkBox[!CheckBox.IsCheckedProperty] = new Binding(nameof(OptionContainerItemViewModel.BoolValue))
            {
                Mode = BindingMode.TwoWay
            };
            Content = checkBox;
        }
        else if (item.IsEnum && item.Option.Searchable)
        {
            // Options that ask for it (IOption.Searchable): type any part of a name to filter.
            SearchComboBox search = new()
            {
                ItemsSource = item.EnumValues,
                MinWidth = 160,
                MaxWidth = 220,
                Height = 32,
                Watermark = "Type to search",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            search[!SearchComboBox.ValueProperty] = new Binding(nameof(OptionContainerItemViewModel.SelectedValue))
            {
                Mode = BindingMode.TwoWay
            };
            ToolTip.SetTip(search, "Type any part of a name to filter the list");
            Content = search;
        }
        else if (item.IsEnum)
        {
            ComboBox comboBox = new()
            {
                ItemsSource = item.EnumValues,
                MinWidth = 160,
                MaxWidth = 220,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            comboBox[!ComboBox.SelectedItemProperty] = new Binding(nameof(OptionContainerItemViewModel.SelectedValue))
            {
                Mode = BindingMode.TwoWay
            };
            Content = comboBox;
        }
        else
        {
            TextBox textBox = new()
            {
                MinWidth = 140,
                MaxWidth = 220,
                Height = 32,
                Padding = new Thickness(8, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            textBox[!TextBox.TextProperty] = new Binding(nameof(OptionContainerItemViewModel.TextValue))
            {
                Mode = BindingMode.TwoWay
            };
            Content = textBox;
        }
    }
}
