namespace Skua.Core.Interfaces;

/// <summary>
/// Represents the status and control interface for a running script, providing properties to query script state and
/// methods to manage script execution.
/// </summary>
/// <remarks>
/// Use this interface to monitor the execution state of a script and to perform actions such as
/// restarting or stopping the script. Implementations may provide additional behavior or state information relevant to
/// script management.
/// </remarks>
public interface IScriptStatus
{
    /// <summary>
    /// Whether the current script should terminate.
    /// </summary>
    bool ShouldExit { get; }

    /// <summary>
    /// Whether the script is running.
    /// </summary>
    bool ScriptRunning { get; }

    /// <summary>
    /// Path to the currently loaded script
    /// </summary>
    string LoadedScript { get; }

    /// <summary>
    /// The last script compiled.
    /// </summary>
    string CompiledScript { get; }

    /// <summary>
    /// Gets or sets the script configuration options for this instance.
    /// </summary>
    /// <remarks>
    /// Use this property to provide or retrieve custom script options that influence script
    /// execution or behavior. The value may be null if no configuration is specified.
    /// </remarks>
    IScriptOptionContainer? Config { get; set; }

    /// <summary>
    /// The option container of the script that is running now, set by its start and cleared when it ends; null when
    /// no script runs. Unlike <see cref="Config"/>, configuring another script doesn't replace it.
    /// </summary>
    IScriptOptionContainer? RunningConfig { get; }

    /// <summary>
    /// Asynchronously restarts the currently running script.
    /// </summary>
    /// <returns>A task that represents the asynchronous restart operation.</returns>
    Task RestartScriptAsync();

    /// <summary>
    /// Asynchronously starts a script.
    /// </summary>
    /// <returns>A task that represents the asynchronous start operation.</returns>
    Task<Exception?> StartScript();

    /// <summary>
    /// Sets the path to for you to start a script with <see cref="StartScript"/>.
    /// </summary>
    /// <param name="path"></param>
    void SetLoadedScript(string path);


    /// <summary>
    /// Asynchronously stops the currently running script.
    /// </summary>
    /// <param name="runScriptStoppingEvent">true to trigger the script stopping event before stopping the script; otherwise, false.</param>
    /// <returns>A ValueTask that represents the asynchronous stop operation.</returns>
    ValueTask StopScript(bool runScriptStoppingEvent = true);
}