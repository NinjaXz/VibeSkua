using System.Text.Json;
using Skua.Core.Models.Items;
using Skua.Core.Services.AgentApi;
using Skua.Core.Tests.Fakes;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// The bank is a second bag, and the client only fills it on request. These tests
/// pin what an agent can learn from it, and what a move has to prove before the
/// route calls it done.
/// </summary>
public sealed class AgentApiBankTests : AgentApiContractTests
{
    private AgentApiRouter Router(IClientController? controller = null) =>
        new(new AgentApiOptions { ScriptsRoot = ScriptsRoot }, World.Bot, World.Manager, World.Logs, null, controller);

    private static JsonElement Data(AgentApiResponse response) => Json(response.Data!);

    /// <summary>A bag or a bank: it holds items, loses them, and gains them.</summary>
    private sealed class Pile
    {
        private readonly List<InventoryItem> _items = new();

        public Pile With(int id, string name, int quantity = 1)
        {
            _items.Add(new InventoryItem { ID = id, Name = name, Quantity = quantity, MaxStack = quantity });
            return this;
        }

        public bool Has(string name) => _items.Any(i => i.Name == name);

        public bool Has(int id) => _items.Any(i => i.ID == id);

        public bool Take(string name)
        {
            InventoryItem? hit = _items.FirstOrDefault(i => i.Name == name);
            if (hit is null)
                return false;
            _items.Remove(hit);
            return true;
        }

        public bool Take(int id)
        {
            InventoryItem? hit = _items.FirstOrDefault(i => i.ID == id);
            if (hit is null)
                return false;
            _items.Remove(hit);
            return true;
        }

        public void Add(InventoryItem item) => _items.Add(item);

        public InventoryItem? Move(string name, Pile other)
        {
            InventoryItem? hit = _items.FirstOrDefault(i => i.Name == name);
            if (hit is null)
                return null;
            _items.Remove(hit);
            other.Add(hit);
            return hit;
        }

        public List<InventoryItem> Snapshot() => new(_items);
    }

    private void GivenAPlayingCharacter()
    {
        World.Set("Player.Playing", true);
    }

    /// <summary>Wires the fake bag and bank so a move really changes what each holds.</summary>
    private void GivenBags(Pile bag, Pile bank, bool bankListIsKnown)
    {
        World.Set("Bank.Loaded", bankListIsKnown);
        World.Set("Bank.Slots", 120);
        World.Set("Bank.UsedSlots", bank.Snapshot().Count);
        World.Set("Bank.Items", bank.Snapshot());
        World.Set("Inventory.TryGetItem", (string name) => bag.Has(name));
        World.Set("Bank.TryGetItem", (string name) => bank.Has(name));
        World.Set("Inventory.ToBank", (string name) => bag.Move(name, bank) is not null);
        World.Set("Bank.ToInventory", (string name) => bank.Move(name, bag) is not null);
    }

    [Fact]
    public async Task TheBankListsItsItemsAndSlots()
    {
        Pile bank = new Pile().With(18927, "Treasure Potion");
        GivenAPlayingCharacter();
        GivenBags(new Pile(), bank, bankListIsKnown: true);

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
        GivenAPlayingCharacter();
        GivenBags(new Pile(), new Pile(), bankListIsKnown: false);

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
        // Measured on a live client: the game hands over the item list and leaves
        // its own loaded flag false.
        GivenAPlayingCharacter();
        GivenBags(new Pile(), new Pile().With(598, "Bone Dust"), bankListIsKnown: false);

        AgentApiResponse response = await Router().HandleAsync(Get("/bank"));
        JsonElement data = Data(response);

        Assert.True(data.GetProperty("loaded").GetBoolean());
        Assert.Equal(string.Empty, data.GetProperty("note").GetString());
    }

    [Fact]
    public async Task OpeningTheBankOpensItAndAsksForTheList()
    {
        GivenAPlayingCharacter();
        GivenBags(new Pile(), new Pile().With(18927, "Treasure Potion"), bankListIsKnown: true);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/open", "{}"));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Bank.Open"));
        Assert.True(World.WasCalled("Bank.Load"));
        Assert.True(Data(response).GetProperty("loaded").GetBoolean());
    }

    [Fact]
    public async Task DepositMovesAnItemTheBagHolds()
    {
        Pile bag = new Pile().With(6697, "Undead Essence", 8);
        Pile bank = new Pile().With(13440, "Power Gem", 2);
        GivenAPlayingCharacter();
        GivenBags(bag, bank, bankListIsKnown: true);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/deposit", """{"name":"Undead Essence"}"""));

        Assert.Equal(200, response.Status);
        Assert.True(Data(response).GetProperty("moved").GetBoolean());
        Assert.True(World.WasCalled("Inventory.ToBank"));
        Assert.False(bag.Has("Undead Essence"));
        Assert.True(bank.Has("Undead Essence"));
    }

    [Fact]
    public async Task DepositByItemIdUsesTheIdBinding()
    {
        Pile bag = new Pile().With(6697, "Undead Essence", 8);
        Pile bank = new Pile();
        GivenAPlayingCharacter();
        GivenBags(bag, bank, bankListIsKnown: true);
        World.Set("Inventory.TryGetItem", (int id) => bag.Has(id));
        World.Set("Inventory.ToBank", (int id) => bag.Take(id));

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/deposit", """{"id":6697}"""));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Inventory.ToBank"));
        Assert.False(bag.Has(6697));
    }

    [Fact]
    public async Task ADepositDoesNotWaitForTheBankList()
    {
        Pile bag = new Pile().With(598, "Bone Dust");
        Pile bank = new Pile();
        GivenAPlayingCharacter();
        GivenBags(bag, bank, bankListIsKnown: false);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/deposit", """{"name":"Bone Dust"}"""));

        Assert.Equal(200, response.Status);
        Assert.True(World.WasCalled("Inventory.ToBank"));
    }

    [Fact]
    public async Task DepositOfAnItemTheBagDoesNotHoldIsANotFound()
    {
        Pile bank = new Pile().With(13440, "Power Gem", 2);
        GivenAPlayingCharacter();
        GivenBags(new Pile(), bank, bankListIsKnown: true);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/deposit", """{"name":"No Such Item"}"""));

        Assert.Equal(404, response.Status);
        Assert.StartsWith("item_not_in_inventory", response.Error);
        Assert.False(World.WasCalled("Inventory.ToBank"));
    }

    [Fact]
    public async Task ADepositThatChangedNothingSaysSoEvenWhenTheCallAgreed()
    {
        // Measured on a live client: a bank move can answer true and change
        // nothing, so the route has to look at the bag afterwards.
        Pile bag = new Pile().With(6697, "Undead Essence", 8);
        GivenAPlayingCharacter();
        GivenBags(bag, new Pile(), bankListIsKnown: true);
        World.Set("Inventory.ToBank", true);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/deposit", """{"name":"Undead Essence"}"""));

        Assert.Equal(409, response.Status);
        Assert.StartsWith("deposit_did_not_take_effect", response.Error);
    }

    [Fact]
    public async Task WithdrawMovesAnItemTheBankHolds()
    {
        Pile bag = new Pile();
        Pile bank = new Pile().With(6697, "Undead Essence", 8);
        GivenAPlayingCharacter();
        GivenBags(bag, bank, bankListIsKnown: true);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/withdraw", """{"name":"Undead Essence"}"""));

        Assert.Equal(200, response.Status);
        Assert.True(Data(response).GetProperty("moved").GetBoolean());
        Assert.True(World.WasCalled("Bank.ToInventory"));
        Assert.True(bag.Has("Undead Essence"));
    }

    [Fact]
    public async Task AWithdrawCountsAsDoneWhenTheGameMovedItButSaidNo()
    {
        // Measured on a live client: the hand-back really happened while
        // Bank.ToInventory answered false, because the game is still working.
        Pile bag = new Pile();
        Pile bank = new Pile().With(6697, "Undead Essence", 8);
        GivenAPlayingCharacter();
        GivenBags(bag, bank, bankListIsKnown: true);
        World.Set("Bank.ToInventory", (string name) =>
        {
            bank.Move(name, bag);
            return false;
        });

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/withdraw", """{"name":"Undead Essence"}"""));

        Assert.Equal(200, response.Status);
        Assert.True(bag.Has("Undead Essence"));
    }

    [Fact]
    public async Task AWithdrawThatChangedNothingSaysSo()
    {
        Pile bank = new Pile().With(6697, "Undead Essence", 8);
        GivenAPlayingCharacter();
        GivenBags(new Pile(), bank, bankListIsKnown: true);
        World.Set("Bank.ToInventory", true);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/withdraw", """{"name":"Undead Essence"}"""));

        Assert.Equal(409, response.Status);
        Assert.StartsWith("withdraw_did_not_take_effect", response.Error);
    }

    [Fact]
    public async Task WithdrawFromAnUnloadedBankSaysOpenItFirst()
    {
        GivenAPlayingCharacter();
        GivenBags(new Pile(), new Pile(), bankListIsKnown: false);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/withdraw", """{"name":"Treasure Potion"}"""));

        Assert.Equal(409, response.Status);
        Assert.StartsWith("bank_not_loaded", response.Error);
        Assert.False(World.WasCalled("Bank.ToInventory"));
    }

    [Fact]
    public async Task WithdrawOfAnItemTheBankDoesNotHoldIsANotFound()
    {
        Pile bank = new Pile().With(13440, "Power Gem", 2);
        GivenAPlayingCharacter();
        GivenBags(new Pile(), bank, bankListIsKnown: true);

        AgentApiResponse response = await Router().HandleAsync(Post("/bank/withdraw", """{"name":"No Such Item"}"""));

        Assert.Equal(404, response.Status);
        Assert.StartsWith("item_not_in_bank", response.Error);
        Assert.False(World.WasCalled("Bank.ToInventory"));
    }

    [Fact]
    public async Task AMoveNeedsANameOrAnId()
    {
        GivenAPlayingCharacter();
        GivenBags(new Pile(), new Pile().With(18927, "Treasure Potion"), bankListIsKnown: true);

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
