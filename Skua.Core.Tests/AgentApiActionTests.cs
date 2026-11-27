using System.Text.Json;
using Skua.Core.Models;
using Skua.Core.Models.Quests;
using Skua.Core.Services.AgentApi;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// Behaviour tests for the mutating routes: what the bot is told to do, and
/// what the client is told back.
/// </summary>
public class AgentApiActionTests : AgentApiContractTests
{
    private void Playing()
    {
        World.Set("Player.Playing", true);
        World.Set("Player.LoggedIn", true);
    }

    private JsonElement Data(AgentApiResponse response) => Json(response.Data!);

    // ---------------------------------------------------------- quest state

    [Fact]
    public async Task QuestCheck_AskedTheGameAndComparedTheSlotValue()
    {
        // A quest finished long ago comes back with status null, so status cannot say
        // "done". The library asks the game for the slot value instead (CoreBots.cs:4890).
        Playing();
        World.Set("Quests.Tree", new List<Quest>
        {
            new() { ID = 3878, Name = "Defeat Drakath!", Slot = 12, Value = 1 },
            new() { ID = 3880, Name = "Defeat the 13th Lord of Chaos", Slot = 13, Value = 1 },
        });
        List<string> asked = new();
        World.Set("Flash.CallGameFunction", new Func<string, object[], int>((fn, gameArgs) =>
        {
            asked.Add(fn);
            return gameArgs.Length > 0 && Convert.ToInt32(gameArgs[0]!) == 12 ? 1 : 0;
        }));

        AgentApiResponse response = await Router().HandleAsync(
            Post("/quests/check", """{"ids":[3878,3880]}"""));

        Assert.Equal(200, response.Status);
        JsonElement quests = Data(response).GetProperty("quests");
        Assert.True(quests[0].GetProperty("complete").GetBoolean());
        Assert.Equal("world.getQuestValue", asked[0]);
        Assert.False(quests[1].GetProperty("complete").GetBoolean());
    }

    [Fact]
    public async Task QuestCheck_AQuestWithNoSlotIsDoneWithoutAsking()
    {
        Playing();
        World.Set("Quests.Tree", new List<Quest> { new() { ID = 7, Name = "No Slot", Slot = -1, Value = 1 } });

        AgentApiResponse response = await Router().HandleAsync(Post("/quests/check", """{"ids":[7]}"""));

        Assert.Equal(200, response.Status);
        Assert.True(Data(response).GetProperty("quests")[0].GetProperty("complete").GetBoolean());
        Assert.Equal(0, World.CallCount("Flash.CallGameFunction"));
    }

    [Fact]
    public async Task QuestCheck_AQuestTheLoadNeverFoundIsReportedNotGuessed()
    {
        Playing();
        World.Set("Quests.Tree", new List<Quest>());

        AgentApiResponse response = await Router().HandleAsync(Post("/quests/check", """{"ids":[4242]}"""));

        Assert.Equal(200, response.Status);
        JsonElement row = Data(response).GetProperty("quests")[0];
        Assert.False(row.GetProperty("complete").GetBoolean());
        Assert.Equal("not_loaded", row.GetProperty("state").GetString());
    }

    [Fact]
    public async Task QuestCheck_ABigLandmarkListIsAcceptedAndAnEmptyOneIsNot()
    {
        Playing();
        World.Set("Quests.Tree", new List<Quest>());

        AgentApiResponse emptyResponse = await Router().HandleAsync(Post("/quests/check", """{"ids":[]}"""));
        Assert.Equal(400, emptyResponse.Status);

        string many = string.Join(",", Enumerable.Range(1, 301));
        AgentApiResponse response = await Router().HandleAsync(Post("/quests/check", $$"""{"ids":[{{many}}]}"""));
        Assert.Equal(400, response.Status);
        Assert.Contains("300", response.Error ?? "");
    }

    // ------------------------------------------------------------- movement

    [Fact]
    public async Task Jump_PassesCellAndPad_AndReturnsNewState()
    {
        Playing();
        World.Set("Player.Cell", "Cave");
        World.Set("Player.Pad", "Entry");

        AgentApiResponse response = await Router().HandleAsync(Post("/jump", """{"cell":"Cave","pad":"Entry"}"""));

        Assert.Equal(200, response.Status);
        Assert.Equal(1, World.CallCount("Map.Jump"));
        Assert.Equal("Cave", Data(response).GetProperty("cell").GetString());
        Assert.Equal("Entry", Data(response).GetProperty("pad").GetString());
    }

    [Fact]
    public async Task Jump_RequiresCell()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/jump", """{"cell":""}"""));

        Assert.Equal(400, response.Status);
        Assert.Contains("cell", response.Error);
    }

    [Fact]
    public async Task Join_UsesMapCellAndPad()
    {
        Playing();
        string? joined = null;
        World.Set("Map.Join", (string map, string cell, string pad, bool ignoreCheck, bool autoCorrect) =>
        {
            joined = $"{map}|{cell}|{pad}|{ignoreCheck}";
            return Task.CompletedTask;
        });

        AgentApiResponse response = await Router().HandleAsync(Post("/join", """{"map":"battleon-12345","cell":"Enter","pad":"Spawn","ignoreCheck":true}"""));

        Assert.Equal(200, response.Status);
        Assert.Equal("battleon-12345|Enter|Spawn|True", joined);
    }

    [Fact]
    public async Task Join_RejectsOverlongMapName()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/join", "{}"));
        Assert.Equal(400, response.Status);

        response = await Router().HandleAsync(Post("/join", "{\"map\":\"" + new string('m', 97) + "\"}"));
        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task Walk_ClampsSpeedOutOfRange()
    {
        Playing();
        int recordedSpeed = 0;
        World.Set("Player.WalkTo", (int x, int y, int speed) =>
        {
            recordedSpeed = speed;
            return Task.CompletedTask;
        });

        AgentApiResponse response = await Router().HandleAsync(Post("/walk", """{"x":10,"y":20,"speed":999}"""));

        Assert.Equal(200, response.Status);
        Assert.Equal(8, recordedSpeed);
    }

    [Fact]
    public async Task Walk_RejectsCoordinatesOutOfRange()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/walk", """{"x":999999,"y":0}"""));

        Assert.Equal(400, response.Status);
        Assert.False(World.WasCalled("Player.WalkTo"));
    }

    [Fact]
    public async Task WalkSpeed_RejectsValueOutsideOneToFifty()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/walk-speed", """{"speed":51}"""));

        Assert.Equal(400, response.Status);
        Assert.Equal("speed must be between 1 and 50", response.Error);
    }

    [Fact]
    public async Task WalkSpeed_SetsPlayerWalkSpeed()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/walk-speed", """{"speed":20}"""));

        Assert.Equal(200, response.Status);
        Assert.Equal(20, World.Received("Player.WalkSpeed"));
    }

    [Fact]
    public async Task Goto_RequiresPlayerName()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/goto", """{"player":""}"""));

        Assert.Equal(400, response.Status);
    }

    // --------------------------------------------------------------- combat

    [Fact]
    public async Task Attack_NumericMonster_UsesIdOverload()
    {
        Playing();
        int idUsed = 0;
        World.Set("Combat.Attack", (int id) =>
        {
            idUsed = id;
            return true;
        });

        AgentApiResponse response = await Router().HandleAsync(Post("/attack", """{"monster":"2423"}"""));

        Assert.Equal(200, response.Status);
        Assert.Equal(2423, idUsed);
        Assert.True(Data(response).GetProperty("attacked").GetBoolean());
    }

    [Fact]
    public async Task Attack_NameMonster_UsesNameOverload()
    {
        Playing();
        string? nameUsed = null;
        World.Set("Combat.Attack", (string name) =>
        {
            nameUsed = name;
            return true;
        });

        AgentApiResponse response = await Router().HandleAsync(Post("/attack", """{"monster":"Rising Sun"}"""));

        Assert.Equal(200, response.Status);
        Assert.Equal("Rising Sun", nameUsed);
    }

    [Fact]
    public async Task Kill_TimesOutAndReportsIt()
    {
        Playing();
        World.Set("Kill.Monster", (string name, CancellationToken? token) =>
        {
            token!.Value.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            return token!.Value.IsCancellationRequested ? throw new OperationCanceledException(token!.Value) : Task.CompletedTask;
        });

        AgentApiResponse response = await Router().HandleAsync(Post("/kill", """{"monster":"Slime","timeoutMs":1000}"""));

        Assert.Equal(200, response.Status);
        Assert.True(Data(response).GetProperty("timedOut").GetBoolean());
    }

    [Fact]
    public async Task Kill_ClampsTimeoutBelowOneSecond()
    {
        Playing();
        CancellationToken observed = default;
        World.Set("Kill.Monster", (string name, CancellationToken? token) =>
        {
            observed = token ?? default;
            return Task.CompletedTask;
        });

        AgentApiResponse response = await Router().HandleAsync(Post("/kill", """{"monster":"Slime","timeoutMs":5}"""));

        Assert.Equal(200, response.Status);
        Assert.Equal(1000, Data(response).GetProperty("timeoutMs").GetInt32());
        Assert.True(observed.CanBeCanceled);
    }

    [Fact]
    public async Task CancelAttack_ClearsAutoAttackAndTarget()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/cancel-attack"));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Combat.CancelAutoAttack"));
        Assert.True(World.WasCalled("Combat.CancelTarget"));
    }

    [Fact]
    public async Task ExitCombat_CallsCombatExit()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/exit-combat"));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Combat.Exit"));
    }

    [Fact]
    public async Task Rest_PassesFullFlag()
    {
        Playing();
        bool full = false;
        World.Set("Player.Rest", (bool value) =>
        {
            full = value;
            return Task.CompletedTask;
        });

        AgentApiResponse response = await Router().HandleAsync(Post("/rest", """{"full":true}"""));

        Assert.Equal(200, response.Status);
        Assert.True(full);
    }

    [Fact]
    public async Task ReloadMap_CallsMapReload()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/reload-map"));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Map.Reload"));
    }

    // ------------------------------------------------------- session control

    [Fact]
    public async Task Login_RequiresUsernameAndPassword()
    {
        AgentApiResponse missingPassword = await Router().HandleAsync(Post("/login", """{"username":"jey"}"""));
        AgentApiResponse missingUsername = await Router().HandleAsync(Post("/login", """{"password":"hunter2"}"""));

        Assert.Equal(400, missingPassword.Status);
        Assert.Equal(400, missingUsername.Status);
    }

    [Fact]
    public async Task Login_RejectsControlCharactersInPassword()
    {
        AgentApiResponse response = await Router().HandleAsync(Post("/login", """{"username":"jey","password":"bad\npw"}"""));

        Assert.Equal(400, response.Status);
        Assert.Contains("control characters", response.Error);
    }

    [Fact]
    public async Task Login_WithServer_StoresCredentialsAndRelogins()
    {
        World.Set("Servers.Relogin", (string server) => true);

        AgentApiResponse response = await Router().HandleAsync(Post("/login", """{"username":"jey","password":"hunter2","server":"Yorumi"}"""));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Servers.SetLoginInfo"));
        Assert.True(World.WasCalled("Servers.Relogin"));
        Assert.Equal("Yorumi", Data(response).GetProperty("server").GetString());
    }

    [Fact]
    public async Task Login_WithoutServer_SendsLoginOnly()
    {
        AgentApiResponse response = await Router().HandleAsync(Post("/login", """{"username":"jey","password":"hunter2"}"""));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Servers.Login"));
        Assert.False(World.WasCalled("Servers.Relogin"));
    }

    [Fact]
    public async Task Logout_CallsServerLogout()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/logout"));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Servers.Logout"));
    }

    [Fact]
    public async Task Relogin_PrefersStoredCredentials()
    {
        AgentApiResponse response = await Router().HandleAsync(Post("/relogin", "{}"));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Servers.Relogin"));
    }

    [Fact]
    public async Task SelectServer_RequiresServerName()
    {
        AgentApiResponse response = await Router().HandleAsync(Post("/select-server", """{"server":""}"""));

        Assert.Equal(400, response.Status);
    }

    // ---------------------------------------------------------------- quests

    [Fact]
    public async Task LoadQuests_RejectsEmptyTooManyAndNonPositiveIds()
    {
        Playing();

        Assert.Equal(400, (await Router().HandleAsync(Post("/quests/load", """{"ids":[]}"""))).Status);
        Assert.Equal(400, (await Router().HandleAsync(Post("/quests/load", """{"ids":[0]}"""))).Status);
        string tooMany = "[" + string.Join(",", Enumerable.Range(1, 31)) + "]";
        Assert.Equal(400, (await Router().HandleAsync(Post("/quests/load", $$"""{"ids":{{tooMany}}}"""))).Status);
    }

    [Fact]
    public async Task LoadQuests_DeduplicatesRequestedIds()
    {
        Playing();
        int[]? loaded = null;
        World.Set("Quests.Load", (int[] ids) =>
        {
            loaded = ids;
            return Task.CompletedTask;
        });

        AgentApiResponse response = await Router().HandleAsync(Post("/quests/load", """{"ids":[7,7,8]}"""));

        Assert.Equal(200, response.Status);
        Assert.Equal(new[] { 7, 8 }, loaded);
    }

    [Fact]
    public async Task AcceptQuest_UsesEnsureByDefault()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/quests/accept", """{"id":9421}"""));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Quests.EnsureAccept"));
        Assert.False(World.WasCalled("Quests.Accept"));
    }

    [Fact]
    public async Task AcceptQuest_EnsureFalse_CallsSingleAttempt()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/quests/accept", """{"id":9421,"ensure":false}"""));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Quests.Accept"));
        Assert.False(World.WasCalled("Quests.EnsureAccept"));
    }

    [Fact]
    public async Task AbandonQuest_UsesTheQuestAbandonMember()
    {
        Playing();
        int abandonedId = 0;
        World.Set("Quests.Abandon", (int id) =>
        {
            abandonedId = id;
            return true;
        });

        AgentApiResponse response = await Router().HandleAsync(Post("/quests/abandon", """{"id":900}"""));

        Assert.Equal(200, response.Status);
        Assert.Equal(900, abandonedId);
        Assert.True(Data(response).GetProperty("abandoned").GetBoolean());
    }

    [Fact]
    public async Task CompleteQuest_PassesRewardItemAndSpecialFlag()
    {
        Playing();
        int itemIdSeen = int.MinValue;
        World.Set("Quests.EnsureComplete", (int id, int itemId, bool special) =>
        {
            itemIdSeen = itemId;
            return true;
        });

        AgentApiResponse response = await Router().HandleAsync(Post("/quests/complete", """{"id":9421,"itemId":421517,"special":true}"""));

        Assert.Equal(200, response.Status);
        Assert.Equal(421517, itemIdSeen);
    }

    [Fact]
    public async Task QuestRoutes_RejectNonPositiveIds()
    {
        Playing();

        foreach (string path in new[] { "/quests/accept", "/quests/complete", "/quests/abandon" })
        {
            AgentApiResponse response = await Router().HandleAsync(Post(path, """{"id":0}"""));
            Assert.Equal(400, response.Status);
        }
    }

    // ---------------------------------------------------------------- shops

    [Fact]
    public async Task LoadShop_RejectsNonPositiveId()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/shop/load", """{"id":0}"""));

        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task BuyItem_WhenShopNotLoaded_IsConflict()
    {
        Playing();
        World.Set("Shops.IsLoaded", false);

        AgentApiResponse response = await Router().HandleAsync(Post("/shop/buy", """{"name":"Bland Blade"}"""));

        Assert.Equal(409, response.Status);
        Assert.StartsWith("shop_not_loaded", response.Error);
    }

    [Fact]
    public async Task BuyItem_UnknownItem_IsNotFound()
    {
        Playing();
        World.Set("Shops.IsLoaded", true);
        World.Set("Shops.Items", new List<Core.Models.Shops.ShopItem>());

        AgentApiResponse response = await Router().HandleAsync(Post("/shop/buy", """{"name":"Nope"}"""));

        Assert.Equal(404, response.Status);
        Assert.Equal("shop_item_not_found", response.Error);
    }

    [Fact]
    public async Task BuyItem_WithUnmetRequirements_IsConflictAndDoesNotBuy()
    {
        Playing();
        Core.Models.Shops.ShopItem item = new() { ID = 1301, Name = "Bland Blade", ShopItemID = 3, Quantity = 1 };
        World.Set("Shops.IsLoaded", true);
        World.Set("Shops.Items", new List<Core.Models.Shops.ShopItem> { item });
        World.Set("Shops.GetUnmetPurchaseRequirements", new List<string> { "Need 1 Bland Metal" });

        AgentApiResponse response = await Router().HandleAsync(Post("/shop/buy", """{"id":1301}"""));

        Assert.Equal(409, response.Status);
        Assert.StartsWith("purchase_requirements_unmet", response.Error);
        Assert.False(World.WasCalled("Shops.BuyItem"));
    }

    [Fact]
    public async Task BuyItem_WithFullInventory_IsConflict()
    {
        Playing();
        Core.Models.Shops.ShopItem item = new() { ID = 1301, Name = "Bland Blade", ShopItemID = 3, Quantity = 1 };
        World.Set("Shops.IsLoaded", true);
        World.Set("Shops.Items", new List<Core.Models.Shops.ShopItem> { item });
        World.Set("Shops.GetUnmetPurchaseRequirements", new List<string>());
        World.Set("Inventory.HasSpaceFor", false);

        AgentApiResponse response = await Router().HandleAsync(Post("/shop/buy", """{"id":1301}"""));

        Assert.Equal(409, response.Status);
        Assert.Equal("inventory_full", response.Error);
    }

    [Fact]
    public async Task CloseShop_UsesTheShopCloseMember()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/shop/close"));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Shops.Close"));
        Assert.True(Data(response).GetProperty("closed").GetBoolean());
    }

    // -------------------------------------------------------- serialisation

    [Fact]
    public async Task MutatingActions_AreSerialised()
    {
        // Two agents must not move the character at the same instant.
        Playing();
        using SemaphoreSlim gate = new(0, 1);
        World.Set("Map.Join", (string map, string cell, string pad, bool ignoreCheck, bool autoCorrect) =>
        {
            gate.Wait(TimeSpan.FromSeconds(5));
            return Task.CompletedTask;
        });
        AgentApiRouter router = Router();

        Task<AgentApiResponse> first = router.HandleAsync(Post("/join", """{"map":"a"}"""));
        await Task.Delay(150);
        Task<AgentApiResponse> second = router.HandleAsync(Post("/jump", """{"cell":"b"}"""));

        gate.Release();
        AgentApiResponse a = await first;
        AgentApiResponse b = await second;

        Assert.Equal(200, a.Status);
        Assert.Equal(200, b.Status);
    }

    [Fact]
    public async Task SecondConcurrentAction_IsRejectedWhileFirstRuns()
    {
        Playing();
        using SemaphoreSlim release = new(0, 1);
        World.Set("Map.Join", (string map, string cell, string pad, bool ignoreCheck, bool autoCorrect) =>
        {
            release.Wait(TimeSpan.FromSeconds(5));
            return Task.CompletedTask;
        });
        AgentApiRouter router = Router();

        Task<AgentApiResponse> first = router.HandleAsync(Post("/join", """{"map":"a"}"""));
        await Task.Delay(200);
        AgentApiResponse second = await router.HandleAsync(Post("/jump", """{"cell":"b"}"""));
        release.Release();
        await first;

        Assert.Equal(409, second.Status);
        Assert.Equal("another_action_in_progress", second.Error);
    }

    private new AgentApiOptions Options(string token = "") => base.Options(token);
}
