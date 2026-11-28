namespace Skua.Core.Interfaces;

/// <summary>
/// The HTTP endpoint set an AI agent uses to drive one character: read state,
/// move, fight, trade, and run scripts over request/response calls.
///
/// It sits beside the remote-control service (which serves the Android
/// companion and the fleet dashboards) and does not replace it: different
/// consumers, different rules about authentication and about what a browser
/// page may read.
/// </summary>
public interface IAgentApiService : IDisposable
{
    /// <summary>True when the listener is bound and answering.</summary>
    bool IsRunning { get; }

    /// <summary>The port the listener is configured for, even when it is off.</summary>
    int Port { get; }

    /// <summary>
    /// Binds the listener. Does nothing when the feature is switched off, and
    /// never throws: a bad port or bind address is logged, and the client keeps
    /// running without the endpoint.
    /// </summary>
    void Start();

    /// <summary>Releases the port.</summary>
    void Stop();
}
