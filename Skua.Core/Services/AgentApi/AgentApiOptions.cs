namespace Skua.Core.Services.AgentApi;

/// <summary>
/// Configuration for the agent control API listener.
///
/// The listener is a second, loopback-first HTTP endpoint next to the existing
/// remote-control service (port 8088, Android companion and army dashboards).
/// It exists so an AI agent can drive one character with request/response
/// calls instead of clicking the client UI.
/// </summary>
public sealed class AgentApiOptions
{
    /// <summary>Legacy default port; kept so an existing agent config keeps working.</summary>
    public const int DefaultPort = 8766;

    /// <summary>Master switch. False means the listener never binds a port.</summary>
    public bool Enabled { get; init; }

    /// <summary>Interface to bind. Loopback unless the operator opts in to LAN access.</summary>
    // Bind and Port stay settable: an embedded child takes a free loopback port
    // of its own instead of the shared one, so tabs do not collide.
    public string Bind { get; set; } = "127.0.0.1";

    public int Port { get; set; } = DefaultPort;

    /// <summary>
    /// Shared secret required on every request. Unlike the remote-control
    /// service, a token is required even on loopback: any page open in the
    /// user's browser can otherwise move the character.
    /// </summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>Folder the client loads scripts from. Writes and runs stay inside it.</summary>
    public string ScriptsRoot { get; init; } = Models.ClientFileSources.SkuaScriptsDIR;

    /// <summary>Folder holding one .cfg per script: the choices a script shows its window.</summary>
    public string OptionsRoot { get; init; } = Models.ClientFileSources.SkuaOptionsDIR;

    /// <summary>Scripts to walk when listing the library: the shipped library has near 2000 files.</summary>
    public int MaxScriptRows { get; init; } = 4000;

    /// <summary>Longest time a mutating action may run before it is abandoned.</summary>
    public int ActionTimeoutSeconds { get; init; } = 180;

    /// <summary>
    /// How long a second action waits for the character before the API answers
    /// "another_action_in_progress". Long enough to absorb two calls that arrive
    /// together, short enough that the caller retries with fresh information.
    /// </summary>
    public int ActionGateWaitMs { get; init; } = 250;

    /// <summary>Largest script body the API will write to disk.</summary>
    public int MaxScriptBytes { get; init; } = 256 * 1024;

    /// <summary>Largest request body the API will read.</summary>
    public int MaxBodyBytes { get; init; } = 1024 * 1024;

    /// <summary>True when the bind address is a loopback address.</summary>
    public bool IsLoopback =>
        Bind is "127.0.0.1" or "localhost" or "::1" or "[::1]" ||
        (Bind.StartsWith("127.", StringComparison.OrdinalIgnoreCase) && Bind.Length <= 15);

    /// <summary>
    /// Throws when the configuration would expose an unauthenticated bot to the
    /// network. Called before binding, so a bad setting fails fast at startup.
    /// </summary>
    public void Validate()
    {
        if (Port is <= 0 or >= 65536)
            throw new InvalidOperationException($"AgentApi: invalid port {Port}.");

        if (string.IsNullOrWhiteSpace(Bind))
            throw new InvalidOperationException("AgentApi: bind address is empty.");

        if (IsLoopback)
            return;

        if (Token.Length == 0)
            throw new InvalidOperationException(
                "AgentApi: a token is required when the bind address is not loopback. " +
                "Anyone on the network could move the character.");

        if (Token.Length < 16)
            throw new InvalidOperationException(
                "AgentApi: a non-loopback token must be at least 16 characters.");
    }

    /// <summary>
    /// Builds the configuration from the client's saved settings.
    ///
    /// Each value may also come from the environment (VIBESKUA_AGENT_ENABLED,
    /// _BIND, _PORT, _TOKEN) so an agent can be launched against a test client
    /// without editing the settings file.
    /// </summary>
    public static AgentApiOptions FromClientSettings(Skua.Core.Models.ClientSettings settings, string environmentPrefix = "VIBESKUA_AGENT")
    {
        static string Env(string prefix, string name, string fallback)
        {
            string? value = Environment.GetEnvironmentVariable(prefix + "_" + name);
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        string bind = string.IsNullOrWhiteSpace(settings.AgentApiBind)
            ? "127.0.0.1"
            : settings.AgentApiBind.Trim();
        bind = Env(environmentPrefix, "BIND", bind);

        int port = settings.AgentApiPort > 0 ? settings.AgentApiPort : DefaultPort;
        port = IntEnv(environmentPrefix, "PORT", port);

        string token = (settings.AgentApiToken ?? string.Empty).Trim();
        token = Env(environmentPrefix, "TOKEN", token);

        bool enabled = settings.AgentApiEnabled ||
                       string.Equals(Environment.GetEnvironmentVariable(environmentPrefix + "_ENABLED"), "1", StringComparison.Ordinal);

        return new AgentApiOptions
        {
            Enabled = enabled,
            Bind = bind,
            Port = port,
            Token = token,
        };
    }

    private static int IntEnv(string prefix, string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(prefix + "_" + name), out int value) ? value : fallback;
}
