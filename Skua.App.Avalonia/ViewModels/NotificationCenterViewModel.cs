using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Skua.App.Avalonia.ViewModels;

/// <summary>
/// A feed of notable game and script events (drops, deaths, relogins, quests, script status). Log lines stay in
/// the Logs window: mixing them in buried these events under debug output.
/// </summary>
public sealed partial class NotificationCenterViewModel : BotControlViewModelBase
{
    private const int MaximumNotifications = 500;

    public NotificationCenterViewModel(IScriptManager scriptManager, IFileDialogService fileDialog)
        : base("Notification Center", 560, 480, canResize: true)
    {
        _scriptManager = scriptManager;
        _fileDialog = fileDialog;
        Filters =
        [
            new NotificationFilter(null, "All"),
            new NotificationFilter(NotificationKind.Item, "Items"),
            new NotificationFilter(NotificationKind.Activity, "Activity"),
            new NotificationFilter(NotificationKind.Warning, "Warnings"),
            new NotificationFilter(NotificationKind.Error, "Errors"),
            new NotificationFilter(NotificationKind.System, "Maps"),
        ];
        _selectedFilter = Filters[0];

        // Keep collecting while the window is hidden so the feed reflects the whole app session.
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, ItemDroppedMessage, int>(this, (int)MessageChannels.GameEvents, OnItemDropped);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, PlayerDeathMessage, int>(this, (int)MessageChannels.GameEvents, OnPlayerDeath);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, ReloginTriggeredMessage, int>(this, (int)MessageChannels.GameEvents, OnRelogin);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, MapChangedMessage, int>(this, (int)MessageChannels.GameEvents, OnMapChanged);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, QuestAcceptedMessage, int>(this, (int)MessageChannels.GameEvents, OnQuestAccepted);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, QuestTurninMessage, int>(this, (int)MessageChannels.GameEvents, OnQuestTurnedIn);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, ItemSoldMessage, int>(this, (int)MessageChannels.GameEvents, OnItemSold);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, ScriptStartedMessage, int>(this, (int)MessageChannels.ScriptStatus, OnScriptStarted);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, ScriptStoppedMessage, int>(this, (int)MessageChannels.ScriptStatus, OnScriptStopped);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, ScriptErrorMessage, int>(this, (int)MessageChannels.ScriptStatus, OnScriptError);
    }

    private readonly IScriptManager _scriptManager;
    private readonly IFileDialogService _fileDialog;
    private readonly ObservableCollection<NotificationEntry> _notifications = new();

    // Name of the script that last started, kept for its stop and error entries (another script may be loaded
    // into the loader while it runs).
    private string? _runningScript;

    public NotificationFilter[] Filters { get; }

    /// <summary>The entries the selected filter lets through, newest first.</summary>
    public ObservableCollection<NotificationEntry> FilteredNotifications { get; } = new();

    [ObservableProperty]
    private NotificationFilter _selectedFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private bool _isEmpty = true;

    public string EmptyText => SelectedFilter.Kind is null
        ? "No events yet. Drops, deaths, relogins, quests and script starts and stops show up here."
        : $"No {SelectedFilter.Kind switch
        {
            NotificationKind.Item => "item",
            NotificationKind.Warning => "warning",
            NotificationKind.Error => "error",
            NotificationKind.System => "map",
            _ => "activity"
        }} events yet.";

    partial void OnSelectedFilterChanged(NotificationFilter value)
    {
        // Null when the chips list deselects during a rebuild; fall back to All.
        if (value is null)
        {
            SelectedFilter = Filters[0];
            return;
        }

        FilteredNotifications.Clear();
        foreach (NotificationEntry entry in _notifications.Where(value.Matches))
            FilteredNotifications.Add(entry);
        UpdateEmpty();
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>The shown entries (current filter), oldest first, one per line as Copy and Save write them.</summary>
    public static string ToText(IEnumerable<NotificationEntry> entries) =>
        string.Join(Environment.NewLine, entries.OrderBy(e => e.Timestamp).Select(e => e.Line));

    [RelayCommand]
    private void Save()
    {
        if (FilteredNotifications.Count > 0)
            _fileDialog.SaveText(ToText(FilteredNotifications));
    }

    [RelayCommand]
    private void Clear()
    {
        _notifications.Clear();
        FilteredNotifications.Clear();
        foreach (NotificationFilter filter in Filters)
            filter.Count = 0;
        UpdateEmpty();
    }

    private void OnItemDropped(NotificationCenterViewModel recipient, ItemDroppedMessage message)
    {
        string suffix = message.AddedToInv ? " (picked up)" : " (dropped)";
        recipient.Add($"{message.Item.Name}{suffix}", NotificationKind.Item);
    }

    private void OnPlayerDeath(NotificationCenterViewModel recipient, PlayerDeathMessage message) =>
        recipient.Add("Your character died.", NotificationKind.Warning);

    private void OnRelogin(NotificationCenterViewModel recipient, ReloginTriggeredMessage message) =>
        recipient.Add(message.WasKicked ? "Connection interrupted; reconnecting." : "Reconnecting to the game.", NotificationKind.Warning);

    private void OnMapChanged(NotificationCenterViewModel recipient, MapChangedMessage message) =>
        recipient.Add($"Entered {message.Map}", NotificationKind.System);

    private void OnQuestAccepted(NotificationCenterViewModel recipient, QuestAcceptedMessage message) =>
        recipient.Add($"Accepted quest #{message.QuestID}", NotificationKind.Activity);

    private void OnQuestTurnedIn(NotificationCenterViewModel recipient, QuestTurninMessage message) =>
        recipient.Add($"Completed quest #{message.QuestID}", NotificationKind.Activity);

    private void OnItemSold(NotificationCenterViewModel recipient, ItemSoldMessage message) =>
        recipient.Add($"Sold {message.QuantitySold:N0} item(s) for {message.Cost:N0} {(message.IsAC ? "AC" : "gold")}", NotificationKind.Item);

    // Sent right after the script thread starts, with LoadedScript still the script being run.
    private void OnScriptStarted(NotificationCenterViewModel recipient, ScriptStartedMessage message)
    {
        recipient._runningScript = ScriptName(recipient._scriptManager.LoadedScript);
        recipient.Add(recipient._runningScript is null ? "Script started" : $"Script started: {recipient._runningScript}", NotificationKind.Activity);
    }

    private void OnScriptStopped(NotificationCenterViewModel recipient, ScriptStoppedMessage message)
    {
        string? name = recipient._runningScript ?? ScriptName(recipient._scriptManager.LoadedScript);
        recipient._runningScript = null;
        recipient.Add(name is null ? "Script stopped" : $"Script stopped: {name}", NotificationKind.Activity);
    }

    private void OnScriptError(NotificationCenterViewModel recipient, ScriptErrorMessage message)
    {
        string? name = recipient._runningScript ?? ScriptName(recipient._scriptManager.LoadedScript);
        recipient.Add(name is null ? $"Script error: {message.Exception.Message}" : $"Script error ({name}): {message.Exception.Message}", NotificationKind.Error);
    }

    private static string? ScriptName(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFileNameWithoutExtension(path);

    private void Add(string message, NotificationKind kind)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            NotificationEntry entry = new(DateTimeOffset.Now, kind, message);
            _notifications.Insert(0, entry);
            Filters[0].Count++;
            FilterFor(kind).Count++;
            if (SelectedFilter.Matches(entry))
                FilteredNotifications.Insert(0, entry);

            // Drop the oldest; only the dropped entry is touched, never the whole list.
            while (_notifications.Count > MaximumNotifications)
            {
                NotificationEntry oldest = _notifications[^1];
                _notifications.RemoveAt(_notifications.Count - 1);
                Filters[0].Count--;
                FilterFor(oldest.Kind).Count--;
                if (FilteredNotifications.Count > 0 && ReferenceEquals(FilteredNotifications[^1], oldest))
                    FilteredNotifications.RemoveAt(FilteredNotifications.Count - 1);
            }
            UpdateEmpty();
        });
    }

    private NotificationFilter FilterFor(NotificationKind kind) => Filters.First(f => f.Kind == kind);

    private void UpdateEmpty() => IsEmpty = FilteredNotifications.Count == 0;
}

/// <summary>One filter chip: a kind (null for all), its label, and how many entries it holds.</summary>
public sealed partial class NotificationFilter : ObservableObject
{
    public NotificationFilter(NotificationKind? kind, string title)
    {
        Kind = kind;
        Title = title;
    }

    public NotificationKind? Kind { get; }
    public string Title { get; }

    [ObservableProperty]
    private int _count;

    public bool Matches(NotificationEntry entry) => Kind is null || entry.Kind == Kind;
}

public sealed record NotificationEntry(DateTimeOffset Timestamp, NotificationKind Kind, string Message)
{
    public string TimeDisplay => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>How the entry reads when copied or saved: time, kind, message.</summary>
    public string Line => $"{TimeDisplay} [{KindName}] {Message}";

    public string Icon => Kind switch
    {
        NotificationKind.Item => "GiftOutline",
        NotificationKind.Warning => "AlertOutline",
        NotificationKind.Error => "AlertCircleOutline",
        NotificationKind.System => "MapMarkerOutline",
        _ => "PlayCircleOutline"
    };

    public string KindName => Kind switch
    {
        NotificationKind.Item => "Item",
        NotificationKind.Warning => "Warning",
        NotificationKind.Error => "Error",
        NotificationKind.System => "Map",
        _ => "Activity"
    };

    public bool IsItem => Kind == NotificationKind.Item;
    public bool IsWarning => Kind == NotificationKind.Warning;
    public bool IsError => Kind == NotificationKind.Error;
    public bool IsSystem => Kind == NotificationKind.System;
}

public enum NotificationKind
{
    Activity,
    Item,
    System,
    Warning,
    Error
}
