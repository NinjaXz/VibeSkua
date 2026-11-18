using Avalonia.Controls;
using Avalonia.Interactivity;
using Skua.App.Avalonia.ViewModels.HotKeys;
using System;
using System.Linq;

namespace Skua.App.Avalonia.Views;

public partial class HotKeysView : UserControl
{
    public HotKeysView()
    {
        InitializeComponent();
        var autoBox = this.FindControl<AutoCompleteBox>("ActionAutoComplete");
        if (autoBox != null)
        {
            autoBox.ItemFilter = (search, item) =>
            {
                if (string.IsNullOrWhiteSpace(search))
                    return true;
                if (item is HotKeyItemViewModel hk)
                {
                    string combined = $"{hk.Title} {hk.Description} {hk.Binding}";
                    string[] terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    return terms.All(term => combined.Contains(term, StringComparison.OrdinalIgnoreCase));
                }
                return false;
            };
        }
    }

    private void ToggleDropdown_Click(object? sender, RoutedEventArgs e)
    {
        var autoBox = this.FindControl<AutoCompleteBox>("ActionAutoComplete");
        if (autoBox != null)
        {
            // The browse button should always reveal the complete action list, not the
            // previous search result set (which may be empty).
            autoBox.Text = string.Empty;
            autoBox.MinimumPrefixLength = 0;
            autoBox.IsDropDownOpen = true;
            autoBox.Focus();
        }
    }
}
