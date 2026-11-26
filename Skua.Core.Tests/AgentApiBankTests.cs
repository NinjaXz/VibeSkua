using System.Text.Json;
using Skua.Core.Models.Items;
using Skua.Core.Services.AgentApi;
using Skua.Core.Tests.Fakes;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// The bank is a second bag, and the client only fills it on request. These tests
/// pin what an agent can learn from it, and what a move needs before it acts.
/// </summary>
public sealed class AgentApiBankTests : AgentApiContractTests
{
    private AgentApiRouter Router(IClientController? controller = null) =>
        new(new AgentApiOptions { ScriptsRoot = ScriptsRoot }, World.Bot, World.Manager, World.Logs, null, controller);

    private static JsonElement Data(AgentApiResponse response) => Json(response.Data!);

    private void GivenALoadedBank()
    {
        World.Set("Player.Playing", true);
        World.Set("Bank.Loaded", true);
        World.Set("Bank.UsedSlots", 1);
        World.Set("Bank.Slots", 120);
        World.Set("Bank.Items", new List<InventoryItem>
        {
            new() { ID = 18927, Name = "Treasure Potion", Quantity = 1, MaxStack = 1 },
        });
    }

    [Fact]
    public async Task TheBankListsItsItemsAndSlots()
    {
        GivenALoadedBank();

        AgentApiResponse response = await Router().HandleAsync(Get("/bank"));
        JsonElement data = Data(response);

        Assert.Equal(200, response.Status);
        Assert.True(data.GetProperty("loaded").GetBoolean());
        Assert.Equal(1, data.GetProperty("usedSlots").GetInt32());
        Assert.Equal(120, data.GetProperty("slots").GetInt32());
        Assert.Equal("Treasure Potion", data.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnUnloadedBankSaysTheListIsNotRealYet()
    {
        World.Set("Player.Playing", true);
        World.Set("Bank.Loaded", false);

        AgentApiResponse response = await Router().HandleAsync(Get("/bank"));
        JsonElement data = Data(response);

        Assert.Equal(200, response.Status);
        Assert.False(data.GetProperty("loaded").GetBoolean());
        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.Contains("open", data.GetProperty("note").GetString());
    }

    [Fact]
    public async Task TheBankCountsAsLoadedWhenItsItemsArrive()
    {
        World.Set("Player.Playing", true);
        World.Set("Bank.Loaded", false);
        World.Set("Bank.Items", new List<InventoryItem>
        {
            new() { ID = 598, Name = "Bone Dust", Quantity = 1, MaxStack = 1 },
        });

        AgentApiResponse response = await Router().HandleAsync(Get("/bank"));
        JsonElement data = Data(response);

        Assert.True(data.GetProperty("loaded").GetBoolean());
        Assert.Equal(string.Empty, data.GetProperty("note").GetString());
    }

    [Fact]
    public async Task ADepositDoesNotWaitForTheBankList()
    {
        World.Set("Player.Playing", true);
        World.Set("Bank.Loaded", false);
        World.Set("Inventory.TryGetItem.out", new InventoryItem { ID = 598, Name = "Bone Dust" });
        World.Set("Inventory.ToBank", true);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/deposit", """{"name":"Bone Dust"}"""));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Inventory.ToBank"));
    }

    [Fact]
    public async Task OpeningTheBankOpensItAndAsksForTheList()
    {
        GivenALoadedBank();

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/open", "{}"));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Bank.Open"));
        Assert.True(World.WasCalled("Bank.Load"));
        Assert.True(Data(response).GetProperty("loaded").GetBoolean());
    }

    [Fact]
    public async Task DepositMovesAnItemTheBagHolds()
    {
        GivenALoadedBank();
        World.Set("Inventory.TryGetItem.out", new InventoryItem { ID = 18927, Name = "Treasure Potion", Quantity = 1 });
        World.Set("Inventory.ToBank", true);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/deposit", """{"name":"Treasure Potion"}"""));

        Assert.Equal(200, response.Status);
        Assert.True(Data(response).GetProperty("moved").GetBoolean());
        Assert.True(World.WasCalled("Inventory.ToBank"));
    }

    [Fact]
    public async Task DepositByItemIdUsesTheIdBinding()
    {
        GivenALoadedBank();
        World.Set("Inventory.TryGetItem.out", new InventoryItem { ID = 18927, Name = "Treasure Potion" });
        World.Set("Inventory.ToBank", true);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/deposit", """{"id":18927}"""));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Inventory.TryGetItem"));
        Assert.True(World.WasCalled("Inventory.ToBank"));
    }

    [Fact]
    public async Task DepositOfAnItemTheBagDoesNotHoldIsANotFound()
    {
        GivenALoadedBank();
        World.Set("Inventory.TryGetItem", false);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/deposit", """{"name":"No Such Item"}"""));

        Assert.Equal(404, response.Status);
        Assert.StartsWith("item_not_in_inventory", response.Error);
        Assert.False(World.WasCalled("Inventory.ToBank"));
    }

    [Fact]
    public async Task AFailedDepositSaysTheGameRefusedIt()
    {
        GivenALoadedBank();
        World.Set("Inventory.TryGetItem.out", new InventoryItem { ID = 1, Name = "Treasure Potion" });
        World.Set("Inventory.ToBank", false);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/deposit", """{"name":"Treasure Potion"}"""));

        Assert.Equal(409, response.Status);
        Assert.StartsWith("deposit_failed", response.Error);
    }

    [Fact]
    public async Task WithdrawMovesAnItemTheBankHolds()
    {
        GivenALoadedBank();
        World.Set("Bank.TryGetItem.out", new InventoryItem { ID = 18927, Name = "Treasure Potion" });
        World.Set("Bank.ToInventory", true);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/withdraw", """{"name":"Treasure Potion"}"""));

        Assert.Equal(200, response.Status);
        Assert.True(Data(response).GetProperty("moved").GetBoolean());
        Assert.True(World.WasCalled("Bank.ToInventory"));
    }

    [Fact]
    public async Task WithdrawFromAnUnloadedBankSaysOpenItFirst()
    {
        World.Set("Player.Playing", true);
        World.Set("Bank.Loaded", false);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/withdraw", """{"name":"Treasure Potion"}"""));

        Assert.Equal(409, response.Status);
        Assert.StartsWith("bank_not_loaded", response.Error);
        Assert.False(World.WasCalled("Bank.ToInventory"));
    }

    [Fact]
    public async Task AMoveNeedsANameOrAnId()
    {
        GivenALoadedBank();

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/deposit", "{}"));

        Assert.Equal(400, response.Status);
        Assert.False(World.WasCalled("Inventory.ToBank"));
    }

    [Theory]
    [InlineData("GET", "/bank")]
    [InlineData("POST", "/bank/open")]
    [InlineData("POST", "/bank/deposit")]
    [InlineData("POST", "/bank/withdraw")]
    public async Task EveryBankRouteOnAHostSaysTheClientIsNotStarted(string method, string path)
    {
        IClientController host = new AgentApiClientTests.StubController { Role = "host", CanHostGame = false };
        AgentApiRequest request = method == "GET" ? Get(path) : Post(path, """{"name":"Treasure Potion"}""");

        AgentApiResponse response = await Router(host).HandleAsync(request);

        Assert.Equal(409, response.Status);
        Assert.StartsWith("client_not_started", response.Error);
    }
}
