using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Shared.Avalonia.ViewModels;
using Skua.Shared.Avalonia.ViewModels.Options;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Skua.App.Avalonia.ViewModels.Options;

// A section of an options window: a chip in the chip row and, under "All", a header over its rows
public class OptionGroupViewModel
{
    public string Title { get; set; } = string.Empty;
    public string ShortTitle { get; set; } = string.Empty;
    public string Icon { get; set; } = "Cog";
    public string Description { get; set; } = string.Empty;
    public List<DisplayOptionItemViewModelBase> Items { get; set; } = new();
    public bool IsAll { get; private init; }
    public int Count => Items.Count;

    public static OptionGroupViewModel CreateAll(IEnumerable<OptionGroupViewModel> groups) => new()
    {
        Title = "All",
        ShortTitle = "All",
        Icon = "Apps",
        Items = groups.SelectMany(g => g.Items).ToList(),
        IsAll = true
    };

    // The groups with only the items whose title or description contains the search text; empty groups dropped
    public static IEnumerable<OptionGroupViewModel> Filter(IEnumerable<OptionGroupViewModel> groups, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return groups;

        string text = search.Trim();
        return groups.Select(g => new OptionGroupViewModel
        {
            Title = g.Title,
            ShortTitle = g.ShortTitle,
            Icon = g.Icon,
            Description = g.Description,
            Items = g.Items.Where(i => i.Content.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                       (i.Description?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)).ToList()
        }).Where(g => g.Items.Count > 0);
    }
}

public partial class ApplicationOptionsViewModel : BotControlViewModelBase
{
    // Sections by settings key (the option's Tag); everything else is a webhook option
    private static readonly string[] GeneralKeys = { "ShowUsernameInTitle", "GrandpaMode", "ClientAnim", "ReloginTryDelay", "ReloginWatchdog", "ClearFlashCache" };
    private static readonly string[] UpdateKeys =
    {
        "CheckBotScriptsUpdates", "AutoUpdateBotScripts", "CheckAdvanceSkillSetsUpdates", "AutoUpdateAdvanceSkillSetsUpdates",
        "CheckJunkItemsUpdates", "AutoUpdateJunkItems"
    };

    private string _searchText = string.Empty;
    private OptionGroupViewModel _selectedSection;

    public ApplicationOptionsViewModel(List<DisplayOptionItemViewModelBase> appOptions)
        : base("Application Options", 580, 650)
    {
        ApplicationOptions = appOptions;
        BuildGroups();
        Sections = new List<OptionGroupViewModel> { OptionGroupViewModel.CreateAll(OptionGroups) }.Concat(OptionGroups).ToList();
        _selectedSection = Sections[0];
    }

    public List<DisplayOptionItemViewModelBase> ApplicationOptions { get; }
    public List<OptionGroupViewModel> OptionGroups { get; private set; } = new();

    // The chip row: "All", then each group
    public List<OptionGroupViewModel> Sections { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                OnPropertyChanged(nameof(FilteredGroups));
        }
    }

    public OptionGroupViewModel SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (value is not null && SetProperty(ref _selectedSection, value))
            {
                OnPropertyChanged(nameof(FilteredGroups));
                OnPropertyChanged(nameof(ShowSectionHeaders));
            }
        }
    }

    public bool ShowSectionHeaders => _selectedSection.IsAll;

    public IEnumerable<OptionGroupViewModel> FilteredGroups =>
        OptionGroupViewModel.Filter(_selectedSection.IsAll ? OptionGroups : new[] { _selectedSection }, _searchText);

    private void BuildGroups()
    {
        List<DisplayOptionItemViewModelBase> InSection(string[] keys) =>
            keys.Select(k => ApplicationOptions.FirstOrDefault(i => i.Tag == k)).OfType<DisplayOptionItemViewModelBase>().ToList();

        List<DisplayOptionItemViewModelBase> general = InSection(GeneralKeys);
        List<DisplayOptionItemViewModelBase> updates = InSection(UpdateKeys);
        List<DisplayOptionItemViewModelBase> webhooks = ApplicationOptions.Except(general).Except(updates).ToList();

        OptionGroups = new List<OptionGroupViewModel>
        {
            new() { Title = "General & Performance", ShortTitle = "General", Icon = "ApplicationCogOutline", Items = general },
            new() { Title = "Auto Updates & Sync", ShortTitle = "Updates", Icon = "CloudSyncOutline", Items = updates },
            new() { Title = "Webhooks & Alerts", ShortTitle = "Webhooks", Icon = "Webhook", Items = webhooks }
        };
    }
}
