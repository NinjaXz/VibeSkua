using System.Text.Json;
using Skua.Core.Interfaces;
using Skua.Core.Services.AgentApi;
using Skua.Core.Tests.Fakes;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// The endpoint answers in two kinds of process: the tab host, which holds no
/// game, and the client process, which does. These tests pin what each one says,
/// and what the host can do about it.
/// </summary>
[Collection("AgentApiEnvironment")]
public sealed class AgentApiClientTests : AgentApiContractTests
{
    private readonly FakeWorld _world = new();

    private AgentApiRouter Router(IClientController? controller = null) =>
        new(new AgentApiOptions { ScriptsRoot = ScriptsRoot }, _world.Bot, _world.Manager, _world.Logs, null, controller);

    private static JsonElement Data(AgentApiResponse response) => Json(response.Data!);

    // --------------------------------------------------- a host has no game

    [Theory]
    [InlineData("/login", """{"username":"jey","password":"pw"}""")]
    [InlineData("/relogin", "{}")]
    [InlineData("/select-server", """{"server":"Alteon"}""")]
    public async Task ASessionRouteOnAHostSaysTheClientIsNotStarted(string path, string body)
    {
        IClientController host = new StubController { Role = "host", CanHostGame = false };

        AgentApiResponse response = await Router(host).HandleAsync(Post(path, body));

        Assert.Equal(409, response.Status);
        Assert.StartsWith("client_not_started", response.Error);
    }

    [Fact]
    public async Task AHostStateAnswerStillWorks()
    {
        IClientController host = new StubController { Role = "host", CanHostGame = false };

        AgentApiResponse state = await Router(host).HandleAsync(Get("/state"));
        AgentApiResponse health = await Router(host).HandleAsync(Get("/health"));

        Assert.Equal(200, state.Status);
        Assert.Equal("host", Data(health).GetProperty("role").GetString());
    }

    [Fact]
    public void TheHostReadsThePortFilesItsChildrenLeave()
    {
        string dir = Path.Combine(Path.GetTempPath(), "skua-agent-workers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, Environment.ProcessId + ".agent.txt"), "8801");
            File.WriteAllText(Path.Combine(dir, "999999.agent.txt"), "8802");
            File.WriteAllText(Path.Combine(dir, "12.agent.txt"), "not-a-port");
            File.WriteAllText(Path.Combine(dir, "13.remote.txt"), "8803");

            IReadOnlyList<ClientWorker> workers =
                new AgentApiClientController(_world.Service<ISettingsService>("Settings"), dir).Workers;

            Assert.Equal(new[] { new ClientWorker(Environment.ProcessId, 8801) }, workers);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AChildKnowsItAlreadyHoldsACharacter()
    {
        Environment.SetEnvironmentVariable("VIBESKUA_EMBED", "1");
        try
        {
            IClientController child = new AgentApiClientController(_world.Service<ISettingsService>("Settings"), Path.GetTempPath());

            Assert.Equal("child", child.Role);
            Assert.True(child.CanHostGame);
            Assert.Contains("client_already_running", child.Start("jey", null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("VIBESKUA_EMBED", null);
        }
    }

    [Fact]
    public async Task AClientProcessRunsTheSessionRouteAsBefore()
    {
        _world.Set("Servers.Relogin", (string server) => true);
        IClientController child = new StubController { Role = "child", CanHostGame = true };

        AgentApiResponse response = await Router(child).HandleAsync(Post("/relogin", "{}"));

        Assert.Equal(200, response.Status);
        Assert.True(_world.WasCalled("Servers.Relogin"));
    }

    [Fact]
    public async Task WithoutAControllerTheRoutesStayAsTheyWere()
    {
        AgentApiResponse session = await Router().HandleAsync(Post("/relogin", "{}"));
        AgentApiResponse start = await Router().HandleAsync(Post("/client/start", """{"username":"jey"}"""));

        Assert.True(_world.WasCalled("Servers.Relogin"));
        Assert.NotEqual(409, session.Status);
        Assert.Equal(501, start.Status);
        Assert.Equal("client_start_unavailable", start.Error);
    }

    // ------------------------------------------------------------- GET /client

    [Fact]
    public async Task ClientStatusNamesTheProcessAndItsWorkers()
    {
        IClientController host = new StubController
        {
            Role = "host",
            CanHostGame = false,
            Workers = new List<ClientWorker> { new(4321, 8801), new(4322, 8802) },
        };

        AgentApiResponse response = await Router(host).HandleAsync(Get("/client"));
        JsonElement data = Data(response);

        Assert.Equal(200, response.Status);
        Assert.Equal("host", data.GetProperty("role").GetString());
        Assert.False(data.GetProperty("canHostGame").GetBoolean());
        Assert.Equal(2, data.GetProperty("workers").GetArrayLength());
        Assert.Equal(4321, data.GetProperty("workers")[0].GetProperty("pid").GetInt32());
        Assert.Equal(8801, data.GetProperty("workers")[0].GetProperty("port").GetInt32());
    }

    // ------------------------------------------------------- POST /client/start

    [Fact]
    public async Task StartingAnAccountLaunchesTheClientProcess()
    {
        StubController controller = new()
        {
            Role = "host",
            CanHostGame = false,
            Starter = (username, server) => null,
        };

        AgentApiResponse response = await Router(controller).HandleAsync(Post("/client/start", """{"username":"jey","server":"Alteon"}"""));
        JsonElement data = Data(response);

        Assert.Equal(200, response.Status);
        Assert.True(data.GetProperty("launched").GetBoolean());
        Assert.Equal("jey", data.GetProperty("username").GetString());
        Assert.Equal("Alteon", data.GetProperty("server").GetString());
        Assert.Equal(("jey", "Alteon"), controller.Started.Single());
    }

    [Fact]
    public async Task TheServerIsOptionalAndComesBackEmpty()
    {
        StubController controller = new() { Role = "host", Starter = (username, server) => null };

        AgentApiResponse response = await Router(controller).HandleAsync(Post("/client/start", """{"username":"jey"}"""));

        Assert.Equal(200, response.Status);
        Assert.Equal(string.Empty, Data(response).GetProperty("server").GetString());
        Assert.Equal(("jey", null), controller.Started.Single());
    }

    [Fact]
    public async Task ARefusalFromTheLauncherIsReportedInWords()
    {
        StubController controller = new() { Role = "host", Starter = (username, server) => "account_not_found: jey" };

        AgentApiResponse response = await Router(controller).HandleAsync(Post("/client/start", """{"username":"jey"}"""));

        Assert.Equal(409, response.Status);
        Assert.Equal("account_not_found: jey", response.Error);
    }

    [Fact]
    public async Task StartingNeedsAUserName()
    {
        StubController controller = new() { Role = "host" };

        AgentApiResponse response = await Router(controller).HandleAsync(Post("/client/start", """{"username":""}"""));

        Assert.Equal(400, response.Status);
        Assert.Empty(controller.Started);
    }

    [Fact]
    public async Task AClientProcessCannotStartAnotherClient()
    {
        StubController controller = new() { Role = "child", CanHostGame = true, Starter = (username, server) => null };

        AgentApiResponse response = await Router(controller).HandleAsync(Post("/client/start", """{"username":"jey"}"""));

        Assert.Equal(409, response.Status);
        Assert.StartsWith("client_already_running", response.Error);
        Assert.Empty(controller.Started);
    }

    private sealed class StubController : IClientController
    {
        public string Role { get; init; } = "host";
        public bool CanHostGame { get; init; }
        public IReadOnlyList<ClientWorker> Workers { get; init; } = Array.Empty<ClientWorker>();
        public Func<string, string?, string?>? Starter { get; init; }
        public List<(string Username, string? Server)> Started { get; } = new();

        public string? Start(string username, string? server)
        {
            Started.Add((username, server));
            return Starter?.Invoke(username, server);
        }
    }
}
