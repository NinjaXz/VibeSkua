using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Skua.App.Avalonia.ViewModels;

public sealed partial class NotificationCenterViewModel : BotControlViewModelBase
{
    private const int MaximumNotifications = 500;
    private string _selectedFilter = "All Events";

    public NotificationCenterViewModel()
        : base("Notification Center", 560, 480, canResize: true)
    {
        // Keep collecting while the window is hidden so the feed reflects the whole app session.
        WeakReferenceMessenger.Default.Register<NotificationCenterViewModel, AddLogMessage>(this, OnLog);
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

    public ObservableCollection<NotificationEntry> Notifications { get; } = new();
    public ObservableCollection<NotificationEntry> FilteredNotifications { get; } = new();
    public string[] FilterOptions { get; } = ["All Events", "Items", "Activity", "Warnings", "Errors", "System"];

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
                ApplyFilter();
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Clear()
    {
        Notifications.Clear();
        FilteredNotifications.Clear();
    }

    private void OnLog(NotificationCenterViewModel recipient, AddLogMessage message)
    {
        NotificationKind kind = message.LogType switch
        {
            LogType.Flash => NotificationKind.Warning,
            LogType.Debug => NotificationKind.System,
            _ => NotificationKind.Activity
        };
        recipient.Add(message.Text, kind);
    }

    private void OnItemDropped(NotificationCenterViewModel recipient, ItemDroppedMessage message)
    {
        string suffix = message.AddedToInv ? " (picked up)" : " (drop appeared)";
        recipient.Add($"{message.Item.Name}{suffix}", NotificationKind.Item);
    }

    private void OnPlayerDeath(NotificationCenterViewModel recipient, PlayerDeathMessage message) =>
        recipient.Add("Your character died.", NotificationKind.Warning);

    private void OnRelogin(NotificationCenterViewModel recipient, ReloginTriggeredMessage message) =>
        recipient.Add(message.WasKicked ? "Connection interrupted; reconnecting." : "Reconnecting to the game.", NotificationKind.Warning);

    private void OnMapChanged(NotificationCenterViewModel recipient, MapChangedMessage message) =>
        recipient.Add($"Entered map: {message.Map}", NotificationKind.System);

    private void OnQuestAccepted(NotificationCenterViewModel recipient, QuestAcceptedMessage message) =>
        recipient.Add($"Accepted quest #{message.QuestID}.", NotificationKind.Activity);

    private void OnQuestTurnedIn(NotificationCenterViewModel recipient, QuestTurninMessage message) =>
        recipient.Add($"Completed quest #{message.QuestID}.", NotificationKind.Activity);

    private void OnItemSold(NotificationCenterViewModel recipient, ItemSoldMessage message) =>
        recipient.Add($"Sold {message.QuantitySold:N0} item(s) for {message.Cost:N0} {(message.IsAC ? "AC" : "gold")}.", NotificationKind.Item);

    private void OnScriptStarted(NotificationCenterViewModel recipient, ScriptStartedMessage message) =>
        recipient.Add("Script started.", NotificationKind.Activity);

    private void OnScriptStopped(NotificationCenterViewModel recipient, ScriptStoppedMessage message) =>
        recipient.Add("Script stopped.", NotificationKind.Activity);

    private void OnScriptError(NotificationCenterViewModel recipient, ScriptErrorMessage message) =>
        recipient.Add(message.Exception.Message, NotificationKind.Error);

    private void ApplyFilter()
    {
        FilteredNotifications.Clear();
        foreach (NotificationEntry entry in Notifications.Where(entry => SelectedFilter == "All Events" || entry.Kind.ToString().Equals(SelectedFilter.TrimEnd('s'), StringComparison.OrdinalIgnoreCase)))
            FilteredNotifications.Add(entry);
    }

    private void Add(string message, NotificationKind kind)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Notifications.Insert(0, new NotificationEntry(DateTimeOffset.Now, kind, message));
            while (Notifications.Count > MaximumNotifications)
                Notifications.RemoveAt(Notifications.Count - 1);
            ApplyFilter();
        });
    }
}

public sealed record NotificationEntry(DateTimeOffset Timestamp, NotificationKind Kind, string Message)
{
    public string TimeDisplay => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string Icon => Kind switch
    {
        NotificationKind.Item => "🎁",
        NotificationKind.Warning => "⚠️",
        NotificationKind.Error => "❌",
        NotificationKind.System => "ℹ️",
        _ => "•"
    };
}

public enum NotificationKind
{
    Activity,
    Item,
    System,
    Warning,
    Error
}
