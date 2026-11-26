using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Services.AgentApi;
using Skua.Core.Tests.Fakes;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// What the client does with the switch: start the endpoint, leave it off, or
/// start nothing when the settings cannot be honoured.
///
/// The rule under test is that a wrong setting costs the endpoint, never the
/// client: nothing here may throw.
/// </summary>
public sealed class AgentApiServiceTests : IDisposable
{
    private readonly FakeWorld _world = new();
    private readonly ClientSettings _settings = new();
    private readonly StubProvider _provider;

    public AgentApiServiceTests()
    {
        _world.Set("Settings.GetClient", _settings);
        _provider = new StubProvider(_world);
        Environment.SetEnvironmentVariable("VIBESKUA_AGENT_ENABLED", null);
        Environment.SetEnvironmentVariable("VIBESKUA_AGENT_TOKEN", null);
        Environment.SetEnvironmentVariable("VIBESKUA_AGENT_PORT", null);
        Environment.SetEnvironmentVariable("VIBESKUA_AGENT_BIND", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("VIBESKUA_AGENT_ENABLED", null);
        Environment.SetEnvironmentVariable("VIBESKUA_AGENT_TOKEN", null);
        Environment.SetEnvironmentVariable("VIBESKUA_AGENT_PORT", null);
        Environment.SetEnvironmentVariable("VIBESKUA_AGENT_BIND", null);
    }

    private AgentApiService Service() => new(_world.Service<ISettingsService>("Settings"), _provider);

    private static int FreePort()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public void SwitchedOff_BindsNothingAndSaysNothing()
    {
        _settings.AgentApiEnabled = false;
        _settings.AgentApiPort = FreePort();

        using AgentApiService service = Service();
        service.Start();

        Assert.False(service.IsRunning);
        Assert.Equal(0, _world.CallCount("Logs.DebugLog"));
        Assert.Equal(_settings.AgentApiPort, service.Port);
    }

    [Fact]
    public async Task LoopbackWithNoToken_ServesHealth()
    {
        _settings.AgentApiEnabled = true;
        _settings.AgentApiBind = "127.0.0.1";
        _settings.AgentApiPort = FreePort();

        using AgentApiService service = Service();
        service.Start();
        Assert.True(service.IsRunning);

        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
        using HttpResponseMessage response = await http.GetAsync($"http://127.0.0.1:{service.Port}/health");
        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("vibeskoa-agent", root.GetProperty("data").GetProperty("name").GetString());

        service.Stop();
        Assert.False(service.IsRunning);
    }

    [Fact]
    public void TokenFromSettings_IsEnforcedOnTheWire()
    {
        _settings.AgentApiEnabled = true;
        _settings.AgentApiBind = "127.0.0.1";
        _settings.AgentApiPort = FreePort();
        _settings.AgentApiToken = "a-secret-worth-checking";

        using AgentApiService service = Service();
        service.Start();

        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
        using HttpRequestMessage request = new(HttpMethod.Get, $"http://127.0.0.1:{service.Port}/state");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer wrong");
        using HttpResponseMessage denied = http.Send(request);

        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public void APortAnotherProgramHoldsIsLoggedNotThrown()
    {
        int port = FreePort();
        using TcpListener squatter = new(IPAddress.Loopback, port);
        squatter.Start();

        _settings.AgentApiEnabled = true;
        _settings.AgentApiBind = "127.0.0.1";
        _settings.AgentApiPort = port;

        using AgentApiService service = Service();
        service.Start();

        Assert.False(service.IsRunning);
        Assert.True(_world.WasCalled("Logs.DebugLog"));
        squatter.Stop();
    }

    [Fact]
    public void ANonLoopbackBindWithoutATokenIsRefused()
    {
        _settings.AgentApiEnabled = true;
        _settings.AgentApiBind = "0.0.0.0";
        _settings.AgentApiPort = FreePort();
        _settings.AgentApiToken = string.Empty;

        using AgentApiService service = Service();
        service.Start();

        Assert.False(service.IsRunning);
        Assert.True(_world.WasCalled("Logs.DebugLog"));
    }

    [Fact]
    public void TheEnvironmentCanTurnTheEndpointOnForOneRun()
    {
        _settings.AgentApiEnabled = false;
        _settings.AgentApiBind = "127.0.0.1";
        _settings.AgentApiPort = FreePort();
        Environment.SetEnvironmentVariable("VIBESKUA_AGENT_ENABLED", "1");

        using AgentApiService service = Service();
        service.Start();

        Assert.True(service.IsRunning);
    }

    [Fact]
    public void StopAfterStopIsHarmless()
    {
        _settings.AgentApiEnabled = true;
        _settings.AgentApiBind = "127.0.0.1";
        _settings.AgentApiPort = FreePort();

        AgentApiService service = Service();
        service.Start();
        service.Stop();
        service.Stop();
        service.Dispose();

        Assert.False(service.IsRunning);
    }

    [Fact]
    public void OptionsFollowTheSavedSettings()
    {
        _settings.AgentApiEnabled = true;
        _settings.AgentApiBind = "127.0.0.5";
        _settings.AgentApiPort = 9123;
        _settings.AgentApiToken = "  spaced-token  ";

        AgentApiOptions options = AgentApiOptions.FromClientSettings(_settings);

        Assert.True(options.Enabled);
        Assert.Equal("127.0.0.5", options.Bind);
        Assert.Equal(9123, options.Port);
        Assert.Equal("spaced-token", options.Token);
        Assert.True(options.IsLoopback);
    }

    [Fact]
    public void ASettingsPortOfZeroFallsBackToTheDefault()
    {
        _settings.AgentApiPort = 0;
        _settings.InitializeDefaults();

        Assert.Equal(8766, _settings.AgentApiPort);
        Assert.Equal(8766, AgentApiOptions.FromClientSettings(_settings).Port);
    }

    private sealed class StubProvider(FakeWorld world) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IScriptInterface))
                return world.Bot;
            if (serviceType == typeof(IScriptManager))
                return world.Manager;
            if (serviceType == typeof(ILogService))
                return world.Logs;
            return null;
        }
    }
}
