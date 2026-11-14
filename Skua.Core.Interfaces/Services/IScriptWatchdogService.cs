namespace Skua.Core.Interfaces.Services;

/// <summary>
/// Service that monitors script activity and detects silent stalls or deadlocks,
/// executing tiered auto-recovery procedures to keep farming scripts running.
/// </summary>
public interface IScriptWatchdogService : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the watchdog is actively monitoring.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the seconds elapsed since the last forward progress was detected.
    /// </summary>
    int SecondsSinceLastProgress { get; }

    /// <summary>
    /// Gets the current escalation tier (0 = healthy, 1 = soft rescue, 2 = map reload, 3 = restart).
    /// </summary>
    int CurrentTier { get; }

    /// <summary>
    /// Manually signals a progress heartbeat (e.g. called from custom scripts during long operations).
    /// </summary>
    void Heartbeat();

    /// <summary>
    /// Starts the watchdog monitoring loop.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the watchdog monitoring loop.
    /// </summary>
    void Stop();

    /// <summary>
    /// Resets progress timestamps and escalation state.
    /// </summary>
    void Reset();
}
