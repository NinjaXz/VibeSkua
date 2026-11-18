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

    private void ActionAutoComplete_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is AutoCompleteBox autoBox)
            autoBox.IsDropDownOpen = true;
    }
}
