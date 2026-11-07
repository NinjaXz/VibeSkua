using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using Skua.Shared.Avalonia.ViewModels.Options;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Skua.App.Avalonia.ViewModels.CoreBotsOptions;

public class CBOptionsViewModel : ObservableObject, IManageCBOptions
{
    private readonly IDialogService _dialogService;
    private string _searchText = string.Empty;

    public CBOptionsViewModel(List<DisplayOptionItemViewModelBase> options, IDialogService dialogService)
    {
        Options = options;
        _dialogService = dialogService;
        DefaultValues = new();
        foreach (DisplayOptionItemViewModelBase option in Options)
            DefaultValues.Add(option.Tag, option.Value!);

        FilteredOptions = new ObservableCollection<DisplayOptionItemViewModelBase>(SortOptions(Options));
    }

    public List<DisplayOptionItemViewModelBase> Options { get; }
    public ObservableCollection<DisplayOptionItemViewModelBase> FilteredOptions { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplySearch();
        }
    }

    private Dictionary<string, object> DefaultValues { get; }

    public StringBuilder Save(StringBuilder builder)
    {
        foreach (DisplayOptionItemViewModelBase option in Options)
        {
            if (option.Tag == "PrivateRooms" && !(bool)option.Value && _dialogService.ShowMessageBox("Whilst we do offer the option, we highly recommend staying in private rooms while botting. Bot in public at your own risk.\r\n Confirm the use of Public Rooms?", "Public Room Warning", true) == false)
            {
                builder.AppendLine($"{option.Tag}: {true}");
                continue;
            }

            if (option.Tag == "PrivateRoomNr" && long.TryParse(option.Value?.ToString(), out long room) && room > int.MaxValue)
            {
                if (_dialogService.ShowMessageBox($"Private room number cannot be greater than {int.MaxValue}. It will be reset to 100000.", "Room Number Warning", true) == false)
                {
                    continue;
                }
                option.Value = 100000;
            }
            builder.AppendLine($"{option.Tag}: {option.Value}");
        }

        return builder;
    }

    public void SetValues(Dictionary<string, string> values)
    {
        foreach (DisplayOptionItemViewModelBase option in Options)
        {
            if (values.TryGetValue(option.Tag, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                try
                {
                    option.Value = option.DisplayType == typeof(int)
                        ? long.TryParse(value, out long longValue) && longValue is >= int.MinValue and <= int.MaxValue
                            ? (int)longValue
                            : DefaultValues[option.Tag]
                        : Convert.ChangeType(value, option.DisplayType);
                }
                catch (Exception)
                {
                    option.Value = DefaultValues[option.Tag];
                }
            }
            else
            {
                option.Value = DefaultValues[option.Tag];
            }
        }
        ApplySearch();
    }

    private void ApplySearch()
    {
        IEnumerable<DisplayOptionItemViewModelBase> filtered = Options.Where(MatchesSearch);
        List<DisplayOptionItemViewModelBase> sorted = SortOptions(filtered).ToList();
        FilteredOptions.Clear();
        foreach (DisplayOptionItemViewModelBase option in sorted)
            FilteredOptions.Add(option);
    }

    private bool MatchesSearch(DisplayOptionItemViewModelBase option)
    {
        string trimmed = SearchText.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return true;

        return option.Content?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) == true
            || (!string.IsNullOrEmpty(option.Description) && option.Description.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<DisplayOptionItemViewModelBase> SortOptions(IEnumerable<DisplayOptionItemViewModelBase> options)
    {
        return options
            .OrderBy(o => o.DisplayType == typeof(bool) ? 0 : o.DisplayType == typeof(int) ? 1 : 2)
            .ThenBy(o => o.Content, StringComparer.OrdinalIgnoreCase);
    }
}