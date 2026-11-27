using System.Text.Json;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using Skua.Core.Models.Monsters;
using Skua.Core.Models.Quests;
using Skua.Core.Services.AgentApi;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// Read-model tests: what an agent sees when it asks where it is and what is
/// around it. Shapes are pinned because the agent plans from these fields.
/// </summary>
public class AgentApiReadTests : AgentApiContractTests
{
    private JsonElement Data(AgentApiResponse response) => Json(response.Data!);

    private void Playing()
    {
        World.Set("Player.Playing", true);
        World.Set("Player.LoggedIn", true);
    }

    [Fact]
    public async Task Health_ReportsLoginAndPlayState()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Get("/health"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.Equal("vibeskoa-agent", data.GetProperty("name").GetString());
        Assert.True(data.GetProperty("loggedIn").GetBoolean());
        Assert.True(data.GetProperty("playing").GetBoolean());
    }

    [Fact]
    public async Task State_IncludesVitalsPositionMapTargetAndScript()
    {
        Playing();
        World.Set("Player.Level", 72);
        World.Set("Player.Health", 1400);
        World.Set("Player.MaxHealth", 1900);
        World.Set("Player.Cell", "Cave");
        World.Set("Player.Pad", "Entry");
        World.Set("Map.Name", "cave");
        World.Set("Map.RoomID", 4242);
        World.Set("Map.PlayerCount", 3);
        World.Set("Manager.ScriptRunning", true);
        World.Set("Manager.LoadedScript", Path.Combine(ScriptsRoot, "Farm.cs"));
        World.Set("Player.Target", new Monster { ID = 90, MapID = 90, Name = "Slime", HP = 0, MaxHP = 100, Cell = "Cave" });

        AgentApiResponse response = await Router().HandleAsync(Get("/state"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.Equal(72, data.GetProperty("level").GetInt32());
        Assert.Equal(1400, data.GetProperty("health").GetInt32());
        Assert.Equal("Cave", data.GetProperty("cell").GetString());
        Assert.Equal("cave", data.GetProperty("map").GetProperty("name").GetString());
        Assert.Equal(4242, data.GetProperty("map").GetProperty("roomId").GetInt32());
        Assert.Equal("Slime", data.GetProperty("target").GetProperty("name").GetString());
        Assert.True(data.GetProperty("script").GetProperty("running").GetBoolean());
        Assert.Equal("Farm.cs", data.GetProperty("script").GetProperty("loaded").GetString());
    }

    [Fact]
    public async Task State_HidesTargetWhenNothingIsSelected()
    {
        Playing();
        World.Set("Player.Target", null);

        AgentApiResponse response = await Router().HandleAsync(Get("/state"));

        Assert.Equal(JsonValueKind.Null, Data(response).GetProperty("target").ValueKind);
    }

    [Fact]
    public async Task Map_ReturnsCellsAndPlayers()
    {
        Playing();
        World.Set("Map.Name", "battleon");
        World.Set("Map.Cells", new List<string> { "Enter", "Grounds" });
        World.Set("Map.Players", new List<Models.Players.PlayerInfo>
        {
            new() { Name = "not_a_grill", EntID = 12, Level = 90, HP = 1500, MaxHP = 1900, Cell = "Enter", Pad = "Spawn", X = 100, Y = 200, State = 1, AFK = false },
        });

        AgentApiResponse response = await Router().HandleAsync(Get("/map"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.Equal(2, data.GetProperty("cells").GetArrayLength());
        Assert.Equal("not_a_grill", data.GetProperty("players")[0].GetProperty("name").GetString());
        Assert.Equal(12, data.GetProperty("players")[0].GetProperty("entId").GetInt32());
    }

    [Fact]
    public async Task Map_FallsBackToPlayerNamesWhenThePlayerListIsEmpty()
    {
        // The uoTree binding can be empty right after a map change while
        // areaUsers is already populated.
        Playing();
        World.Set("Map.Players", new List<Models.Players.PlayerInfo>());
        World.Set("Map.PlayerNames", new List<string> { "ghost" });
        World.Set("Map.GetPlayer.out", new Models.Players.PlayerInfo { Name = "ghost", Level = 1 });

        AgentApiResponse response = await Router().HandleAsync(Get("/map"));

        Assert.Equal(200, response.Status);
        Assert.Equal("ghost", Data(response).GetProperty("players")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Monsters_ReturnsCurrentCellAndWholeMap()
    {
        Playing();
        World.Set("Player.Cell", "Grounds");
        World.Set("Monsters.CurrentAvailableMonsters", new List<Monster> { new() { Name = "Slime", ID = 5, MapID = 5, Cell = "Grounds", HP = 10, MaxHP = 10 } });
        World.Set("Monsters.MapMonsters", new List<Monster> { new() { Name = "Slime", ID = 5, MapID = 5, Cell = "Grounds", HP = 10, MaxHP = 10 }, new() { Name = "Other", ID = 6, MapID = 6, Cell = "Cave", HP = 0, MaxHP = 10 } });

        AgentApiResponse response = await Router().HandleAsync(Get("/monsters"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.Equal(1, data.GetProperty("currentCell").GetArrayLength());
        Assert.Equal(2, data.GetProperty("all").GetArrayLength());
        Assert.False(data.GetProperty("all")[1].GetProperty("alive").GetBoolean());
    }

    [Fact]
    public async Task Servers_ListsWithConnectionState()
    {
        World.Set("Servers.IsConnected", true);
        World.Set("Servers.LastName", "Yorumi");
        World.Set("Servers.ServerList", new List<Models.Servers.Server>
        {
            new() { Name = "Yorumi", IP = "1.2.3.4", Port = 80, Online = true, PlayerCount = 120, MaxPlayers = 200, Lang = "EN", Upgrade = false },
        });

        AgentApiResponse response = await Router().HandleAsync(Get("/servers"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.True(data.GetProperty("connected").GetBoolean());
        Assert.Equal("Yorumi", data.GetProperty("servers")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Inventory_RequiresPlaying()
    {
        World.Set("Player.Playing", false);

        AgentApiResponse response = await Router().HandleAsync(Get("/inventory"));

        Assert.Equal(409, response.Status);
    }

    [Fact]
    public async Task Inventory_ListsItemsAndSlotUsage()
    {
        Playing();
        World.Set("Player.Gold", 50_000);
        World.Set("Player.Coins", 120);
        World.Set("Inventory.UsedSlots", 30);
        World.Set("Inventory.Slots", 40);
        World.Set("Inventory.Items", new List<InventoryItem>
        {
            new() { ID = 1301, Name = "Bland Blade", Quantity = 1, Equipped = true, MaxStack = 1 },
        });

        AgentApiResponse response = await Router().HandleAsync(Get("/inventory"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.Equal(50_000, data.GetProperty("gold").GetInt32());
        Assert.Equal(30, data.GetProperty("usedSlots").GetInt32());
        Assert.Equal("Bland Blade", data.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.True(data.GetProperty("items")[0].GetProperty("equipped").GetBoolean());
    }

    [Fact]
    public async Task Quests_SplitsActiveCompletedAndLoaded()
    {
        Playing();


        World.Set("Quests.Tree", new List<Quest>
        {
            new() { ID = 1, Name = "Active one", Status = "s", Level = 10 },
            new() { ID = 2, Name = "Done one", Status = "c", Level = 10 },
        });
        World.Set("Quests.Registered", new List<int> { 1 });
        World.Set("Quests.CachedDictionary", new Dictionary<int, QuestData> { [7] = new() { ID = 7, Name = "cached" } });

        AgentApiResponse response = await Router().HandleAsync(Get("/quests"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.Equal("Active one", data.GetProperty("active")[0].GetProperty("name").GetString());
        Assert.Equal("Done one", data.GetProperty("completed")[0].GetProperty("name").GetString());
        Assert.Equal(2, data.GetProperty("loaded").GetArrayLength());
        Assert.Equal(1, data.GetProperty("cachedCount").GetInt32());
    }

    [Fact]
    public async Task Quest_ReturnsStateFlagsForOneId()
    {
        Playing();
        World.Set("Quests.TryGetQuest.out", new Quest { ID = 12, Name = "Shadow Scythe", Status = "s", Level = 60 });
        World.Set("Quests.IsInProgress", true);
        World.Set("Quests.CanComplete", false);

        AgentApiResponse response = await Router().HandleAsync(Get("/quest?id=12"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.Equal("Shadow Scythe", data.GetProperty("quest").GetProperty("name").GetString());
        Assert.True(data.GetProperty("inProgress").GetBoolean());
        Assert.False(data.GetProperty("canComplete").GetBoolean());
    }

    [Fact]
    public async Task Quest_RequiresAPositiveId()
    {
        Playing();

        Assert.Equal(400, (await Router().HandleAsync(Get("/quest"))).Status);
        Assert.Equal(400, (await Router().HandleAsync(Get("/quest?id=0"))).Status);
    }

    [Fact]
    public async Task CachedQuests_PagesTheCatalogue()
    {
        World.Set("Quests.GetCachedQuests", new List<QuestData> { new() { ID = 1, Name = "a" }, new() { ID = 2, Name = "b" } });

        AgentApiResponse response = await Router().HandleAsync(Get("/quests/cached?start=1&count=2"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.Equal(1, data.GetProperty("start").GetInt32());
        Assert.Equal(2, data.GetProperty("returned").GetInt32());
    }

    [Fact]
    public async Task CachedQuests_ClampsCountAndDefaultsMissingArgs()
    {
        List<QuestData> page = new() { new() { ID = 1, Name = "a" } };
        World.Set("Quests.GetCachedQuests", page);

        AgentApiResponse response = await Router().HandleAsync(Get("/quests/cached?start=5&count=9999"));

        Assert.Equal(200, response.Status);
        Assert.Equal(200, Data(response).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Shop_ReturnsItemsWithCostAndRequirements()
    {
        Playing();
        World.Set("Shops.IsLoaded", true);
        World.Set("Shops.ID", 2283);
        World.Set("Shops.Name", "Nullshaper's Vault");
        Models.Shops.ShopItem item = new()
        {
            ID = 610010,
            ShopItemID = 4,
            Name = "Nullshaper's Champion",
            Description = "Sword",
            Quantity = 1,
            MaxStack = 1,
            Cost = 1_000_000,
            Level = 60,
            Coins = false,
            Requirements = new List<ItemBase> { new() { ID = 100, Name = "Token", Quantity = 5 } },
        };
        World.Set("Shops.Items", new List<Models.Shops.ShopItem> { item });

        AgentApiResponse response = await Router().HandleAsync(Get("/shop"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.Equal(2283, data.GetProperty("id").GetInt32());
        Assert.Equal(1_000_000, data.GetProperty("items")[0].GetProperty("cost").GetInt32());
        Assert.Equal("Token", data.GetProperty("items")[0].GetProperty("requirements")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Logs_ValidatesTypeAndClampsLimit()
    {
        World.Set("Logs.GetLogs", new List<string> { "one", "two", "three" });

        Assert.Equal(400, (await Router().HandleAsync(Get("/logs?type=nope"))).Status);

        AgentApiResponse response = await Router().HandleAsync(Get("/logs?type=script&limit=2"));
        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal(2, data.GetProperty("returned").GetInt32());
        Assert.Equal("two", data.GetProperty("entries")[0].GetString());

        AgentApiResponse clamped = await Router().HandleAsync(Get("/logs?limit=99999"));
        Assert.Equal(2000, Data(clamped).GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task Logs_DefaultsToScriptType()
    {
        World.Set("Logs.GetLogs", new List<string> { "hello" });

        AgentApiResponse response = await Router().HandleAsync(Get("/logs"));

        Assert.Equal(200, response.Status);
        Assert.Equal("script", Data(response).GetProperty("type").GetString());
    }

    [Fact]
    public async Task ScriptStatus_ReturnsLoadedScriptAndRecentLogs()
    {
        World.Set("Manager.ScriptRunning", true);
        World.Set("Manager.ScriptPaused", false);
        World.Set("Manager.LoadedScript", Path.Combine(ScriptsRoot, "Farm.cs"));
        World.Set("Logs.GetLogs", Enumerable.Range(1, 40).Select(i => "line" + i).ToList());

        AgentApiResponse response = await Router().HandleAsync(Get("/script"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.True(data.GetProperty("running").GetBoolean());
        Assert.Equal("Farm.cs", data.GetProperty("loadedScript").GetString());
        Assert.Equal(20, data.GetProperty("recentLogs").GetArrayLength());
        Assert.Equal("line40", data.GetProperty("recentLogs")[19].GetString());
    }

    [Fact]
    public async Task CompletedQuests_AreLightEnoughToDiffTwoAccounts()
    {
        // The campaign question is "who is behind on stories". That is a set
        // difference over completed quest ids, so the answer is ids, sorted.
        World.Set("Quests.Tree", new List<Quest>
        {
            new() { ID = 30, Name = "Gather Water", Status = "c" },
            new() { ID = 12, Name = "Shadow Scythe", Status = "c" },
            new() { ID = 7, Name = "Still Going", Status = "s" },
        });

        AgentApiResponse response = await Router().HandleAsync(Get("/quests/completed"));

        Assert.Equal(200, response.Status);
        JsonElement data = Json(response.Data!);
        Assert.Equal(2, data.GetProperty("count").GetInt32());
        Assert.Equal(12, data.GetProperty("ids")[0].GetInt32());
        Assert.Equal(30, data.GetProperty("ids")[1].GetInt32());
    }

    [Fact]
    public async Task CompletedQuests_AnEmptyTreeIsAZeroNotAnError()
    {
        AgentApiResponse response = await Router().HandleAsync(Get("/quests/completed"));

        Assert.Equal(200, response.Status);
        Assert.Equal(0, Json(response.Data!).GetProperty("count").GetInt32());
    }
}
