using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Skua.Core.Interfaces;
using Skua.Core.Models.Servers;
using Skua.Shared.Avalonia.ViewModels;
using Skua.Shared.Avalonia.ViewModels.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Skua.App.Avalonia.ViewModels.Options;

public class GameOptionsViewModel : BotControlViewModelBase
{
    private static readonly string[] DefaultAQWServers =
    {
        "Artix", "Twilly", "Galanoth", "Gravelyn", "Safiria", "Sir Ver", "Sepulchure", "Yorumi", "Alteon", "Espada", "Yokai", "Zorbot"
    };

    // Sections by IScriptOption property name; options not listed go to Gameplay.
    private static readonly string[] VisualsOptions = { "LagKiller", "HidePlayers", "OptimizePlayers", "DisableFX", "DisableCollisions", "ShowFPS", "SetFPS" };
    private static readonly string[] CombatOptions = { "AggroMonsters", "AggroAllMonsters", "AttackWithoutTarget", "InfiniteRange", "Magnetise", "HuntDelay", "HuntBuffer", "RestPackets", "SafeTimings" };
    private static readonly string[] DropsOptions = { "AcceptACDrops", "AcceptAllDrops", "RejectAllDrops" };
    private static readonly string[] ReloginOptions = { "AutoRelogin", "AutoReloginAny", "ReloginFallback", "RetryRelogin", "SafeRelogin", "ReloginTries", "LoginTimeout" };

    private readonly IScriptServers _servers;
    private readonly IScriptOption _options;
    private readonly CBOChoiceOptionItemViewModel _reloginServerItem;
    private readonly List<string> _reloginServerNames;

    public GameOptionsViewModel(List<DisplayOptionItemViewModelBase> gameOptions, IScriptServers servers, IScriptOption options)
        : base("Game Options", 612, 720)
    {
        _servers = servers;
        _options = options;
        _selectedServer = !string.IsNullOrWhiteSpace(options.ReloginServer) ? options.ReloginServer : "Twilly";
        GameOptions = gameOptions;
        ResetOptionsCommand = new RelayCommand(_options.Reset);
        ResetDefaultOptionsCommand = new RelayCommand(_options.ResetToDefault);
        SaveOptionsCommand = new RelayCommand(_options.Save);

        _reloginServerNames = ServersList;
        _reloginServerItem = new CBOChoiceOptionItemViewModel("Relogin Server", "Server to log back in to", "ReloginServer",
            _reloginServerNames, Math.Max(0, _reloginServerNames.IndexOf(SelectedServer ?? string.Empty)));
        _reloginServerItem.PropertyChanged += ReloginServerItem_PropertyChanged;

        BuildGroups();
        Sections = new List<OptionGroupViewModel> { OptionGroupViewModel.CreateAll(OptionGroups) }.Concat(OptionGroups).ToList();
        _selectedSection = Sections[0];
    }

    protected override void OnActivated()
    {
        Messenger.Register<GameOptionsViewModel, PropertyChangedMessage<List<Server>>>(this, ServersChanged);
        Messenger.Register<GameOptionsViewModel, PropertyChangedMessage<string>>(this, OptionServerChanged);
        OnPropertyChanged(nameof(ServersList));
    }

    public List<DisplayOptionItemViewModelBase> GameOptions { get; }
    public List<OptionGroupViewModel> OptionGroups { get; private set; } = new();

    // The chip row: "All", then each group
    public List<OptionGroupViewModel> Sections { get; }

    private string _searchText = string.Empty;
    private OptionGroupViewModel _selectedSection;

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
            // The chip list clears its selection when it loses its items; keep the last section instead
            if (value is not null && SetProperty(ref _selectedSection, value))
            {
                OnPropertyChanged(nameof(FilteredGroups));
                OnPropertyChanged(nameof(ShowSectionHeaders));
            }
        }
    }

    // Headers only when several sections are shown
    public bool ShowSectionHeaders => _selectedSection.IsAll;

    public IEnumerable<OptionGroupViewModel> FilteredGroups =>
        OptionGroupViewModel.Filter(_selectedSection.IsAll ? OptionGroups : new[] { _selectedSection }, _searchText);

    private void BuildGroups()
    {
        string? BindingOf(DisplayOptionItemViewModelBase item) => (item as IBindingOptionItem)?.Binding;
        List<DisplayOptionItemViewModelBase> InSection(string[] names) =>
            names.Select(n => GameOptions.FirstOrDefault(i => BindingOf(i) == n)).OfType<DisplayOptionItemViewModelBase>().ToList();

        // Name colours and Reload Map aren't script options; they sit with the visuals
        List<DisplayOptionItemViewModelBase> visuals = InSection(VisualsOptions)
            .Concat(GameOptions.Where(i => BindingOf(i) is null))
            .ToList();
        List<DisplayOptionItemViewModelBase> combat = InSection(CombatOptions);
        List<DisplayOptionItemViewModelBase> drops = InSection(DropsOptions);
        List<DisplayOptionItemViewModelBase> relogin = InSection(ReloginOptions);
        relogin.Insert(0, _reloginServerItem);

        HashSet<DisplayOptionItemViewModelBase> used = new(visuals.Concat(combat).Concat(drops).Concat(relogin));
        List<DisplayOptionItemViewModelBase> gameplay = GameOptions.Where(i => !used.Contains(i))
            .OrderBy(i => i.Content, StringComparer.OrdinalIgnoreCase)
            .ToList();

        OptionGroups = new List<OptionGroupViewModel>
        {
            new() { Title = "Performance & Visuals", ShortTitle = "Visuals", Icon = "Speedometer", Items = visuals },
            new() { Title = "Combat & Targeting", ShortTitle = "Combat", Icon = "SwordCross", Items = combat },
            new() { Title = "Drops & Looting", ShortTitle = "Drops", Icon = "PackageVariantClosed", Items = drops },
            new() { Title = "Auto-Relogin & Network", ShortTitle = "Relogin", Icon = "ServerNetwork", Items = relogin },
            new() { Title = "Gameplay & Character", ShortTitle = "Gameplay", Icon = "AccountCogOutline", Items = gameplay }
        };
    }

    public List<string> ServersList
    {
        get
        {
            HashSet<string> serverNames = new(StringComparer.OrdinalIgnoreCase);

            if (_servers.CachedServers != null && _servers.CachedServers.Count > 0)
            {
                foreach (Server server in _servers.CachedServers)
                {
                    if (!string.IsNullOrWhiteSpace(server.Name))
                        serverNames.Add(server.Name);
                }
            }

            if (serverNames.Count == 0 && _servers.ServerList != null && _servers.ServerList.Count > 0)
            {
                foreach (Server server in _servers.ServerList)
                {
                    if (!string.IsNullOrWhiteSpace(server.Name))
                        serverNames.Add(server.Name);
                }
            }

            if (serverNames.Count == 0)
            {
                foreach (string name in DefaultAQWServers)
                    serverNames.Add(name);
            }

            return serverNames.OrderBy(s => s).ToList();
        }
    }

    private string? _selectedServer;

    public string? SelectedServer
    {
        get => _selectedServer ?? _options.ReloginServer ?? "Twilly";
        set
        {
            if (SetProperty(ref _selectedServer, value) && value is not null)
            {
                if (_options.ReloginServer != value)
                    _options.ReloginServer = value;
                int index = _reloginServerNames.FindIndex(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase));
                if (index >= 0 && !Equals(_reloginServerItem.Value, index))
                    _reloginServerItem.Value = index;
            }
        }
    }

    public IRelayCommand ResetOptionsCommand { get; }
    public IRelayCommand ResetDefaultOptionsCommand { get; }
    public IRelayCommand SaveOptionsCommand { get; }

    private void ReloginServerItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DisplayOptionItemViewModelBase.Value)
            && _reloginServerItem.Value is int index && index >= 0 && index < _reloginServerNames.Count)
            SelectedServer = _reloginServerNames[index];
    }

    private void ServersChanged(GameOptionsViewModel recipient, PropertyChangedMessage<List<Server>> message)
    {
        if (message.PropertyName == nameof(IScriptServers.CachedServers))
            recipient.OnPropertyChanged(nameof(recipient.ServersList));
    }

    private void OptionServerChanged(GameOptionsViewModel recipient, PropertyChangedMessage<string> message)
    {
        if (message.PropertyName == nameof(IScriptOption.ReloginServer) && message.NewValue != recipient.SelectedServer)
            recipient.SelectedServer = message.NewValue;
    }
}
