namespace Skua.Core.Interfaces;

/// <summary>
/// Service providing local HTTP/REST API and remote controller connectivity
/// for Android mobile apps and LAN dashboards.
/// </summary>
public interface IRemoteControlService : IDisposable
{
    /// <summary>
    /// Gets whether the remote control HTTP server is actively running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the port the server is listening on.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Starts the remote control server.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the remote control server.
    /// </summary>
    void Stop();

    /// <summary>
    /// Optional provider returning the PID of the currently active child tab.
    /// </summary>
    Func<int>? ActiveChildPidProvider { get; set; }

    /// <summary>
    /// Optional action to switch or select active tab by PID.
    /// </summary>
    Action<int>? SelectChildPidAction { get; set; }

    /// <summary>
    /// Optional action to bring the PC window to front and focus the specified tab.
    /// </summary>
    Action<int>? FocusChildPidAction { get; set; }

    /// <summary>
    /// Optional action to close a tab by PID.
    /// </summary>
    Func<int, bool>? CloseChildPidAction { get; set; }

    /// <summary>
    /// Optional action to launch a managed account.
    /// </summary>
    Action<string, string?, string?>? LaunchAccountAction { get; set; }

    /// <summary>
    /// Optional action to launch an account group.
    /// </summary>
    Action<string>? LaunchGroupAction { get; set; }

    /// <summary>
    /// Optional action to start scripts on all tabs.
    /// </summary>
    Action? StartAllScriptsAction { get; set; }

    /// <summary>
    /// Optional action to stop scripts on all tabs.
    /// </summary>
    Action? StopAllScriptsAction { get; set; }

    /// <summary>
    /// Optional action to load a script across all tabs.
    /// </summary>
    Action<string>? LoadScriptAllAction { get; set; }

    /// <summary>
    /// Optional action to set headless mode across all tabs.
    /// </summary>
    Action<bool>? HeadlessAllAction { get; set; }

    /// <summary>
    /// Optional action to relogin all tabs.
    /// </summary>
    Action? ReloginAllAction { get; set; }

    /// <summary>
    /// Optional action to broadcast army scheduler payload.
    /// </summary>
    Action<string>? BroadcastArmySchedulerAction { get; set; }

    /// <summary>
    /// Optional action to stop army scheduler across all tabs.
    /// </summary>
    Action? StopArmySchedulerAction { get; set; }

    /// <summary>
    /// Optional provider returning a list of all open tabs and their info.
    /// </summary>
    Func<List<object>>? TabsInfoProvider { get; set; }

    /// <summary>
    /// Gets or sets whether the service is running in VibeSkua Manager.
    /// </summary>
    bool IsManagerMode { get; set; }

    /// <summary>
    /// Optional provider returning Army Queue status, squads, steps, and presets.
    /// </summary>
    Func<object>? ArmyQueueProvider { get; set; }

    /// <summary>
    /// Optional action to start the Army Queue.
    /// </summary>
    Func<Task<object>>? StartArmyQueueAction { get; set; }

    /// <summary>
    /// Optional action to stop the Army Queue.
    /// </summary>
    Action? StopArmyQueueAction { get; set; }

    /// <summary>
    /// Optional action to pause or resume the Army Queue.
    /// </summary>
    Action? PauseResumeArmyQueueAction { get; set; }

    /// <summary>
    /// Optional action to assign a script to a squad in the Army Queue.
    /// </summary>
    Action<int, string?>? SetArmyQueueStepScriptAction { get; set; }

    /// <summary>
    /// Optional action to add a new squad to the Army Queue.
    /// </summary>
    Action<string>? AddArmyQueueStepAction { get; set; }

    /// <summary>
    /// Optional action to remove a squad from the Army Queue.
    /// </summary>
    Action<int>? RemoveArmyQueueStepAction { get; set; }
}
