namespace Skua.Core.Services.AgentApi;

/// <summary>
/// The routes about the process that answers: who is here, which client
/// processes a tab host can see, and how to start one.
///
/// The rule behind them: the process that holds the HTTP endpoint is often the
/// tab host, which holds no character. An agent must learn that as a state
/// answer, not as a crash inside the game.
/// </summary>
public sealed partial class AgentApiRouter
{
    private object BuildClientStatus() => new
    {
        role = _client?.Role ?? "standalone",
        canHostGame = _client?.CanHostGame ?? true,
        workers = (_client?.Workers ?? (IReadOnlyList<ClientWorker>)Array.Empty<ClientWorker>())
            .Select(worker => new { pid = worker.Pid, port = worker.Port })
            .ToList(),
    };

    private object StartClient(AgentApiRequest request)
    {
        StartClientRequest body = AgentApiJson.Read<StartClientRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        string username = RequireText(body.Username, "username", 64);

        if (_client is null)
            throw new AgentApiException(501, "client_start_unavailable");

        if (_client.CanHostGame)
            throw new AgentApiException(409, "client_already_running: this process holds a character already");

        string? server = string.IsNullOrWhiteSpace(body.Server) ? null : RequireText(body.Server, "server", 64);

        string? refusal = _client.Start(username, server);
        if (!string.IsNullOrEmpty(refusal))
            throw new AgentApiException(409, refusal!);

        return new { launched = true, username, server = server ?? string.Empty };
    }

    /// <summary>
    /// Stops a session route in a process that cannot hold a game, before the
    /// request reaches the game and fails there.
    /// </summary>
    private void RequireGameSession()
    {
        if (_client is { CanHostGame: false })
            throw new AgentApiException(
                409,
                "client_not_started: no game client runs in this process, so no character is here to drive. " +
                "Start one with POST /client/start, then drive the client process that GET /client lists.");
    }

    private sealed record StartClientRequest
    {
        public string Username { get; init; } = string.Empty;
        public string? Server { get; init; }
    }
}
