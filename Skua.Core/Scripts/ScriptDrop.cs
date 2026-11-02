using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models.Items;
using Skua.Core.Utils;

namespace Skua.Core.Scripts;

public partial class ScriptDrop : ObservableRecipient, IScriptDrop, IAsyncDisposable
{
    public ScriptDrop(
        Lazy<IFlashUtil> flash,
        Lazy<IScriptSend> send,
        Lazy<IScriptMap> map,
        Lazy<IScriptWait> wait,
        Lazy<IScriptOption> options,
        Lazy<IScriptPlayer> player,
        Lazy<IScriptInventory> inventory)
    {
        _lazySend = send;
        _lazyWait = wait;
        _lazyMap = map;
        _lazyOptions = options;
        _lazyPlayer = player;
        _lazyFlash = flash;
        _lazyInventory = inventory;

        StrongReferenceMessenger.Default.Register<ScriptDrop, ItemDroppedMessage, int>(this, (int)MessageChannels.GameEvents, AddDrop);
        StrongReferenceMessenger.Default.Register<ScriptDrop, DropPickupResultMessage, int>(this, (int)MessageChannels.GameEvents, PickupResult);
        StrongReferenceMessenger.Default.Register<ScriptDrop, ItemSoldMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r._refusedIDs.Clear());
        StrongReferenceMessenger.Default.Register<ScriptDrop, ItemBoughtMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r._refusedIDs.Clear());
        StrongReferenceMessenger.Default.Register<ScriptDrop, ItemAddedToBankMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r._refusedIDs.Clear());
        StrongReferenceMessenger.Default.Register<ScriptDrop, LogoutMessage, int>(this, (int)MessageChannels.GameEvents, ClearDrops);
        StrongReferenceMessenger.Default.Register<ScriptDrop, ScriptStoppedMessage, int>(this, (int)MessageChannels.ScriptStatus, ScriptStopped);
        Messenger.Register<ScriptDrop, PropertyChangedMessage<bool>>(this, OptionsChanged);

        _timerDrops = new(TimeSpan.FromMilliseconds(1000));
    }

    private readonly Lazy<IScriptSend> _lazySend;
    private readonly Lazy<IScriptWait> _lazyWait;
    private readonly Lazy<IScriptMap> _lazyMap;
    private readonly Lazy<IScriptOption> _lazyOptions;
    private readonly Lazy<IScriptPlayer> _lazyPlayer;
    private readonly Lazy<IFlashUtil> _lazyFlash;
    private readonly Lazy<IScriptInventory> _lazyInventory;
    private IScriptSend Send => _lazySend.Value;
    private IScriptWait Wait => _lazyWait.Value;
    private IScriptMap Map => _lazyMap.Value;
    private IScriptOption Options => _lazyOptions.Value;
    private IScriptPlayer Player => _lazyPlayer.Value;
    private IFlashUtil Flash => _lazyFlash.Value;
    private IScriptInventory Inventory => _lazyInventory.Value;

    private readonly PeriodicTimer _timerDrops;
    private Task? _taskDrops;
    private CancellationTokenSource? _ctsDrops;

    [ObservableProperty]
    private int _interval;

    [ObservableProperty]
    private bool _rejectElse;

    public bool Enabled => _taskDrops is { IsCompleted: false } && _ctsDrops is { IsCancellationRequested: false };
    private readonly SynchronizedList<string> _toPickup = new();
    public IEnumerable<string> ToPickup => _toPickup.Items;
    private readonly SynchronizedList<int> _toPickupIDs = new();
    public IEnumerable<int> ToPickupIDs => _toPickupIDs.Items;
    // Refused drop IDs, with the free slots at the time of the refusal.
    private readonly ConcurrentDictionary<int, (int Bag, int Misc)> _refusedIDs = new();
    // Set only on the automatic loop, so its pickups stop waiting as soon as it is cancelled.
    private readonly AsyncLocal<CancellationToken?> _loopToken = new();
    private readonly SynchronizedList<ItemBase> _currentDropInfos = new();
    public IEnumerable<ItemBase> CurrentDropInfos => _currentDropInfos.Items;
    public IEnumerable<string> CurrentDrops => CurrentDropInfos.Select(x => x.Name.Trim()).ToList();

    public void Pickup(string name)
    {
        ItemBase? drop = CurrentDropInfos.FirstOrDefault(d => d.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
        if (drop is null)
            return;

        SendPickup(drop);
    }

    public void Pickup(int id)
    {
        ItemBase? drop = CurrentDropInfos.FirstOrDefault(i => i.ID == id);
        if (drop is null)
            return;

        SendPickup(drop);
    }

    // The drop leaves the list only when the server accepts it (PickupResult), so a refused pickup stays listed.
    // Returns whether a request went out, and so whether a reply is coming.
    private bool SendPickup(ItemBase drop)
    {
        _loopToken.Value?.ThrowIfCancellationRequested();
        if (!drop.Temp && !Inventory.HasSpaceFor(drop))
            return false;
        // The automatic loop only re-sends a refused drop once a slot has freed up; an explicit pickup always retries.
        if (_loopToken.Value is not null && _refusedIDs.TryGetValue(drop.ID, out (int Bag, int Misc) refused))
        {
            (int bag, int misc) = FreeSlots();
            if (bag <= refused.Bag && misc <= refused.Misc)
                return false;
        }

        _refusedIDs.TryRemove(drop.ID, out _);
        Send.Packet($"%xt%zm%getDrop%{Map.RoomID}%{drop.ID}%");
        return true;
    }

    private (int Bag, int Misc) FreeSlots() => (Inventory.FreeSlots, Inventory.MiscFreeSlots);

    private void PickupEach(IEnumerable<ItemBase> drops)
    {
        foreach (ItemBase drop in drops)
        {
            if (SendPickup(drop))
                WaitForReplies(drop.ID);
        }
    }

    private void WaitForReplies(params int[] ids)
    {
        CancellationToken token = _loopToken.Value ?? CancellationToken.None;
        Wait.ForTrue(() => token.IsCancellationRequested || !Player.Playing || ids.All(HasReply), Wait.OverrideTimeout ? Wait.DropActionTimeout : 10);
        token.ThrowIfCancellationRequested();
    }

    private bool HasReply(int id) => _refusedIDs.ContainsKey(id) || !CurrentDropInfos.Any(d => d.ID == id);

    public void Pickup(params string[] names)
    {
        HashSet<string> wanted = new(names, StringComparer.OrdinalIgnoreCase);
        PickupEach(CurrentDropInfos.Where(d => wanted.Contains(d.Name.Trim())));
    }

    public void Pickup(params int[] ids)
    {
        PickupEach(CurrentDropInfos.Where(d => ids.Contains(d.ID)));
    }

    public void PickupACItems()
    {
        PickupEach(CurrentDropInfos.Where(d => d.Coins));
    }

    public void PickupAll(bool skipWait = false)
    {
        int[] sent = _currentDropInfos.Items.Where(SendPickup).Select(d => d.ID).ToArray();
        OnPropertyChanged(nameof(CurrentDropInfos));
        OnPropertyChanged(nameof(CurrentDrops));
        if (!skipWait)
            WaitForReplies(sent);
    }

    public void RejectExcept(params string[] names)
    {
        if (Options.AcceptACDrops)
            PickupACItems();
        RejectExceptCore(names);
    }

    public void RejectExcept(params int[] ids)
    {
        if (Options.AcceptACDrops)
            PickupACItems();
        IEnumerable<string> items = CurrentDropInfos.Where(d => ids.Contains(d.ID)).Select(d => d.Name);
        RejectExceptCore(items);
    }

    public void RejectAll(bool skipWait = false)
    {
        if (Options.AcceptACDrops)
            PickupACItems();
        RejectExceptCore(Array.Empty<string>());
    }

    private void RejectExceptCore(IEnumerable<string> names)
    {
        HashSet<string> keep = new(names, StringComparer.OrdinalIgnoreCase);
        if (Options.AcceptACDrops)
            keep.UnionWith(CurrentDropInfos.Where(d => d.Coins).Select(d => d.Name));
        if (!Flash.Call<bool>("rejectExcept", keep.Join(',').ToLowerInvariant()))
            return;
        _currentDropInfos.Remove(d => !keep.Contains(d.Name));
        OnPropertyChanged(nameof(CurrentDropInfos));
        OnPropertyChanged(nameof(CurrentDrops));
    }

    public void Start()
    {
        if (_taskDrops is { IsCompleted: false })
            return;
        _ctsDrops?.Dispose();

        _ctsDrops = new();
        CancellationToken token = _ctsDrops.Token;
        // Off the UI thread, where the replies this loop waits for arrive.
        _taskDrops = Task.Run(() => HandleDrops(_timerDrops, token));
        OnPropertyChanged(nameof(Enabled));
    }

    public void Stop()
    {
        if (_taskDrops is not { } task)
            return;

        _ctsDrops?.Cancel();
        Wait.ForTrue(() => task.IsCompleted, null, 2);
        OnPropertyChanged(nameof(Enabled));
        // Still finishing: keep the reference so Start can't run a second loop beside it.
        if (!task.IsCompleted)
            return;

        _ctsDrops?.Dispose();
        _ctsDrops = null;
        _taskDrops = null;
    }

    public async ValueTask StopAsync()
    {
        if (_taskDrops is not { } task)
            return;

        _ctsDrops?.Cancel();
        await task;
        _ctsDrops?.Dispose();
        _ctsDrops = null;
        _taskDrops = null;
        OnPropertyChanged(nameof(Enabled));
    }

    public void Add(params string[] names)
    {
        _toPickup.AddRange(names.Except(_toPickup.Items));
        OnPropertyChanged(nameof(ToPickup));
        Broadcast(null, ToPickup, nameof(ToPickup));
    }

    public void Add(params int[] ids)
    {
        _toPickupIDs.AddRange(ids.Except(_toPickupIDs.Items));
        OnPropertyChanged(nameof(ToPickupIDs));
        Broadcast(null, ToPickupIDs, nameof(ToPickupIDs));
    }

    public void Clear()
    {
        _toPickupIDs.Clear();
        _toPickup.Clear();
        OnPropertyChanged(nameof(ToPickupIDs));
        Broadcast(null, ToPickupIDs, nameof(ToPickupIDs));
        OnPropertyChanged(nameof(ToPickup));
        Broadcast(null, ToPickup, nameof(ToPickup));
    }

    public void Remove(params string[] names)
    {
        _toPickup.Remove(names.Contains);
        OnPropertyChanged(nameof(ToPickup));
        Broadcast(null, ToPickup, nameof(ToPickup));
    }

    public void Remove(params int[] ids)
    {
        _toPickupIDs.Remove(ids.Contains);
        OnPropertyChanged(nameof(ToPickupIDs));
        Broadcast(null, ToPickupIDs, nameof(ToPickupIDs));
    }

    private async Task HandleDrops(PeriodicTimer timer, CancellationToken token)
    {
        _loopToken.Value = token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(token))
                    break;
                if (!Player.Playing)
                    continue;

                if (Interval > 0)
                    await Task.Delay(Interval, token);

                if (Options.AcceptAllDrops)
                {
                    PickupAll();
                    continue;
                }

                if (Options.AcceptACDrops)
                    PickupACItems();

                if (Options.RejectAllDrops)
                {
                    RejectAll();
                    continue;
                }

                int[] pickupIDs = _toPickupIDs.Items.ToArray();
                if (pickupIDs.Length > 0)
                    Pickup(pickupIDs);

                string[] pickupNames = _toPickup.Items.ToArray();
                if (pickupNames.Length > 0)
                    Pickup(pickupNames);

                if (RejectElse)
                    RejectExcept(BuildRejectElseWhitelist(pickupNames, pickupIDs));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Error in drop handler: {ex.Message}");
            }
        }
    }

    private string[] BuildRejectElseWhitelist(string[] pickupNames, int[] pickupIDs)
    {
        HashSet<string> whitelist = new(pickupNames, StringComparer.OrdinalIgnoreCase);
        if (pickupIDs.Length == 0)
            return whitelist.ToArray();

        HashSet<int> idWhitelist = new(pickupIDs);
        foreach (ItemBase drop in _currentDropInfos.Items)
        {
            if (idWhitelist.Contains(drop.ID))
                whitelist.Add(drop.Name);
        }

        return whitelist.ToArray();
    }

    private void OptionsChanged(ScriptDrop recipient, PropertyChangedMessage<bool> message)
    {
        if (message.Sender.GetType() != typeof(ScriptOption))
            return;

        switch (message.PropertyName)
        {
            case nameof(IScriptOption.AcceptACDrops):
            case nameof(IScriptOption.AcceptAllDrops):
            case nameof(IScriptOption.RejectAllDrops):
                recipient.Start();
                break;
        }
    }

    private void PickupResult(ScriptDrop recipient, DropPickupResultMessage message)
    {
        if (!message.Success)
        {
            recipient._refusedIDs[message.ItemID] = recipient.FreeSlots();
            return;
        }

        recipient._refusedIDs.TryRemove(message.ItemID, out _);
        recipient._currentDropInfos.Remove(d => d.ID == message.ItemID);
        recipient.OnPropertyChanged(nameof(recipient.CurrentDropInfos));
        recipient.OnPropertyChanged(nameof(recipient.CurrentDrops));
    }

    private void ClearDrops(ScriptDrop recipient, LogoutMessage message)
    {
        recipient._refusedIDs.Clear();
        recipient._currentDropInfos.Clear();
        recipient.OnPropertyChanged(nameof(recipient.CurrentDropInfos));
        recipient.OnPropertyChanged(nameof(recipient.CurrentDrops));
    }

    private void AddDrop(ScriptDrop recipient, ItemDroppedMessage message)
    {
        if (message.AddedToInv)
            return;

        if (!recipient.CurrentDropInfos.Contains(message.Item))
            recipient._currentDropInfos.Add(message.Item);
        else
            recipient._currentDropInfos.Find(i => i.Equals(message.Item))!.Quantity += message.Item.Quantity;
        recipient.OnPropertyChanged(nameof(recipient.CurrentDropInfos));
        recipient.OnPropertyChanged(nameof(recipient.CurrentDrops));
    }

    private async void ScriptStopped(ScriptDrop recipient, ScriptStoppedMessage message)
    {
        try
        {
            await recipient.StopAsync();
        }
        catch (Exception ex)
        {
            // Log the exception to prevent unobserved task exceptions
            System.Diagnostics.Trace.WriteLine($"Error in ScriptStopped handler: {ex.Message}");
        }
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _ctsDrops?.Cancel();
            Wait.ForTrue(() => _taskDrops?.IsCompleted == true, null, 20);
        }
        catch { }

        _ctsDrops?.Dispose();
        _ctsDrops = null;
        _taskDrops = null;

        _timerDrops?.Dispose();

        if (IsActive)
        {
            StrongReferenceMessenger.Default.UnregisterAll(this);
            Messenger.UnregisterAll(this);
            IsActive = false;
        }

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_taskDrops is not null)
            {
                _ctsDrops?.Cancel();
                await _taskDrops;
            }
        }
        catch { }

        _ctsDrops?.Dispose();
        _ctsDrops = null;
        _taskDrops = null;

        _timerDrops?.Dispose();

        if (IsActive)
        {
            StrongReferenceMessenger.Default.UnregisterAll(this);
            Messenger.UnregisterAll(this);
            IsActive = false;
        }

        GC.SuppressFinalize(this);
    }
}