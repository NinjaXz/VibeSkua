namespace Skua.Core.Services.AgentApi;

/// <summary>
/// The bank: a second bag that the game fills only on request.
///
/// Two things make it different from the inventory. The item list is empty until
/// the client asks for it, so an agent must never read an empty list as an empty
/// bank. And a move needs the item to exist where it says it is, so the route
/// checks that before it touches the game and can name the real reason.
/// </summary>
public sealed partial class AgentApiRouter
{
    private object BuildBank()
    {
        RequireGameSession();
        RequirePlaying();

        List<InventoryRow> rows = ReadBankRows();

        // The game hands over the item list without ever setting its own loaded
        // flag, so the list is the proof: an answer that trusted the flag would
        // call a full bank unknown forever.
        bool loaded = Safe(() => _bot.Bank.Loaded, false) || rows.Count > 0;
        return new
        {
            loaded,
            usedSlots = Safe(() => _bot.Bank.UsedSlots, 0),
            slots = Safe(() => _bot.Bank.Slots, 0),
            items = rows,
            note = loaded ? string.Empty : "the list is only real when loaded is true: call POST /bank/open, then read again",
        };
    }

    private List<InventoryRow> ReadBankRows() =>
        Safe(() => _bot.Bank.Items.Where(i => i is not null)
            .Select(i => new InventoryRow(i.ID, i.Name, i.Quantity, i.MaxStack, i.Equipped))
            .ToList(), new List<InventoryRow>());

    private async Task<object?> OpenBankAsync(CancellationToken ct) =>
        await RunActionAsync(() =>
        {
            RequireGameSession();
            RequirePlaying();

            _bot.Bank.Open();

            // The load is not waited on: the game answers it whenever the bank
            // screen is ready, and a request that blocks on it times out.
            _bot.Bank.Load(false);
            return BuildBank();
        }, ct).ConfigureAwait(false);

    private async Task<object?> MoveBankItemAsync(AgentApiRequest request, CancellationToken ct, bool toBank) =>
        await RunActionAsync(() =>
        {
            RequireGameSession();
            RequirePlaying();

            BankMoveRequest body = AgentApiJson.Read<BankMoveRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
            string wanted = NameOrId(body);

            // A deposit only needs the bag. A withdrawal has to know what the bank
            // holds, so only that direction waits for the list.
            if (!toBank && !BankListIsKnown())
                throw new AgentApiException(409, "bank_not_loaded: call POST /bank/open, then read GET /bank");

            if (toBank)
            {
                bool inBag = Safe(() => body.Id is int id ? _bot.Inventory.TryGetItem(id, out _) : _bot.Inventory.TryGetItem(body.Name!, out _), false);
                if (!inBag)
                    throw new AgentApiException(404, $"item_not_in_inventory: {wanted}");

                bool stored = Safe(() => body.Id is int id ? _bot.Inventory.ToBank(id) : _bot.Inventory.ToBank(body.Name!), false);
                if (!stored)
                    throw new AgentApiException(409, $"deposit_failed: the game refused to store {wanted}");
            }
            else
            {
                bool inBank = Safe(() => body.Id is int id ? _bot.Bank.TryGetItem(id, out _) : _bot.Bank.TryGetItem(body.Name!, out _), false);
                if (!inBank)
                    throw new AgentApiException(404, $"item_not_in_bank: {wanted}");

                bool taken = Safe(() => body.Id is int id ? _bot.Bank.ToInventory(id) : _bot.Bank.ToInventory(body.Name!), false);
                if (!taken)
                    throw new AgentApiException(409, $"withdraw_failed: the game refused to hand back {wanted}");
            }

            return new { moved = true, name = wanted, toBank, bank = BuildBank() };
        }, ct).ConfigureAwait(false);

    /// <summary>True when the bank has told this client what it holds.</summary>
    private bool BankListIsKnown() => Safe(() => _bot.Bank.Loaded, false) || ReadBankRows().Count > 0;

    /// <summary>The one thing a move names, checked for shape before anything moves.</summary>
    private static string NameOrId(BankMoveRequest body)
    {
        if (body.Id is int id && id > 0)
            return id.ToString();
        return RequireText(body.Name, "name", 64);
    }

    private sealed record BankMoveRequest
    {
        public string? Name { get; init; }
        public int? Id { get; init; }
    }
}
