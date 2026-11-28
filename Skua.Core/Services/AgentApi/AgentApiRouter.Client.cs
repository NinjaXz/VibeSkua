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
    /// A worker whose HTTP endpoint never answers, or whose character died and
    /// never logged in, otherwise fills the host's worker list forever. Only the
    /// host may stop one: the process that holds a character is busy holding it.
    /// </summary>
    private object StopClient(AgentApiRequest request)
    {
        StopClientRequest body = AgentApiJson.Read<StopClientRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);

        if (_client is null)
            throw new AgentApiException(501, "client_stop_unavailable");

        if (_client.CanHostGame)
            throw new AgentApiException(409, "client_stop_needs_host: this process holds a character, so ask the tab host that answers on the agent API port.");

        IReadOnlyList<ClientWorker> workers = _client.Workers;
        ClientWorker? worker = body.Port > 0
            ? workers.FirstOrDefault(found => found.Port == body.Port)
            : workers.FirstOrDefault(found => found.Pid == body.Pid);

        if (worker is null)
            throw new AgentApiException(404, "worker_not_found: no live client process holds " + (body.Port > 0 ? "port " + body.Port : "pid " + body.Pid));

        string? refusal = _client.Stop(worker.Pid);
        if (!string.IsNullOrEmpty(refusal))
            throw new AgentApiException(409, refusal!);

        return new { stopped = true, pid = worker.Pid, port = worker.Port };
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

    private sealed record StopClientRequest
    {
        public int Port { get; init; }
        public int Pid { get; init; }
    }
}
