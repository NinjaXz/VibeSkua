using System.Text.Json;

namespace Skua.Core.Services.AgentApi;

/// <summary>
/// One API call, stripped of any HTTP type.
///
/// Keeping the transport out of the router is what makes the agent API
/// testable: the existing remote-control service parses sockets inside a
/// 4,900-line switch, so none of its handlers could be unit tested.
/// </summary>
public sealed class AgentApiRequest
{
    public string Method { get; init; } = "GET";

    /// <summary>Lower-cased absolute path, without the query string.</summary>
    public string Path { get; init; } = "/";

    /// <summary>Query-string values, matched case-insensitively.</summary>
    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>();

    /// <summary>Raw request body (JSON). Null when the request had no body.</summary>
    public string? Body { get; init; }

    /// <summary>Authorization header value, or the bare token from a custom header.</summary>
    public string? Authorization { get; init; }

    public int BodyLength => Body?.Length ?? 0;
}

/// <summary>Outcome of one API call: a status code plus either data or an error code.</summary>
public sealed class AgentApiResponse
{
    public int Status { get; init; }

    /// <summary>Payload for a successful call.</summary>
    public object? Data { get; init; }

    /// <summary>Machine-readable error for a failed call. Agents branch on this.</summary>
    public string? Error { get; init; }

    public static AgentApiResponse Ok(object? data) => new() { Status = 200, Data = data };

    public static AgentApiResponse Fail(int status, string error) => new() { Status = status, Error = error };

    /// <summary>Wire envelope: <c>{ok,data,error,timestamp}</c>.</summary>
    public object ToEnvelope() => Error is null
        ? new { ok = true, data = Data, timestamp = DateTimeOffset.UtcNow }
        : new { ok = false, error = Error, timestamp = DateTimeOffset.UtcNow };
}

/// <summary>
/// A request failure with the HTTP status it maps to. Thrown by handlers and
/// validation, and turned into an <see cref="AgentApiResponse"/> by the router.
/// </summary>
public sealed class AgentApiException : Exception
{
    public int Status { get; }

    public AgentApiException(int status, string message) : base(message) => Status = status;
}

/// <summary>
/// Reader for what is inside the loaded map: which NPC stands in which cell,
/// which quests and shops they expose, and which exits they lead to.
///
/// The game only stores this in the map SWF timeline, so the implementation
/// downloads and decompiles that file. It is behind an interface so the router
/// can be tested without a game client, a Flash runtime or Java.
/// </summary>
public interface IMapInfoReader
{
    Task<MapInfo> GetCurrentMapAsync(bool refresh, CancellationToken ct);
}

/// <summary>Failure while reading map information, with a code the client can branch on.</summary>
public sealed class MapInfoException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Decompiled contents of the current map, cached per map file.</summary>
public sealed class MapInfo
{
    public string Map { get; init; } = string.Empty;
    public int RoomId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public List<string> Cells { get; init; } = new();
    public bool FromCache { get; init; }
    public List<MapNpc> Npcs { get; init; } = new();
}

/// <summary>An NPC or interactive object in the map, and where it stands.</summary>
public sealed class MapNpc
{
    public string Name { get; init; } = string.Empty;
    public List<string> Cells { get; init; } = new();
    public List<NpcQuest> Quests { get; init; } = new();
    public List<NpcShop> Shops { get; init; } = new();
    public List<NpcJoin> Joins { get; init; } = new();
    public bool HasDialogue { get; init; }
}

public sealed class NpcQuest
{
    public int Id { get; init; }
    public bool TurnIn { get; init; }
}

public sealed class NpcShop
{
    public int Id { get; init; }
}

public sealed class NpcJoin
{
    public string Map { get; init; } = string.Empty;
    public string Cell { get; init; } = string.Empty;
}

/// <summary>JSON conventions shared by the request and response bodies.</summary>
internal static class AgentApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static T Read<T>(string? body, int bodyLength, int maxBodyBytes) where T : class
    {
        if (bodyLength > maxBodyBytes)
            throw new AgentApiException(413, "request_body_too_large");
        if (string.IsNullOrWhiteSpace(body))
            throw new AgentApiException(400, "request_body_required");

        try
        {
            return JsonSerializer.Deserialize<T>(body, Options) ?? throw new AgentApiException(400, "invalid_json_body");
        }
        catch (JsonException ex)
        {
            throw new AgentApiException(400, $"invalid_json_body: {ex.Message}");
        }
    }
}
