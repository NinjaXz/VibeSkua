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
    public string Bind { get; init; } = "127.0.0.1";

    public int Port { get; init; } = DefaultPort;

    /// <summary>
    /// Shared secret required on every request. Unlike the remote-control
    /// service, a token is required even on loopback: any page open in the
    /// user's browser can otherwise move the character.
    /// </summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>Folder the client loads scripts from. Writes and runs stay inside it.</summary>
    public string ScriptsRoot { get; init; } = Models.ClientFileSources.SkuaScriptsDIR;

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
    /// Reads the configuration from client settings, falling back to the
    /// environment so a headless or test run needs no settings file.
    /// </summary>
    public static AgentApiOptions FromSettings(Func<string, object?> get, string? environmentPrefix = "VIBESKUA_AGENT")
    {
        static string Env(string? prefix, string name) =>
            prefix is null ? string.Empty : (Environment.GetEnvironmentVariable(prefix + "_" + name) ?? string.Empty);

        string BindOf(string name, string prefixName, string fallback)
        {
            object? fromSettings = get(name);
            if (fromSettings is string text && !string.IsNullOrWhiteSpace(text))
                return text.Trim();
            string fromEnv = Env(environmentPrefix, prefixName);
            return string.IsNullOrWhiteSpace(fromEnv) ? fallback : fromEnv.Trim();
        }

        int IntOf(string name, string prefixName, int fallback)
        {
            object? value = get(name);
            if (value is int number)
                return number;
            if (value is string text && int.TryParse(text.Trim(), out int parsed))
                return parsed;
            string fromEnv = Env(environmentPrefix, prefixName);
            return int.TryParse(fromEnv, out int parsedEnv) ? parsedEnv : fallback;
        }

        object? enabledSetting = get("AgentApiEnabled");
        bool enabled = enabledSetting is bool flag
            ? flag
            : string.Equals(enabledSetting as string, "true", StringComparison.OrdinalIgnoreCase);

        return new AgentApiOptions
        {
            Enabled = enabled || string.Equals(Env(environmentPrefix, "ENABLED"), "1", StringComparison.Ordinal),
            Bind = BindOf("AgentApiBind", "BIND", "127.0.0.1"),
            Port = IntOf("AgentApiPort", "PORT", DefaultPort),
            Token = (get("AgentApiToken") as string ?? string.Empty).Trim() is { Length: > 0 } token
                ? token
                : Env(environmentPrefix, "TOKEN").Trim(),
        };
    }
}
