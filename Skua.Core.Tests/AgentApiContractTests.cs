using System.Text.Json;
using Skua.Core.Services.AgentApi;
using Skua.Core.Tests.Fakes;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// Wire-contract tests for the agent control API: transport-free dispatch,
/// authentication, status codes and the response envelope.
///
/// These are the rules an AI client depends on. A handler that returns 200
/// with an error string instead of a 4xx status makes an agent believe an
/// action happened, so the status code is the assertion that matters.
/// </summary>
public class AgentApiContractTests
{
    protected FakeWorld World { get; } = new();
    protected string ScriptsRoot { get; } = Path.Combine(Path.GetTempPath(), "vibeskoa-agent-tests-" + Guid.NewGuid().ToString("N"));

    protected AgentApiRouter Router(AgentApiOptions? overrideOptions = null) =>
        new(overrideOptions ?? Options(), World.Bot, World.Manager, World.Logs);

    protected AgentApiOptions Options(string token = "", int maxScriptBytes = 256 * 1024) => new()
    {
        Token = token,
        ScriptsRoot = ScriptsRoot,
        MaxScriptBytes = maxScriptBytes,
    };

    public AgentApiContractTests()
    {
        Directory.CreateDirectory(ScriptsRoot);
    }

    public static AgentApiRequest Get(string path, string authorization = null!) =>
        new() { Method = "GET", Path = path, Authorization = authorization };

    public static AgentApiRequest Post(string path, string json = "{}", string authorization = null!) =>
        new() { Method = "POST", Path = path, Body = json, Authorization = authorization };

    // ---------------------------------------------------------------- auth

    [Fact]
    public async Task TokenIsConfigured_RejectsRequestWithoutBearer()
    {
        AgentApiResponse response = await Router(Options("secret-token")).HandleAsync(Get("/state"));

        Assert.Equal(401, response.Status);
        Assert.Equal("unauthorized", response.Error);
    }

    [Fact]
    public async Task TokenIsConfigured_AcceptsBearerAndHeader()
    {
        AgentApiRouter router = Router(Options("secret-token"));

        AgentApiResponse bearer = await router.HandleAsync(Get("/state", "Bearer secret-token"));
        AgentApiResponse header = await router.HandleAsync(Get("/state", "token secret-token"));

        Assert.Equal(200, bearer.Status);
        Assert.Equal(200, header.Status);
    }

    [Fact]
    public async Task WrongToken_IsRejected()
    {
        AgentApiResponse response = await Router(Options("expected")).HandleAsync(Get("/state", "Bearer other"));

        Assert.Equal(401, response.Status);
    }

    [Fact]
    public async Task NoTokenConfigured_AllowsLoopbackRequests()
    {
        AgentApiResponse response = await Router(Options("")).HandleAsync(Get("/state"));

        Assert.Equal(200, response.Status);
    }

    [Fact]
    public void NonLoopbackBind_RequiresToken()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new AgentApiOptions { Bind = "0.0.0.0", Token = "" }.Validate());

        Assert.Contains("token", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------ dispatch

    [Fact]
    public async Task UnknownPath_IsNotFound()
    {
        AgentApiResponse response = await Router().HandleAsync(Get("/does/not/exist"));

        Assert.Equal(404, response.Status);
        Assert.Equal("not_found", response.Error);
    }

    [Fact]
    public async Task WrongMethod_IsMethodNotAllowed()
    {
        AgentApiResponse response = await Router().HandleAsync(Post("/state"));

        Assert.Equal(405, response.Status);
        Assert.Equal("method_not_allowed", response.Error);
    }

    [Fact]
    public async Task PostWithoutBody_IsBadRequest()
    {
        AgentApiResponse response = await Router().HandleAsync(new AgentApiRequest { Method = "POST", Path = "/jump" });

        Assert.Equal(400, response.Status);
        Assert.Equal("request_body_required", response.Error);
    }

    [Fact]
    public async Task MalformedJson_IsBadRequest()
    {
        AgentApiResponse response = await Router().HandleAsync(Post("/jump", "{not json"));

        Assert.Equal(400, response.Status);
        Assert.StartsWith("invalid_json_body", response.Error);
    }

    [Fact]
    public async Task EveryRoute_IsReachable()
    {
        // Pins the route table against the client vocabulary: a renamed or
        // dropped route shows up here instead of as a mysterious 404 in an
        // agent session.
        string[] gets =
        {
            "/health", "/state", "/map", "/map/npcs", "/players", "/inventory", "/monsters",
            "/servers", "/shop", "/quests", "/quest?id=1", "/quests/cached", "/scripts", "/script", "/logs",
        };
        string[] posts =
        {
            "/join", "/jump", "/walk", "/walk-speed", "/goto", "/attack", "/kill", "/cancel-attack",
            "/exit-combat", "/rest", "/reload-map", "/relogin", "/login", "/logout", "/select-server",
            "/quests/load", "/quests/accept", "/quests/complete", "/quests/abandon", "/shop/load",
            "/shop/buy", "/shop/close", "/scripts/write", "/scripts/run", "/scripts/pause",
            "/scripts/resume", "/scripts/stop",
        };

        AgentApiRouter router = Router();
        List<string> missing = new();
        foreach (string path in gets)
        {
            AgentApiResponse r = await router.HandleAsync(Get(path));
            if (r.Status == 404 && r.Error == "not_found")
                missing.Add("GET " + path);
        }
        foreach (string path in posts)
        {
            AgentApiResponse r = await router.HandleAsync(Post(path, "{}"));
            if (r.Status == 404 && r.Error == "not_found")
                missing.Add("POST " + path);
        }

        Assert.Empty(missing);
    }

    // --------------------------------------------------------- bot not ready

    [Fact]
    public async Task ActionWhenNotPlaying_IsConflictAndTouchesNothing()
    {
        World.Set("Player.Playing", false);

        AgentApiResponse response = await Router().HandleAsync(Post("/jump", """{"cell":"c1","pad":"Spawn"}"""));

        Assert.Equal(409, response.Status);
        Assert.Equal("player_not_playing", response.Error);
        Assert.False(World.WasCalled("Map.Jump"));
    }

    [Fact]
    public async Task StateAnswersWhileLoggedOut()
    {
        World.Set("Player.Playing", false);
        World.Set("Player.LoggedIn", false);

        AgentApiResponse response = await Router().HandleAsync(Get("/state"));

        Assert.Equal(200, response.Status);
        JsonElement data = Json(JsonSerializer.SerializeToElement(response.Data!));
        Assert.False(data.GetProperty("playing").GetBoolean());
    }

    [Fact]
    public async Task ThrowingBotMember_DegradesToDefaultsInsteadOfFailing()
    {
        // A half-loaded map makes the generated bindings throw. The client must
        // still get a usable status object.
        World.ThrowOn("Map.Name");

        AgentApiResponse response = await Router().HandleAsync(Get("/state"));

        Assert.Equal(200, response.Status);
        JsonElement data = Json(JsonSerializer.SerializeToElement(response.Data!));
        Assert.Equal(string.Empty, data.GetProperty("map").GetProperty("name").GetString());
    }

    // ---------------------------------------------------------- serialisation

    [Fact]
    public async Task ResponseEnvelope_HasOkDataAndTimestamp()
    {
        object? payload = (await Router().HandleAsync(Get("/health"))).Data;
        string envelope = JsonSerializer.Serialize(new { ok = true, data = payload, timestamp = DateTimeOffset.UtcNow });

        using JsonDocument doc = JsonDocument.Parse(envelope);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("timestamp", out _));
    }

    /// <summary>
    /// Serialises a payload the way the HTTP layer does, so a test asserts on
    /// the property names an agent actually sees (camelCase).
    /// </summary>
    internal static JsonElement Json(object value) =>
        value is JsonElement e ? e : JsonSerializer.SerializeToElement(value, AgentJsonOptions);

    internal static readonly JsonSerializerOptions AgentJsonOptions = new(JsonSerializerDefaults.Web);
}
