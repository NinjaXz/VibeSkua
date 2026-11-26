using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Skua.Core.Services.AgentApi;
using Skua.Core.Tests.Fakes;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// The HTTP shell over the router, exercised through a real socket on loopback.
///
/// Everything below the router (binding, envelopes, status codes, the token
/// check, no CORS header) is glue that unit tests cannot see. One test per
/// branch of that glue is cheaper than finding it in the field.
/// </summary>
public sealed class AgentApiServerHttpTests : IDisposable
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.GZip });

    private readonly FakeWorld _world = new();
    private readonly string _root;
    private AgentApiServer? _server;

    public AgentApiServerHttpTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "vibeskoa-agent-http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _world.Set("Player.Playing", false);
    }

    private async Task<string> StartAsync(string token = "")
    {
        // A free port, taken the way a client picks one: bind port zero and
        // read what the operating system handed out.
        System.Net.Sockets.TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        AgentApiOptions options = new()
        {
            Enabled = true,
            Bind = "127.0.0.1",
            Port = port,
            Token = token,
            ScriptsRoot = _root,
        };
        AgentApiServer server = new(options, new AgentApiRouter(options, _world.Bot, _world.Manager, _world.Logs), _world.Logs);
        server.Start();
        _server = server;
        return $"http://127.0.0.1:{port}";
    }

    public void Dispose()
    {
        _server?.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Leftover temp folder is not worth failing the run.
        }
    }

    [Fact]
    public async Task State_ReturnsTheEnvelopeOverHttp()
    {
        string baseUrl = await StartAsync();

        using HttpResponseMessage response = await Http.GetAsync(baseUrl + "/state");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement root = JsonDocument.Parse(body).RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.False(root.GetProperty("data").GetProperty("playing").GetBoolean());
        Assert.True(root.TryGetProperty("timestamp", out _));

        // A page in the user's browser must not be able to read the bot without
        // an explicit choice, so no CORS header is sent.
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task WrongToken_IsRejectedAndTheReasonIsMachineReadable()
    {
        string baseUrl = await StartAsync("secret-token-1234567890");

        // Every endpoint needs the token, health included: the payload reports
        // the account name, the level and whether the character is in a map.
        using HttpRequestMessage anonymous = new(HttpMethod.Get, baseUrl + "/health");
        using HttpResponseMessage granted = await Http.SendAsync(anonymous);
        await granted.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, granted.StatusCode);

        using HttpRequestMessage authorized = new(HttpMethod.Get, baseUrl + "/health");
        authorized.Headers.TryAddWithoutValidation("Authorization", "Bearer secret-token-1234567890");
        using HttpResponseMessage accepted = await Http.SendAsync(authorized);
        await accepted.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using HttpRequestMessage wrong = new(HttpMethod.Get, baseUrl + "/health");
        wrong.Headers.TryAddWithoutValidation("Authorization", "Bearer nope");
        using HttpResponseMessage denied = await Http.SendAsync(wrong);

        JsonElement root = JsonDocument.Parse(await denied.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("unauthorized", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task QueryInTheUrl_ReachesTheHandler()
    {
        string baseUrl = await StartAsync();

        using HttpResponseMessage response = await Http.GetAsync(baseUrl + "/logs?type=script&limit=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal("script", data.GetProperty("type").GetString());
        Assert.Equal(2, data.GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task PostJson_ReachesTheHandlerAndWritesTheFile()
    {
        string baseUrl = await StartAsync();

        using StringContent content = new("""{"name":"Farm.cs","content":"void Main() { }"}""", Encoding.UTF8, "application/json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using HttpResponseMessage response = await Http.PostAsync(baseUrl + "/scripts/write", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(_root, "Farm.cs")));
    }

    [Fact]
    public async Task UnknownPath_IsFourOhFour()
    {
        string baseUrl = await StartAsync();

        using HttpResponseMessage response = await Http.GetAsync(baseUrl + "/not-a-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("not_found", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task NonLoopbackBind_WithoutAToken_FailsAtStartup()
    {
        AgentApiOptions options = new() { Enabled = true, Bind = "0.0.0.0", Port = 8766, ScriptsRoot = _root };

        using AgentApiServer server = new(options, new AgentApiRouter(options, _world.Bot, _world.Manager, _world.Logs), _world.Logs);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(server.Start);
        Assert.Contains("token is required", error.Message);
    }
}
