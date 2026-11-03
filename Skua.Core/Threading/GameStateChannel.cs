using System.Threading.Channels;

namespace Skua.Core.Threading;

public interface IGameStateChange
{
    string Category { get; }
    object? Data { get; }
    DateTime Timestamp { get; }
}

public class GameStateChange : IGameStateChange
{
    public string Category { get; set; } = string.Empty;
    public object? Data { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class GameStateChannel : IDisposable
{
    private readonly Channel<GameStateChange> _stateChannel;
    private readonly CancellationTokenSource _cts;
    private Task? _processingTask;
    private readonly Action<GameStateChange> _stateProcessor;

    public GameStateChannel(Action<GameStateChange> stateProcessor, int capacity = 100)
    {
        _stateProcessor = stateProcessor;
        _cts = new();
        _stateChannel = Channel.CreateUnbounded<GameStateChange>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Start()
    {
        if (_processingTask != null)
            return;

        _processingTask = ProcessStateChangesAsync(_cts.Token);
    }

    public async ValueTask PublishAsync(IGameStateChange change)
    {
        if (change is not GameStateChange gsc)
        {
            gsc = new GameStateChange
            {
                Category = change.Category,
                Data = change.Data,
                Timestamp = change.Timestamp
            };
        }

        await _stateChannel.Writer.WriteAsync(gsc, _cts.Token);
    }

    public void Publish(IGameStateChange change)
    {
        if (change is not GameStateChange gsc)
        {
            gsc = new GameStateChange
            {
                Category = change.Category,
                Data = change.Data,
                Timestamp = change.Timestamp
            };
        }

        _stateChannel.Writer.TryWrite(gsc);
    }

    private async Task ProcessStateChangesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (GameStateChange stateChange in _stateChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    _stateProcessor(stateChange);
                }
                catch
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task StopAsync()
    {
        _stateChannel.Writer.TryComplete();

        if (_processingTask is not null)
            await _processingTask.ConfigureAwait(false);

        _cts.Dispose();
    }

    public void Dispose()
    {
        _stateChannel.Writer.TryComplete();
        _cts.Cancel();
        _cts.Dispose();
    }
}

public static class GameStateCategories
{
    public const string PlayerInventory = nameof(PlayerInventory);
    public const string PlayerStats = nameof(PlayerStats);
    public const string MapChanged = nameof(MapChanged);
    public const string MonsterSpawned = nameof(MonsterSpawned);
    public const string MonsterKilled = nameof(MonsterKilled);
    public const string ItemDropped = nameof(ItemDropped);
    public const string AuraApplied = nameof(AuraApplied);
    public const string AuraRemoved = nameof(AuraRemoved);
    public const string CombatStart = nameof(CombatStart);
    public const string CombatEnd = nameof(CombatEnd);
}
