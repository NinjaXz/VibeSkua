using System.Globalization;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using Skua.Core.Models.Monsters;
using Skua.Core.Models.Players;
using Skua.Core.Models.Quests;

namespace Skua.Core.Services.AgentApi;

/// <summary>
/// Read side of the agent API: the snapshots an agent plans from.
///
/// Every reader degrades per field instead of failing the whole request. The
/// generated game bindings throw while a map, the avatar, or a shop is still
/// loading, and a half-filled snapshot tells the agent more than an error does.
/// </summary>
public sealed partial class AgentApiRouter
{
    private object Health() => new
    {
        name = "vibeskoa-agent",
        // Which process answered matters: a tab host holds no character, so its
        // state is empty by design and the client process must be asked instead.
        role = _client?.Role ?? "standalone",
        pid = Environment.ProcessId,
        version = Safe(() => _bot.Version.ToString(), string.Empty),
        loggedIn = Safe(() => _bot.Player.LoggedIn, false),
        member = Safe(() => _bot.Player.IsMember, false),
        playing = Safe(() => _bot.Player.Playing, false),
        scriptRunning = Safe(() => _scriptManager.ScriptRunning, false),
    };

    private object BuildState() => new
    {
        name = Safe(() => _bot.Player.Username, string.Empty),
        level = Safe(() => _bot.Player.Level, 0),
        health = Safe(() => _bot.Player.Health, 0),
        maxHealth = Safe(() => _bot.Player.MaxHealth, 0),
        mana = Safe(() => _bot.Player.Mana, 0),
        maxMana = Safe(() => _bot.Player.MaxMana, 0),
        gold = Safe(() => _bot.Player.Gold, 0),
        coins = Safe(() => _bot.Player.Coins, 0),
        cell = Safe(() => _bot.Player.Cell, string.Empty),
        pad = Safe(() => _bot.Player.Pad, string.Empty),
        x = Safe(() => _bot.Player.X, 0),
        y = Safe(() => _bot.Player.Y, 0),
        hpPercent = Percent(Safe(() => _bot.Player.Health, 0), Safe(() => _bot.Player.MaxHealth, 0)),
        mpPercent = Percent(Safe(() => _bot.Player.Mana, 0), Safe(() => _bot.Player.MaxMana, 0)),
        alive = Safe(() => _bot.Player.Alive, false),
        inCombat = Safe(() => _bot.Player.InCombat, false),
        playing = Safe(() => _bot.Player.Playing, false),
        loggedIn = Safe(() => _bot.Player.LoggedIn, false),
        member = Safe(() => _bot.Player.IsMember, false),
        map = BuildMapSnapshot(),
        target = ToMonsterDto(Safe(() => _bot.Player.Target, (Monster?)null)),
        script = new
        {
            running = Safe(() => _scriptManager.ScriptRunning, false),
            paused = Safe(() => _scriptManager.ScriptPaused, false),
            loaded = Safe(() => Path.GetFileName(_scriptManager.LoadedScript), string.Empty),
        },
    };

    /// <summary>Health or mana as a percentage of the maximum, 0 when unknown.</summary>
    private static float Percent(int value, int max) => max <= 0 ? 0f : (float)Math.Round(value * 100.0 / max, 1);

    private object BuildMapSnapshot() => new
    {
        name = Safe(() => _bot.Map.Name, string.Empty),
        roomId = Safe(() => _bot.Map.RoomID, 0),
        playerCount = Safe(() => _bot.Map.PlayerCount, 0),
    };

    private object BuildMap() => new
    {
        name = Safe(() => _bot.Map.Name, string.Empty),
        roomId = Safe(() => _bot.Map.RoomID, 0),
        playerCount = Safe(() => _bot.Map.PlayerCount, 0),
        cells = Safe<List<string>?>(() => _bot.Map.Cells, null) ?? new List<string>(),
        // The uoTree binding can be empty while the raw player list is already
        // populated; the names still tell the agent who is in the room.
        players = ReadPlayerRows(),
    };

    private async Task<object> BuildMapNpcsAsync(AgentApiRequest request, CancellationToken ct)
    {
        if (_mapInfo is null)
            throw new AgentApiException(501, "map_info_unavailable");

        try
        {
            MapInfo info = await _mapInfo.GetCurrentMapAsync(QueryInt(request, "refresh") == 1, ct).ConfigureAwait(false);
            return new
            {
                map = info.Map,
                roomId = info.RoomId,
                file = info.FilePath,
                fromCache = info.FromCache,
                cells = info.Cells,
                npcs = info.Npcs.Select(npc => new
                {
                    name = npc.Name,
                    cells = npc.Cells,
                    quests = npc.Quests.Select(q => new { id = q.Id, turnIn = q.TurnIn }).ToList(),
                    shops = npc.Shops.Select(s => s.Id).ToList(),
                    joins = npc.Joins.Select(j => new { map = j.Map, cell = j.Cell }).ToList(),
                    hasDialogue = npc.HasDialogue,
                }).ToList(),
            };
        }
        catch (MapInfoException ex)
        {
            throw new AgentApiException(StatusFor(ex.Code), ex.Code);
        }
    }

    private object BuildPlayers() => new { players = ReadPlayerRows() };

    private List<object> ReadPlayerRows()
    {
        List<PlayerInfo>? bound = Safe<List<PlayerInfo>?>(() => _bot.Map.Players, null);
        List<PlayerInfo> players = bound ?? new List<PlayerInfo>();
        if (players.Count > 0)
            return players.Select(ToPlayerDto).ToList();

        // Fallback: the client exposes player names even when the detail rows
        // are not populated yet.
        List<string>? names = Safe<List<string>?>(() => _bot.Map.PlayerNames, null);
        return (names ?? new List<string>())
            .Select(name =>
            {
                PlayerInfo? info = Safe(() => _bot.Map.GetPlayer(name), (PlayerInfo?)null);
                return info is null
                    ? (object)new { name, entId = 0, level = 0, hp = 0, maxHp = 0, cell = string.Empty, pad = string.Empty, x = 0, y = 0, state = 0, afk = false }
                    : ToPlayerDto(info);
            })
            .ToList();
    }

    private static object ToPlayerDto(PlayerInfo player) => new
    {
        name = player.Name,
        entId = player.EntID,
        level = player.Level,
        hp = player.HP,
        maxHp = player.MaxHP,
        cell = player.Cell,
        pad = player.Pad,
        x = player.X,
        y = player.Y,
        state = player.State,
        afk = player.AFK,
    };

    private object BuildMonsters()
    {
        List<Monster> current = Safe<List<Monster>>(() => _bot.Monsters.CurrentAvailableMonsters, new List<Monster>());
        List<Monster> all = Safe<List<Monster>>(() => _bot.Monsters.MapMonsters, new List<Monster>());
        return new
        {
            cell = Safe(() => _bot.Player.Cell, string.Empty),
            // The client's "available" list is what can be attacked from where
            // the character stands; "all" is every monster instance on the map.
            currentCell = current.Select(ToMonsterDto).ToList(),
            all = all.Select(ToMonsterDto).ToList(),
        };
    }

    private static object? ToMonsterDto(Monster? monster) => monster is null
        ? null
        : new
        {
            id = monster.ID,
            name = monster.Name,
            hp = monster.HP,
            maxHp = monster.MaxHP,
            cell = monster.Cell,
            mapId = monster.MapID,
            race = monster.Race,
            fileName = monster.FileName,
            state = monster.State,
            alive = monster.Alive,
        };

    private object BuildServers() => new
    {
        connected = Safe(() => _bot.Servers.IsConnected, false),
        current = Safe(() => _bot.Servers.LastName, string.Empty),
        servers = Safe(() => _bot.Servers.ServerList.Select(s => (object)new
        {
            name = s.Name,
            ip = s.IP,
            port = s.Port,
            online = s.Online,
            playerCount = s.PlayerCount,
            maxPlayers = s.MaxPlayers,
            lang = s.Lang,
            upgrade = s.Upgrade,
        }).ToList(), new List<object>()),
    };

    private object BuildInventory()
    {
        RequirePlaying();

        List<InventoryRow> rows = ReadInventoryRows();
        return new
        {
            gold = Safe(() => _bot.Player.Gold, 0),
            coins = Safe(() => _bot.Player.Coins, 0),
            usedSlots = Safe(() => _bot.Inventory.UsedSlots, 0),
            slots = Safe(() => _bot.Inventory.Slots, 0),
            items = rows,
        };
    }

    /// <summary>
    /// Inventory rows from the game binding. The remote-control service reads
    /// the same binding, so both surfaces agree on what the character carries.
    /// </summary>
    private List<InventoryRow> ReadInventoryRows() =>
        Safe(() => _bot.Inventory.Items.Where(i => i is not null)
            .Select(i => new InventoryRow(i.ID, i.Name, i.Quantity, i.MaxStack, i.Equipped))
            .ToList(), new List<InventoryRow>());

    private sealed record InventoryRow(int Id, string Name, int Quantity, int MaxStack, bool Equipped);

    private object BuildShop()
    {
        bool loaded = Safe(() => _bot.Shops.IsLoaded, false);
        return new
        {
            loaded,
            id = Safe(() => _bot.Shops.ID, 0),
            name = Safe(() => _bot.Shops.Name, string.Empty),
            items = loaded
                ? Safe(() => _bot.Shops.Items.Select(item => (object)new
                {
                    id = item.ID,
                    shopItemId = item.ShopItemID,
                    name = item.Name,
                    description = item.Description,
                    category = item.CategoryString,
                    quantity = item.Quantity,
                    maxStack = item.MaxStack,
                    cost = item.Cost,
                    level = item.Level,
                    faction = item.Faction,
                    coins = item.Coins,
                    upgrade = item.Upgrade,
                    requiredReputation = item.RequiredReputation,
                    tokenId = item.TokenID,
                    tokenName = item.TokenName,
                    tokenQuantity = item.TokenQuantity,
                    requirements = item.Requirements.Select(r => new { id = r.ID, name = r.Name, quantity = r.Quantity }).ToList(),
                }).ToList(), new List<object>())
                : new List<object>(),
        };
    }

    private object BuildQuests()
    {
        List<Quest> tree = Safe(() => _bot.Quests.Tree, new List<Quest>());
        return new
        {
            active = tree.Where(q => q.Active).Select(ToQuestDto).ToList(),
            completed = tree.Where(q => string.Equals(q.Status, "c", StringComparison.OrdinalIgnoreCase)).Select(ToQuestDto).ToList(),
            loaded = tree.Select(ToQuestDto).ToList(),
            registered = Safe(() => _bot.Quests.Registered.ToList(), new List<int>()),
            cachedCount = Safe(() => _bot.Quests.CachedDictionary.Count, 0),
        };
    }

    private object BuildQuest(int id)
    {
        if (id <= 0)
            throw new AgentApiException(400, "id must be a positive quest id");
        RequirePlaying();

        Quest? quest = TryGetQuest(id) ?? throw new AgentApiException(404, "quest_not_found");
        return new
        {
            quest = ToQuestDto(quest),
            inProgress = Safe(() => _bot.Quests.IsInProgress(id), false),
            canComplete = Safe(() => _bot.Quests.CanComplete(id), false),
            canCompleteFullCheck = Safe(() => _bot.Quests.CanCompleteFullCheck(id), false),
            isUnlocked = Safe(() => _bot.Quests.IsUnlocked(id), false),
            isDailyComplete = Safe(() => _bot.Quests.IsDailyComplete(id), false),
        };
    }

    private object BuildCachedQuests(AgentApiRequest request)
    {
        int start = QueryInt(request, "start", 0);
        int count = QueryInt(request, "count", 200);
        if (start < 0)
            throw new AgentApiException(400, "start must be zero or greater");
        if (count < 1)
            throw new AgentApiException(400, "count must be positive");
        if (count > 200)
            count = 200;

        List<QuestData> cached = Safe(() => _bot.Quests.GetCachedQuests(start, count), new List<QuestData>());
        return new
        {
            start,
            count,
            returned = cached.Count,
            quests = cached.Select(ToQuestDataDto).ToList(),
        };
    }

    private Quest? TryGetQuest(int id)
    {
        try
        {
            if (_bot.Quests.TryGetQuest(id, out Quest? quest))
                return quest;
        }
        catch
        {
            // Fall through to the tree scan: TryGetQuest only covers loaded data.
        }

        return Safe(() => _bot.Quests.Tree.FirstOrDefault(q => q.ID == id), (Quest?)null);
    }

    private object? TryGetQuestDto(int id)
    {
        Quest? quest = TryGetQuest(id);
        return quest is null ? null : ToQuestDto(quest);
    }

    private static object ToQuestDto(Quest quest) => new
    {
        id = quest.ID,
        name = quest.Name,
        status = quest.Status,
        level = quest.Level,
        gold = quest.Gold,
        xp = quest.XP,
        once = quest.Once,
        upgrade = quest.Upgrade,
        cell = quest.Field,
        active = quest.Active,
        rewards = (quest.SimpleRewards ?? new List<SimpleReward>()).Select(ToRewardDto).ToList(),
        rewardItems = (quest.Rewards ?? new List<ItemBase>()).Select(ToItemDto).ToList(),
        requirements = (quest.Requirements ?? new List<ItemBase>()).Select(ToItemDto).ToList(),
    };

    private static object ToRewardDto(SimpleReward reward) => new
    {
        itemId = reward.ID,
        name = reward.Name,
        quantity = reward.Quantity,
        rate = reward.Rate,
        type = reward.Type,
    };

    private static object ToItemDto(ItemBase item) => new
    {
        id = item.ID,
        name = item.Name,
        quantity = item.Quantity,
    };

    private static object ToQuestDataDto(QuestData quest) => new
    {
        id = quest.ID,
        name = quest.Name,
        level = quest.Level,
        gold = quest.Gold,
        xp = quest.XP,
        once = quest.Once,
        upgrade = quest.Upgrade,
    };

    private object BuildLogs(AgentApiRequest request)
    {
        string type = (request.Query.TryGetValue("type", out string? raw) ? raw : "script")?.Trim().ToLowerInvariant() ?? "script";
        LogType logType = type switch
        {
            "debug" => LogType.Debug,
            "script" => LogType.Script,
            "flash" => LogType.Flash,
            _ => throw new AgentApiException(400, "type must be debug, script, or flash"),
        };

        int limit = QueryInt(request, "limit", 50);
        if (limit < 1)
            throw new AgentApiException(400, "limit must be positive");
        if (limit > 2000)
            limit = 2000;

        List<string> entries = Safe(() => _logService.GetLogs(logType), new List<string>());
        List<string> tail = entries.TakeLast(limit).ToList();
        return new
        {
            type,
            limit,
            total = entries.Count,
            returned = tail.Count,
            entries = tail,
        };
    }
}
