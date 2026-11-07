using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace Skua.Shared.Avalonia.ViewModels.Options;

public class OptionCategoryGroup
{
    public string Category { get; set; } = "Options";
    public List<OptionContainerItemViewModel> Items { get; set; } = [];
}

public partial class OptionContainerViewModel : ObservableObject, IOptionContainerViewModel
{
    public OptionContainerViewModel(IOptionContainer container)
    {
        Container = container;
        Options = [];
        foreach (IOption option in container.Options)
            Options.Add(new OptionContainerItemViewModel(container, option));

        if (container.MultipleOptions.Count > 0)
        {
            foreach (List<IOption> optionList in container.MultipleOptions.Values)
            {
                foreach (IOption option in optionList)
                    Options.Add(new OptionContainerItemViewModel(container, option));
            }
        }

        GroupedOptions = Options
            .OfType<OptionContainerItemViewModel>()
            .GroupBy(o => string.IsNullOrWhiteSpace(o.Category) ? "Options" : o.Category)
            .Select(g => new OptionCategoryGroup
            {
                Category = g.Key,
                Items = g.ToList()
            })
            .ToList();

        SelectedOption = Options.FirstOrDefault();

        if (container.ResetNotices.Count > 0)
            ResetNotice = "Saved choices that no longer exist were reset to their defaults. Review them and press Save to keep the change.\n"
                + string.Join("\n", container.ResetNotices);
    }

    public string Title { get; } = "Script Options";
    public string? ResetNotice { get; }
    public IOptionContainer Container { get; set; }
    public List<IOptionContainerItemViewModel> Options { get; }
    public List<OptionCategoryGroup> GroupedOptions { get; }

    [ObservableProperty]
    private IOptionContainerItemViewModel? _selectedOption;

    public void ResetAllToDefaults()
    {
        foreach (var opt in Options)
        {
            if (opt is OptionContainerItemViewModel item)
                item.ResetToDefault();
        }
    }
}
