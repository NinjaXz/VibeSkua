using System.Text.Json;
using Skua.Core.Services.AgentApi;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// Script control: write a script, run it, pause it, stop it.
///
/// This is the loop an agent uses to change behaviour without restarting the
/// client, so the file-name rules and the "one script at a time" rules are the
/// parts worth pinning.
/// </summary>
public class AgentApiScriptApiTests : AgentApiContractTests
{
    private static string Body(object payload) => JsonSerializer.Serialize(payload, AgentJsonOptions);

    private AgentApiRequest PostScript(string path, object payload) => Post(path, Body(payload));

    /// <summary>Makes the fake script manager report a fixed running state.</summary>
    private void RunningScript(bool running = true) =>
        World.Set("Manager.ScriptRunning", (Func<bool>)(() => running));

    /// <summary>Makes Wait.ForTrue evaluate the predicate once instead of polling the game.</summary>
    private void WaitEvaluatesPredicate() =>
        World.Set("Wait.ForTrue", (Func<Func<bool>, int, int, bool>)((predicate, timeout, sleep) => predicate()));

    // ------------------------------------------------------------ /scripts

    [Fact]
    public async Task List_ReturnsCsFilesAndIgnoresEverythingElse()
    {
        Directory.CreateDirectory(Path.Combine(ScriptsRoot, "sub"));
        File.WriteAllText(Path.Combine(ScriptsRoot, "Farm.cs"), "// farm");
        File.WriteAllText(Path.Combine(ScriptsRoot, "sub", "Deep.cs"), "// deep");
        File.WriteAllText(Path.Combine(ScriptsRoot, "notes.txt"), "not a script");

        AgentApiResponse response = await Router().HandleAsync(Get("/scripts"));

        Assert.Equal(200, response.Status);
        JsonElement scripts = Json(response.Data!).GetProperty("scripts");
        Assert.Equal(2, scripts.GetArrayLength());
        Assert.Equal("Farm.cs", scripts[0].GetProperty("path").GetString());
        Assert.Equal("sub/Deep.cs", scripts[1].GetProperty("path").GetString());
        Assert.True(scripts[0].GetProperty("size").GetInt32() > 0);
    }

    [Fact]
    public async Task List_ReportsWhenTheFolderIsMissing()
    {
        // A fresh install has no script folder yet; that is a state, not an error.
        AgentApiOptions missing = new() { ScriptsRoot = Path.Combine(ScriptsRoot, "gone-" + Guid.NewGuid().ToString("N")) };

        AgentApiResponse response = await Router(missing).HandleAsync(Get("/scripts"));

        Assert.Equal(200, response.Status);
        Assert.False(Json(response.Data!).GetProperty("exists").GetBoolean());
    }

    // -------------------------------------------------------- /scripts/write

    [Fact]
    public async Task Write_CreatesTheFileInsideTheScriptsRoot()
    {
        AgentApiResponse response = await Router().HandleAsync(PostScript("/scripts/write", new { name = "Farm", content = "void Main() { }" }));

        Assert.Equal(200, response.Status);
        string path = Path.Combine(ScriptsRoot, "Farm.cs");
        Assert.True(File.Exists(path));
        Assert.Equal("void Main() { }", File.ReadAllText(path));
        JsonElement data = Json(response.Data!);
        Assert.Equal("Farm.cs", data.GetProperty("name").GetString());
        Assert.True(data.GetProperty("created").GetBoolean());
    }

    [Fact]
    public async Task Write_RejectsAnyNameThatCouldLeaveTheFolder()
    {
        AgentApiRouter router = Router();

        foreach (string name in new[] { "../evil", "a/../../x", "C:\\Windows\\x", "..", "Farm|Script" })
        {
            AgentApiResponse response = await router.HandleAsync(PostScript("/scripts/write", new { name, content = "x = 1;" }));
            Assert.True(response.Status == 400, $"name must be rejected: {name} -> {response.Status}");
        }

        Assert.Empty(Directory.GetFiles(ScriptsRoot));
    }

    [Fact]
    public async Task Write_ExistingFileNeedsTheOverwriteFlag()
    {
        AgentApiRouter router = Router();
        await router.HandleAsync(PostScript("/scripts/write", new { name = "Farm.cs", content = "first" }));

        AgentApiResponse blocked = await router.HandleAsync(PostScript("/scripts/write", new { name = "Farm.cs", content = "second" }));
        Assert.Equal(409, blocked.Status);
        Assert.Equal("first", File.ReadAllText(Path.Combine(ScriptsRoot, "Farm.cs")));

        AgentApiResponse replaced = await router.HandleAsync(PostScript("/scripts/write", new { name = "Farm.cs", content = "second", overwrite = true }));
        Assert.Equal(200, replaced.Status);
        Assert.Equal("second", File.ReadAllText(Path.Combine(ScriptsRoot, "Farm.cs")));
        Assert.True(Json(replaced.Data!).GetProperty("overwritten").GetBoolean());
    }

    [Fact]
    public async Task Write_RejectsContentOverTheSizeLimit()
    {
        AgentApiResponse response = await Router(Options(maxScriptBytes: 16)).HandleAsync(PostScript("/scripts/write", new { name = "Big.cs", content = new string('x', 40) }));

        Assert.Equal(413, response.Status);
        Assert.False(File.Exists(Path.Combine(ScriptsRoot, "Big.cs")));
    }

    [Fact]
    public async Task Write_LeavesNoTemporaryFileBehind()
    {
        await Router().HandleAsync(PostScript("/scripts/write", new { name = "Farm.cs", content = "one" }));
        await Router().HandleAsync(PostScript("/scripts/write", new { name = "Farm.cs", content = "two", overwrite = true }));

        Assert.Empty(Directory.GetFiles(ScriptsRoot, "*.tmp"));
    }

    [Fact]
    public async Task Write_RequiresContentAndAName()
    {
        AgentApiRouter router = Router();

        Assert.Equal(400, (await router.HandleAsync(PostScript("/scripts/write", new { name = "Farm.cs" }))).Status);
        Assert.Equal(400, (await router.HandleAsync(PostScript("/scripts/write", new { content = "x" }))).Status);
    }

    // ---------------------------------------------------------- /scripts/run

    [Fact]
    public async Task Run_LoadsTheScriptAndReportsThatItIsRunning()
    {
        File.WriteAllText(Path.Combine(ScriptsRoot, "Farm.cs"), "// farm");
        bool running = false;
        string? loaded = null;
        World.Set("Manager.ScriptRunning", (Func<bool>)(() => running));
        World.Set("Manager.SetLoadedScript", (Action<string>)(path => loaded = path));
        World.Set("Manager.StartScript", () =>
        {
            running = true;
            return Task.FromResult<Exception?>(null);
        });
        WaitEvaluatesPredicate();

        AgentApiResponse response = await Router().HandleAsync(PostScript("/scripts/run", new { name = "Farm.cs" }));

        Assert.Equal(200, response.Status);
        Assert.Equal(Path.Combine(ScriptsRoot, "Farm.cs"), loaded);
        Assert.True(World.WasCalled("Manager.StartScript"));
        JsonElement data = Json(response.Data!);
        Assert.True(data.GetProperty("started").GetBoolean());
        Assert.Equal("Farm.cs", data.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Run_ReportsTheCompilerErrorWhenTheScriptWillNotStart()
    {
        File.WriteAllText(Path.Combine(ScriptsRoot, "Broken.cs"), "not valid c#");
        World.Set("Manager.ScriptRunning", (Func<bool>)(() => false));
        World.Set("Manager.StartScript", () => Task.FromResult<Exception?>(new InvalidOperationException("CS1002: ; expected")));
        WaitEvaluatesPredicate();

        AgentApiResponse response = await Router().HandleAsync(PostScript("/scripts/run", new { name = "Broken.cs" }));

        Assert.Equal(500, response.Status);
        Assert.Contains("CS1002", response.Error);
    }

    [Fact]
    public async Task Run_UnknownScript_IsNotFound()
    {
        AgentApiResponse response = await Router().HandleAsync(PostScript("/scripts/run", new { name = "Missing.cs" }));

        Assert.Equal(404, response.Status);
    }

    [Fact]
    public async Task Run_WhileAScriptRuns_NeedsTheForceFlag()
    {
        File.WriteAllText(Path.Combine(ScriptsRoot, "Farm.cs"), "// farm");
        RunningScript();
        WaitEvaluatesPredicate();
        AgentApiRouter router = Router();

        AgentApiResponse blocked = await router.HandleAsync(PostScript("/scripts/run", new { name = "Farm.cs" }));
        Assert.Equal(409, blocked.Status);
        Assert.False(World.WasCalled("Manager.StopScript"));

        World.Set("Manager.StartScript", () => Task.FromResult<Exception?>(null));
        AgentApiResponse forced = await router.HandleAsync(PostScript("/scripts/run", new { name = "Farm.cs", force = true }));

        Assert.Equal(200, forced.Status);
        Assert.True(World.WasCalled("Manager.StopScript"));
    }

    // ------------------------------------------------- pause, resume, stop

    [Fact]
    public async Task Pause_AsksTheManagerAndReportsTheNewFlag()
    {
        RunningScript();
        bool? asked = null;
        World.Set("Manager.SetPausedAsync", (bool paused) =>
        {
            asked = paused;
            return Task.FromResult(true);
        });

        AgentApiResponse pause = await Router().HandleAsync(Post("/scripts/pause", "{}"));
        Assert.Equal(200, pause.Status);
        Assert.Equal(true, asked);
        Assert.True(Json(pause.Data!).GetProperty("paused").GetBoolean());

        AgentApiResponse resume = await Router().HandleAsync(Post("/scripts/resume", "{}"));
        Assert.Equal(200, resume.Status);
        Assert.Equal(false, asked);
    }

    [Fact]
    public async Task Pause_WithoutARunningScript_IsAConflict()
    {
        RunningScript(running: false);

        AgentApiResponse response = await Router().HandleAsync(Post("/pause", "{}"));

        Assert.Equal(409, response.Status);
    }

    [Fact]
    public async Task Stop_StopsTheRunningScript()
    {
        bool running = true;
        World.Set("Manager.ScriptRunning", (Func<bool>)(() => running));
        World.Set("Manager.StopScript", (bool graceful) =>
        {
            running = false;
            return default(ValueTask);
        });
        WaitEvaluatesPredicate();

        AgentApiResponse response = await Router().HandleAsync(Post("/scripts/stop", "{}"));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Manager.StopScript"));
        Assert.True(Json(response.Data!).GetProperty("stopped").GetBoolean());
    }

    [Fact]
    public async Task Stop_WithoutARunningScript_IsAConflict()
    {
        RunningScript(running: false);

        AgentApiResponse response = await Router().HandleAsync(Post("/stop", "{}"));

        Assert.Equal(409, response.Status);
    }

    // ----------------------------------------------------------- /script

    [Fact]
    public async Task ScriptStatus_ReportsTheRunningScriptAndItsLog()
    {
        RunningScript();
        World.Set("Manager.LoadedScript", Path.Combine(ScriptsRoot, "Farm.cs"));
        World.Set("Logs.GetLogs", new List<string> { "started", "at map cave" });

        AgentApiResponse response = await Router().HandleAsync(Get("/script"));

        Assert.Equal(200, response.Status);
        JsonElement data = Json(response.Data!);
        Assert.True(data.GetProperty("running").GetBoolean());
        Assert.Equal("Farm.cs", data.GetProperty("loadedScript").GetString());
        Assert.Equal(2, data.GetProperty("recentLogs").GetArrayLength());
    }
}
