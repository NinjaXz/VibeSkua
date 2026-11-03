using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.ObjectModel;

namespace Skua.App.Avalonia.ViewModels;

public sealed partial class NotificationCenterViewModel : BotControlViewModelBase
{
    private const int MaximumNotifications = 500;

    public NotificationCenterViewModel()
        : base("Notification Center", 560, 480, canResize: true)
    {
        // Keep collecting while the window is hidden so the feed reflects the whole app session.
        WeakReferenceMessenger.Default.Register<NotificationCenterViewModel, AddLogMessage>(this, OnLog);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, ItemDroppedMessage, int>(this, (int)MessageChannels.GameEvents, OnItemDropped);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, PlayerDeathMessage, int>(this, (int)MessageChannels.GameEvents, OnPlayerDeath);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, ScriptStartedMessage, int>(this, (int)MessageChannels.GameEvents, OnScriptStarted);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, ScriptStoppedMessage, int>(this, (int)MessageChannels.GameEvents, OnScriptStopped);
        StrongReferenceMessenger.Default.Register<NotificationCenterViewModel, ScriptErrorMessage, int>(this, (int)MessageChannels.GameEvents, OnScriptError);
    }

    public ObservableCollection<NotificationEntry> Notifications { get; } = new();

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Clear()
    {
        Notifications.Clear();
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

    private void OnScriptStarted(NotificationCenterViewModel recipient, ScriptStartedMessage message) =>
        recipient.Add("Script started.", NotificationKind.Activity);

    private void OnScriptStopped(NotificationCenterViewModel recipient, ScriptStoppedMessage message) =>
        recipient.Add("Script stopped.", NotificationKind.Activity);

    private void OnScriptError(NotificationCenterViewModel recipient, ScriptErrorMessage message) =>
        recipient.Add(message.Exception.Message, NotificationKind.Error);

    private void Add(string message, NotificationKind kind)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Notifications.Insert(0, new NotificationEntry(DateTimeOffset.Now, kind, message));
            while (Notifications.Count > MaximumNotifications)
                Notifications.RemoveAt(Notifications.Count - 1);
        });
    }
}

public sealed record NotificationEntry(DateTimeOffset Timestamp, NotificationKind Kind, string Message)
{
    public string TimeDisplay => Timestamp.ToLocalTime().ToString("HH:mm:ss");
}

public enum NotificationKind
{
    Activity,
    Item,
    System,
    Warning,
    Error
}
