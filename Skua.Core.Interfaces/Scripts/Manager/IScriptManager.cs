using System.ComponentModel;

namespace Skua.Core.Interfaces;

/// <summary>
/// Defines the contract for managing script execution, compilation, and configuration within an application.
/// </summary>
/// <remarks>Implementations of this interface provide mechanisms to start, compile, and manage scripts, as well
/// as to monitor script status and respond to property changes. This interface extends both <see cref="IScriptStatus"/> for script
/// state information and <see cref="INotifyPropertyChanged"/> to support data binding scenarios.</remarks>
public interface IScriptManager : IScriptStatus, INotifyPropertyChanged
{
    /// <summary>
    /// Gets the cancellation token source used to signal cancellation for the script operation.
    /// </summary>
    CancellationTokenSource? ScriptCts { get; }

    /// <summary>
    /// Gets or sets whether the script is currently paused.
    /// </summary>
    bool ScriptPaused { get; set; }

    /// <summary>
    /// Event used to pause and resume the script execution thread.
    /// </summary>
    ManualResetEventSlim ScriptPauseEvent { get; }

    /// <summary>
    /// Asynchronously starts the script and returns any exception that occurs during execution.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains an exception if the script fails;
    /// otherwise, null if the script completes successfully.</returns>
    Task<Exception?> StartScript();

    /// <summary>
    /// Compiles the specified source code and returns the resulting object representation.
    /// </summary>
    /// <param name="source">The source code to compile. Cannot be null or empty.</param>
    /// <returns>An object representing the compiled form of the source code, or null if compilation fails.</returns>
    object? Compile(string source);

    /// <summary>
    /// Loads the specified script configuration into the current context.
    /// </summary>
    /// <param name="script">An object representing the script configuration to load. Can be null to clear the current configuration.</param>
    void LoadScriptConfig(object? script);

    /// <summary>
    /// Sets the path of the currently loaded script.
    /// </summary>
    /// <param name="path">The file system path to the script to be marked as loaded. Cannot be null or empty.</param>
    void SetLoadedScript(string path);
}