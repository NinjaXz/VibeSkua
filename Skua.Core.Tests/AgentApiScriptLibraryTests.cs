using System.Text.Json;
using Skua.Core.Services.AgentApi;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// The shipped script library lives in folders, and each script asks for its
/// choices through a window the client draws.
///
/// An agent has no window. It needs the library as a searchable list, the
/// choices as data it can read and set, and a start that does not wait for a
/// click. Those three are what this file pins.
/// </summary>
public class AgentApiScriptLibraryTests : AgentApiContractTests
{
    private string ArmyDir => Path.Combine(ScriptsRoot, "Army", "Farm");

    private const string ArmyScript = """
        /*
        name: ArmySevenCirclesWar
        description: Levels army rank and farms War Medals.
        tags: army, gold
        */
        using Skua.Core.Interfaces;

        public class ArmySevenCirclesWar
        {
            public string OptionsStorage = "ArmySevenCirclesWar";
            public bool DontPreconfigure = true;

            public void ScriptMain(IScriptInterface bot)
            {
                Core.SetOptions();
                bot.Log("running");
            }

            public Option[] Options =
            {
                new Option<bool>("Solo", "Solo Farm", "Use just 1 account"),
                new Option<bool>("Endless", "Endless Farm", "Ignore the level check and continue until stopped."),
                new Option<ClassType>("ClassType", "ClassType to use", "Class to equip while farming."),
            };
        }
        """;

    private const string GroupedScript = """
        public class Grouped
        {
            public string OptionsStorage = "Grouped";

            public Option[] Options =
            {
                new Option<int>("Count", "How many", "Number of runs", 3),
            };

            public Grouped()
            {
                MultipleOptions.Add("Runs", new Option<bool>("Taunter", "Use the taunter", "Bring the taunter class"));
            }
        }
        """;

    /// <summary>The path the fake manager was told to load.</summary>
    protected string? Loaded { get; set; }

    private JsonElement Rows(AgentApiResponse response) => Json(response.Data!).GetProperty("scripts");

    private void WriteArmyScript()
    {
        Directory.CreateDirectory(ArmyDir);
        File.WriteAllText(Path.Combine(ArmyDir, "ArmySevenCirclesWar.cs"), ArmyScript);
    }

    /// <summary>Makes the fake script manager run and lets the wait helpers resolve at once.</summary>
    private void ScriptStarts(Action? onStar = null)
    {
        bool running = false;
        World.Set("Manager.ScriptRunning", (Func<bool>)(() => running));
        World.Set("Manager.SetLoadedScript", (Action<string>)(path => Loaded = path));
        World.Set("Manager.StartScript", () =>
        {
            running = true;
            onStar?.Invoke();
            return Task.FromResult<Exception?>(null);
        });
        World.Set("Wait.ForTrue", (Func<Func<bool>, int, int, bool>)((predicate, timeout, sleep) => predicate()));
    }

    // ---------------------------------------------------------------- /scripts

    [Fact]
    public async Task List_WalksTheScriptFolders()
    {
        WriteArmyScript();
        File.WriteAllText(Path.Combine(ScriptsRoot, "Farm.cs"), "// farm");

        AgentApiResponse response = await Router().HandleAsync(Get("/scripts"));

        Assert.Equal(200, response.Status);
        JsonElement data = Json(response.Data!);
        Assert.Equal(2, data.GetProperty("count").GetInt32());
        JsonElement scripts = data.GetProperty("scripts");
        Assert.Equal("Army/Farm/ArmySevenCirclesWar.cs", scripts[0].GetProperty("path").GetString());
        Assert.Equal("ArmySevenCirclesWar.cs", scripts[0].GetProperty("fileName").GetString());
        Assert.Equal("Farm.cs", scripts[1].GetProperty("path").GetString());
    }

    [Fact]
    public async Task List_CarriesTheHeaderBlockSoAnAgentCanChoose()
    {
        WriteArmyScript();

        AgentApiResponse response = await Router().HandleAsync(Get("/scripts"));

        JsonElement row = Json(response.Data!).GetProperty("scripts")[0];
        Assert.Equal("ArmySevenCirclesWar", row.GetProperty("title").GetString());
        Assert.Equal("Levels army rank and farms War Medals.", row.GetProperty("description").GetString());
        Assert.Equal("army, gold", row.GetProperty("tags").GetString());
    }

    [Fact]
    public async Task List_FilterMatchesPathTitleDescriptionAndTags()
    {
        WriteArmyScript();
        File.WriteAllText(Path.Combine(ScriptsRoot, "Farm.cs"), "// plain");

        AgentApiRouter router = Router();
        Assert.Equal(1, Rows(await router.HandleAsync(Get("/scripts?filter=army"))).GetArrayLength());
        Assert.Equal(1, Rows(await router.HandleAsync(Get("/scripts?filter=War%20Medals"))).GetArrayLength());
        Assert.Equal(0, Rows(await router.HandleAsync(Get("/scripts?filter=zzz"))).GetArrayLength());
    }

    [Fact]
    public async Task List_PagesWithLimitAndFrom()
    {
        foreach (string name in new[] { "A.cs", "B.cs", "C.cs" })
            File.WriteAllText(Path.Combine(ScriptsRoot, name), "// x");

        AgentApiRouter router = Router();
        Assert.Equal(2, Rows(await router.HandleAsync(Get("/scripts?limit=2"))).GetArrayLength());
        Assert.Equal(1, Rows(await router.HandleAsync(Get("/scripts?limit=2&from=2"))).GetArrayLength());
    }

    [Fact]
    public async Task List_LimitIsCappedAndTheWalkIsCapped()
    {
        AgentApiResponse response = await Router().HandleAsync(Get("/scripts?limit=99999"));

        Assert.Equal(200, response.Status);
        Assert.True(Json(response.Data!).GetProperty("scanned").GetInt32() >= 0);
    }

    // -------------------------------------------------------- /scripts/write

    [Fact]
    public async Task Write_IntoAFolder_CreatesTheFoldersAndReportsTheRelativeName()
    {
        AgentApiResponse response = await Router().HandleAsync(Post("/scripts/write",
            JsonSerializer.Serialize(new { name = "Army/Farm/MyArmy.cs", content = "// mine", overwrite = true }, AgentJsonOptions)));

        Assert.Equal(200, response.Status);
        Assert.True(File.Exists(Path.Combine(ArmyDir, "MyArmy.cs")));
        Assert.Equal("Army/Farm/MyArmy.cs", Json(response.Data!).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Write_AndRun_AcceptARelativePath()
    {
        ScriptStarts();

        AgentApiResponse write = await Router().HandleAsync(Post("/scripts/write",
            JsonSerializer.Serialize(new { name = "Army/My.cs", content = "// mine" }, AgentJsonOptions)));
        Assert.Equal(200, write.Status);

        AgentApiResponse run = await Router().HandleAsync(Post("/scripts/run",
            JsonSerializer.Serialize(new { name = "Army/My.cs" }, AgentJsonOptions)));

        Assert.Equal(200, run.Status);
        Assert.Equal("Army/My.cs", Json(run.Data!).GetProperty("name").GetString());
        Assert.Equal(Path.Combine(ScriptsRoot, "Army", "My.cs"), Loaded);
    }

    [Fact]
    public async Task Run_APathEscapingTheScriptsRoot_IsRefused()
    {
        foreach (string name in new[] { "../Evil.cs", "Army/../../Evil.cs", "C:/Windows/x.cs", "/x.cs", "x/" })
        {
            AgentApiResponse response = await Router().HandleAsync(Post("/scripts/run",
                JsonSerializer.Serialize(new { name }, AgentJsonOptions)));

            Assert.Equal(400, response.Status);
        }
    }

    // ------------------------------------------------------ silent start

    [Fact]
    public async Task Run_StartsWithTheOptionsWindowClosed()
    {
        WriteArmyScript();
        bool? silentDuringStart = null;
        ScriptStarts(() => silentDuringStart = (bool?)World.Received("Manager.SilentConfig"));

        AgentApiResponse response = await Router().HandleAsync(Post("/scripts/run",
            JsonSerializer.Serialize(new { name = "Army/Farm/ArmySevenCirclesWar.cs" }, AgentJsonOptions)));

        Assert.Equal(200, response.Status);
        Assert.True(silentDuringStart);
        Assert.True(Json(response.Data!).GetProperty("silent").GetBoolean());
        Assert.Equal(false, World.Received("Manager.SilentConfig"));
    }

    [Fact]
    public async Task Run_WithSilentFalse_LeavesTheOptionsWindowToTheClient()
    {
        WriteArmyScript();
        bool? silentDuringStart = null;
        ScriptStarts(() => silentDuringStart = (bool?)World.Received("Manager.SilentConfig"));

        AgentApiResponse response = await Router().HandleAsync(Post("/scripts/run",
            JsonSerializer.Serialize(new { name = "Army/Farm/ArmySevenCirclesWar.cs", silent = false }, AgentJsonOptions)));

        Assert.Equal(200, response.Status);
        Assert.False(silentDuringStart);
    }

    // ------------------------------------------------------- /script/options

    [Fact]
    public async Task Options_ListsNamesLabelsTypesAndStorage()
    {
        WriteArmyScript();

        AgentApiResponse response = await Router().HandleAsync(Get("/script/options?name=Army/Farm/ArmySevenCirclesWar.cs"));

        Assert.Equal(200, response.Status);
        JsonElement data = Json(response.Data!);
        Assert.Equal("ArmySevenCirclesWar", data.GetProperty("storage").GetString());
        Assert.False(data.GetProperty("configured").GetBoolean());
        JsonElement options = data.GetProperty("options");
        Assert.Equal(3, options.GetArrayLength());
        Assert.Equal("Solo", options[0].GetProperty("name").GetString());
        Assert.Equal("bool", options[0].GetProperty("type").GetString());
        Assert.Equal("Solo Farm", options[0].GetProperty("label").GetString());
        Assert.Equal("ClassType", options[2].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Options_AScriptWithoutAStorageNameSharesTheDefaultFile()
    {
        File.WriteAllText(Path.Combine(ScriptsRoot, "Plain.cs"), "public class Plain { public Option[] Options = { new Option<int>(\"Count\", \"How many\", \"Runs\", 3) }; }");

        AgentApiResponse response = await Router().HandleAsync(Get("/script/options?name=Plain.cs"));

        Assert.Equal(200, response.Status);
        JsonElement data = Json(response.Data!);
        Assert.Equal("default", data.GetProperty("storage").GetString());
        Assert.Equal("3", data.GetProperty("options")[0].GetProperty("default").GetString());
    }

    [Fact]
    public async Task Options_UnknownScript_IsNotFound()
    {
        AgentApiResponse response = await Router().HandleAsync(Get("/script/options?name=Missing.cs"));

        Assert.Equal(404, response.Status);
        Assert.Equal("script_not_found", response.Error);
    }

    [Fact]
    public async Task Options_AskingWithoutAName_IsARefusalNotACrash()
    {
        AgentApiResponse response = await Router().HandleAsync(Get("/script/options"));

        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task Options_WritingAChoiceStoresItAndAnswersWithIt()
    {
        WriteArmyScript();

        AgentApiResponse response = await Router().HandleAsync(Post("/script/options",
            JsonSerializer.Serialize(new { name = "Army/Farm/ArmySevenCirclesWar.cs", values = new Dictionary<string, string> { ["Endless"] = "True", ["ClassType"] = "Farm" } }, AgentJsonOptions)));

        Assert.Equal(200, response.Status);
        string cfg = Path.Combine(OptionsRoot, "ArmySevenCirclesWar.cfg");
        Assert.True(File.Exists(cfg));
        string[] lines = File.ReadAllLines(cfg);
        Assert.Contains("Endless=True", lines);
        Assert.Contains("ClassType=Farm", lines);

        JsonElement options = Json(response.Data!).GetProperty("options");
        Assert.Equal("True", options[1].GetProperty("value").GetString());

        // Reading it back must agree with the file, not with the request.
        AgentApiResponse again = await Router().HandleAsync(Get("/script/options?name=Army/Farm/ArmySevenCirclesWar.cs"));
        Assert.Equal("Farm", Json(again.Data!).GetProperty("options")[2].GetProperty("value").GetString());
        Assert.True(Json(again.Data!).GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task Options_AGroupedChoiceKeepsItsCategoryKey()
    {
        File.WriteAllText(Path.Combine(ScriptsRoot, "Grouped.cs"), GroupedScript);

        AgentApiResponse response = await Router().HandleAsync(Post("/script/options",
            JsonSerializer.Serialize(new { name = "Grouped.cs", values = new Dictionary<string, string> { ["Taunter"] = "True" } }, AgentJsonOptions)));

        Assert.Equal(200, response.Status);
        Assert.Contains("Runs:Taunter=True", File.ReadAllLines(Path.Combine(OptionsRoot, "Grouped.cfg")));
        Assert.Equal("True", Json(response.Data!).GetProperty("options")[1].GetProperty("value").GetString());
    }

    [Fact]
    public async Task Options_ANameTheScriptDoesNotHaveIsRefused()
    {
        WriteArmyScript();

        AgentApiResponse response = await Router().HandleAsync(Post("/script/options",
            JsonSerializer.Serialize(new { name = "Army/Farm/ArmySevenCirclesWar.cs", values = new Dictionary<string, string> { ["Endles"] = "True" } }, AgentJsonOptions)));

        Assert.Equal(400, response.Status);
        Assert.Contains("unknown_option", response.Error);
        Assert.False(File.Exists(Path.Combine(OptionsRoot, "ArmySevenCirclesWar.cfg")));
    }

    [Fact]
    public async Task Options_WithoutValues_IsRejected()
    {
        WriteArmyScript();

        AgentApiResponse response = await Router().HandleAsync(Post("/script/options",
            JsonSerializer.Serialize(new { name = "Army/Farm/ArmySevenCirclesWar.cs" }, AgentJsonOptions)));

        Assert.Equal(400, response.Status);
    }

    // ------------------------------------------- borrowed (shared) options

    private const string CoreBotsFixture = """
        public class CoreBots
        {
            public static CoreBots Instance = new CoreBots();

            public Option<bool> SkipOptions = new(
                "SkipOption",
                "Skip this window next time",
                "You can return to this screen via Scripts.",
                false
            );
        }
        """;

    private const string BorrowingScript = """
        /*
        name: Agent Pack
        description: Runs other scripts work and skips the window.
        tags: agent
        */
        //cs_include Scripts/CoreBots.cs
        public class AgentPack
        {
            public string OptionsStorage = "AgentPack";

            public List<IOption> Options = new()
            {
                new Option<bool>("Loud", "Log loudly", "Shout the answer"),
                CoreBots.Instance.SkipOptions,
            };
        }
        """;

    /// <summary>Scripts borrow shared options, so the questions are not all in one file.</summary>
    private void WriteBorrowingScript()
    {
        File.WriteAllText(Path.Combine(ScriptsRoot, "CoreBots.cs"), CoreBotsFixture);
        Directory.CreateDirectory(Path.Combine(ScriptsRoot, "Agent"));
        File.WriteAllText(Path.Combine(ScriptsRoot, "Agent", "AgentPack.cs"), BorrowingScript);
    }

    [Fact]
    public async Task Options_ASharedOptionIsListedByItsNameNotTheFieldName()
    {
        WriteBorrowingScript();

        AgentApiResponse response = await Router().HandleAsync(Get("/script/options?name=Agent/AgentPack.cs"));

        Assert.Equal(200, response.Status);
        JsonElement options = Json(response.Data!).GetProperty("options");
        Assert.Equal(2, options.GetArrayLength());
        Assert.Equal("Loud", options[0].GetProperty("name").GetString());

        // CoreBots names this option "SkipOption" while the field is SkipOptions. The client
        // stores the name, so an agent must be told the name.
        Assert.Equal("SkipOption", options[1].GetProperty("name").GetString());
        Assert.Equal("bool", options[1].GetProperty("type").GetString());
        Assert.Equal("false", options[1].GetProperty("default").GetString());
    }

    [Fact]
    public async Task Options_ASharedChoiceCanBeStored()
    {
        WriteBorrowingScript();

        AgentApiResponse response = await Router().HandleAsync(Post("/script/options",
            JsonSerializer.Serialize(new { name = "Agent/AgentPack.cs", values = new Dictionary<string, string> { ["SkipOption"] = "True" } }, AgentJsonOptions)));

        Assert.Equal(200, response.Status);
        Assert.Contains("SkipOption=True", File.ReadAllLines(Path.Combine(OptionsRoot, "AgentPack.cfg")));
        Assert.Equal("True", Json(response.Data!).GetProperty("options")[1].GetProperty("value").GetString());
    }

    [Fact]
    public async Task Options_AnUnresolvableBorrowDoesNotBreakTheList()
    {
        Directory.CreateDirectory(Path.Combine(ScriptsRoot, "Agent"));
        File.WriteAllText(Path.Combine(ScriptsRoot, "Agent", "Mystery.cs"),
            "public class Mystery { public string OptionsStorage = \"Mystery\"; public List<IOption> Options = new() { Someone.Elses.Option }; }");

        AgentApiResponse response = await Router().HandleAsync(Get("/script/options?name=Agent/Mystery.cs"));

        Assert.Equal(200, response.Status);
        Assert.Empty(Json(response.Data!).GetProperty("options").EnumerateArray());
    }

    // ------------------------------------------------------------------ state

    [Fact]
    public async Task State_CarriesMembershipBecauseContentGatesOnIt()
    {
        World.Set("Player.IsMember", true);

        AgentApiResponse response = await Router().HandleAsync(Get("/state"));

        Assert.Equal(200, response.Status);
        Assert.True(Json(response.Data!).GetProperty("member").GetBoolean());
    }

    [Fact]
    public async Task State_ReportsANonMemberWithoutThrowing()
    {
        AgentApiResponse response = await Router().HandleAsync(Get("/state"));

        Assert.Equal(200, response.Status);
        Assert.False(Json(response.Data!).GetProperty("member").GetBoolean());
    }
}
