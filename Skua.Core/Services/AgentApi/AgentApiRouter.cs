using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using Skua.Core.Models.Monsters;
using Skua.Core.Models.Players;
using Skua.Core.Models.Quests;
using Skua.Core.Models.Shops;

namespace Skua.Core.Services.AgentApi;

/// <summary>
/// Request/response control surface for an AI agent driving one character.
///
/// It answers two questions the existing remote-control service cannot: what is
/// around the character right now (monsters, players, quests, shops, the loaded
/// script and its log), and what happened after an action. Every mutating call
/// returns the new state, so an agent does not have to guess.
///
/// Requests arrive as <see cref="AgentApiRequest"/>; <see cref="AgentApiServer"/>
/// is the HTTP shell around this class. Keeping the socket out of the router is
/// what makes every handler testable.
/// </summary>
public sealed partial class AgentApiRouter
{
    private readonly AgentApiOptions _options;
    private readonly IScriptInterface _bot;
    private readonly IScriptManager _scriptManager;
    private readonly ILogService _logService;
    private readonly IMapInfoReader? _mapInfo;
    private readonly IClientController? _client;
    private readonly SemaphoreSlim _actionGate = new(1, 1);
    private readonly SemaphoreSlim _scriptGate = new(1, 1);
    private readonly string _scriptsRoot;
    private readonly string _optionsRoot;

    public AgentApiRouter(
        AgentApiOptions options,
        IScriptInterface bot,
        IScriptManager scriptManager,
        ILogService logService,
        IMapInfoReader? mapInfo = null,
        IClientController? client = null)
    {
        _options = options;
        _bot = bot;
        _scriptManager = scriptManager;
        _logService = logService;
        _mapInfo = mapInfo;
        _client = client;
        _scriptsRoot = Path.GetFullPath(options.ScriptsRoot);
        _optionsRoot = Path.GetFullPath(options.OptionsRoot);
    }

    /// <summary>Handles one request and returns its status plus payload or error.</summary>
    public async Task<AgentApiResponse> HandleAsync(AgentApiRequest request, CancellationToken ct = default)
    {
        if (!IsAuthorized(request))
            return AgentApiResponse.Fail(401, "unauthorized");

        string method = request.Method.Trim().ToUpperInvariant();
        string path = NormalizePath(request.Path);
        if (path.Contains('?'))
        {
            // The seam is forgiving: a caller may hand over "quest?id=1" as one
            // string. The HTTP server already separates the two.
            int split = path.IndexOf('?');
            (path, request) = WithQueryString(request, path[(split + 1)..], path[..split]);
        }

        try
        {
            object? data = method switch
            {
                "GET" => await GetAsync(path, request, ct).ConfigureAwait(false),
                "POST" => await PostAsync(path, request, ct).ConfigureAwait(false),
                _ => throw new AgentApiException(405, "method_not_allowed"),
            };
            return AgentApiResponse.Ok(data);
        }
        catch (AgentApiException ex)
        {
            return AgentApiResponse.Fail(ex.Status, ex.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return AgentApiResponse.Fail(499, "cancelled");
        }
        catch (Exception ex)
        {
            return AgentApiResponse.Fail(500, ex.Message);
        }
    }

    private static string NormalizePath(string path)
    {
        string trimmed = (path ?? string.Empty).Trim().ToLowerInvariant().TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    /// <summary>
    /// Merges a query string that arrived in the path into the query values.
    /// Values the caller set explicitly win, so a server that already parsed
    /// the query is never overwritten.
    /// </summary>
    private static (string Path, AgentApiRequest Request) WithQueryString(AgentApiRequest request, string query, string path)
    {
        string clean = NormalizePath(path);
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;
            values[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        foreach (KeyValuePair<string, string> entry in request.Query)
            values[entry.Key] = entry.Value;

        AgentApiRequest merged = new()
        {
            Method = request.Method,
            Path = clean,
            Query = values,
            Body = request.Body,
            Authorization = request.Authorization,
        };
        return (clean, merged);
    }

    private async Task<object?> GetAsync(string path, AgentApiRequest request, CancellationToken ct)
    {
        switch (path)
        {
            case "/":
            case "/health":
                return Health();
            case "/state":
                return BuildState();
            case "/map":
                return BuildMap();
            case "/map/npcs":
                return await BuildMapNpcsAsync(request, ct).ConfigureAwait(false);
            case "/players":
                return BuildPlayers();
            case "/inventory":
                return BuildInventory();
            case "/monsters":
                return BuildMonsters();
            case "/servers":
                return BuildServers();
            case "/shop":
                return BuildShop();
            case "/quests":
                return BuildQuests();
            case "/quest":
                return BuildQuest(QueryInt(request, "id"));
            case "/quests/cached":
                return BuildCachedQuests(request);
            case "/quests/completed":
                return BuildCompletedQuests();
            case "/scripts":
                return BuildScripts(request);
            case "/script/options":
                return ReadScriptOptions(request);
            case "/script":
                return BuildScriptStatus();
            case "/logs":
                return BuildLogs(request);
            case "/bank":
                return BuildBank();
            case "/client":
                return BuildClientStatus();
            default:
                // A route the other verb owns is a method error: the agent's
                // next step differs between "wrong verb" and "no such endpoint".
                throw Known(PostRoutes, path)
                    ? new AgentApiException(405, "method_not_allowed")
                    : new AgentApiException(404, "not_found");
        }
    }

    private static bool Known(IReadOnlySet<string> routes, string path) => routes.Contains(path);

    private async Task<object?> PostAsync(string path, AgentApiRequest request, CancellationToken ct)
    {
        switch (path)
        {
            case "/join":
                return await JoinAsync(request, ct).ConfigureAwait(false);
            case "/jump":
                return await JumpAsync(request, ct).ConfigureAwait(false);
            case "/walk":
                return await WalkAsync(request, ct).ConfigureAwait(false);
            case "/walk-speed":
                return await WalkSpeedAsync(request, ct).ConfigureAwait(false);
            case "/goto":
                return await GotoAsync(request, ct).ConfigureAwait(false);
            case "/attack":
                return await AttackAsync(request, ct).ConfigureAwait(false);
            case "/kill":
                return await KillAsync(request, ct).ConfigureAwait(false);
            case "/cancel-attack":
                return await RunActionAsync(() =>
                {
                    RequirePlaying();
                    _bot.Combat.CancelAutoAttack();
                    _bot.Combat.CancelTarget();
                    return BuildState();
                }, ct).ConfigureAwait(false);
            case "/exit-combat":
                return await RunActionAsync(() =>
                {
                    RequirePlaying();
                    _bot.Combat.Exit();
                    return BuildState();
                }, ct).ConfigureAwait(false);
            case "/rest":
                return await RestAsync(request, ct).ConfigureAwait(false);
            case "/reload-map":
                return await RunActionAsync(() =>
                {
                    RequirePlaying();
                    _bot.Map.Reload();
                    return BuildState();
                }, ct).ConfigureAwait(false);
            case "/relogin":
                return await ReloginAsync(request, ct).ConfigureAwait(false);
            case "/login":
                return await LoginAsync(request, ct).ConfigureAwait(false);
            case "/logout":
                return await LogoutAsync(ct).ConfigureAwait(false);
            case "/bank/open":
                return await OpenBankAsync(ct).ConfigureAwait(false);
            case "/bank/deposit":
                return await MoveBankItemAsync(request, ct, toBank: true).ConfigureAwait(false);
            case "/bank/withdraw":
                return await MoveBankItemAsync(request, ct, toBank: false).ConfigureAwait(false);
            case "/client/start":
                return StartClient(request);
            case "/select-server":
                return await SelectServerAsync(request, ct).ConfigureAwait(false);
            case "/quests/load":
                return await LoadQuestsAsync(request, ct).ConfigureAwait(false);
            case "/quests/check":
                return await CheckQuestsAsync(request, ct).ConfigureAwait(false);
            case "/quests/accept":
                return await AcceptQuestAsync(request, ct).ConfigureAwait(false);
            case "/quests/complete":
                return await CompleteQuestAsync(request, ct).ConfigureAwait(false);
            case "/quests/abandon":
                return await AbandonQuestAsync(request, ct).ConfigureAwait(false);
            case "/shop/load":
                return await LoadShopAsync(request, ct).ConfigureAwait(false);
            case "/shop/buy":
                return await BuyShopItemAsync(request, ct).ConfigureAwait(false);
            case "/shop/close":
                return await RunActionAsync(CloseShop, ct).ConfigureAwait(false);
            default:
                return await PostScriptsAsync(path, request, ct).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------- session

    private async Task<object?> ReloginAsync(AgentApiRequest request, CancellationToken ct)
    {
        RequireGameSession();
        ReloginRequest body = AgentApiJson.Read<ReloginRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        string? server = string.IsNullOrWhiteSpace(body.Server) ? null : RequireText(body.Server, "server", 64);

        return await RunActionAsync(() =>
        {
            bool reconnected = string.IsNullOrEmpty(server)
                ? _bot.Servers.Relogin()
                : _bot.Servers.Relogin(server);
            return new { reconnected, state = BuildState() };
        }, ct, timeoutSeconds: 300).ConfigureAwait(false);
    }

    private async Task<object?> LoginAsync(AgentApiRequest request, CancellationToken ct)
    {
        RequireGameSession();
        LoginRequest body = AgentApiJson.Read<LoginRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        string username = RequireText(body.Username, "username", 64);
        string password = RequireSecret(body.Password, "password", 256);
        string? server = string.IsNullOrWhiteSpace(body.Server) ? null : RequireText(body.Server, "server", 64);

        return await RunActionAsync(() =>
        {
            // With a server name, Relogin performs the whole
            // logout -> login -> connect sequence with the game's own waits.
            if (!string.IsNullOrEmpty(server))
            {
                _bot.Servers.SetLoginInfo(username, password);
                bool connected = _bot.Servers.Relogin(server);
                string actual = Safe(() => _bot.Servers.LastName, string.Empty);
                return new
                {
                    loginStarted = true,
                    connected,
                    server = string.IsNullOrWhiteSpace(actual) ? server : actual,
                    state = BuildState(),
                };
            }

            if (Safe(() => _bot.Player.Playing, false))
            {
                try
                {
                    _bot.Servers.Logout();
                }
                catch
                {
                    // Already disconnected; the login below is what matters.
                }
                Safe(() => _bot.Wait.ForTrue(() => !_bot.Player.Playing, 10), false);
            }

            _bot.Servers.SetLoginInfo(username, password);
            _bot.Servers.Login(username, password);
            return (object)new
            {
                loginStarted = true,
                connected = Safe(() => _bot.Player.Playing, false),
                state = BuildState(),
            };
        }, ct, timeoutSeconds: 300).ConfigureAwait(false);
    }

    private async Task<object?> LogoutAsync(CancellationToken ct)
    {
        RequireGameSession();
        return await RunActionAsync(() =>
        {
            _bot.Servers.Logout();
            Safe(() => _bot.Wait.ForTrue(() => !_bot.Player.Playing, 10), false);
            return (object)new
            {
                loggedOut = !Safe(() => _bot.Player.Playing, false),
                state = BuildState(),
            };
        }, ct, timeoutSeconds: 60).ConfigureAwait(false);
    }

    private async Task<object?> SelectServerAsync(AgentApiRequest request, CancellationToken ct)
    {
        RequireGameSession();
        SelectServerRequest body = AgentApiJson.Read<SelectServerRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        string server = RequireText(body.Server, "server", 64);

        return await RunActionAsync(() =>
        {
            // Relogin uses the credentials already stored in the client, so the
            // password never travels through this endpoint.
            bool connected = _bot.Servers.Relogin(server);
            string actual = Safe(() => _bot.Servers.LastName, string.Empty);
            return (object)new
            {
                connected,
                server = string.IsNullOrWhiteSpace(actual) ? server : actual,
                state = BuildState(),
            };
        }, ct, timeoutSeconds: 300).ConfigureAwait(false);
    }

    // ------------------------------------------------------------- movement

    private async Task<object?> JoinAsync(AgentApiRequest request, CancellationToken ct)
    {
        JoinRequest body = AgentApiJson.Read<JoinRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        string map = RequireText(body.Map, "map", 96);
        string cell = OptionalText(body.Cell, "Enter", 64);
        string pad = OptionalText(body.Pad, "Spawn", 64);

        return await RunActionAsync(() =>
        {
            RequirePlaying();
            _bot.Map.Join(map, cell, pad, body.IgnoreCheck);
            return BuildState();
        }, ct).ConfigureAwait(false);
    }

    private async Task<object?> JumpAsync(AgentApiRequest request, CancellationToken ct)
    {
        JumpRequest body = AgentApiJson.Read<JumpRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        string cell = RequireText(body.Cell, "cell", 64);
        string pad = OptionalText(body.Pad, "Spawn", 64);

        return await RunActionAsync(() =>
        {
            RequirePlaying();
            _bot.Map.Jump(cell, pad, body.AutoCorrect, body.ClientOnly);
            return BuildState();
        }, ct).ConfigureAwait(false);
    }

    private async Task<object?> WalkAsync(AgentApiRequest request, CancellationToken ct)
    {
        WalkRequest body = AgentApiJson.Read<WalkRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        if (body.X is < -100000 or > 100000 || body.Y is < -100000 or > 100000)
            throw new AgentApiException(400, "x and y must be between -100000 and 100000");
        int speed = body.Speed is >= 1 and <= 50 ? body.Speed : 8;

        return await RunActionAsync(() =>
        {
            RequirePlaying();
            _bot.Player.WalkTo(body.X, body.Y, speed);
            return BuildState();
        }, ct).ConfigureAwait(false);
    }

    private async Task<object?> WalkSpeedAsync(AgentApiRequest request, CancellationToken ct)
    {
        WalkSpeedRequest body = AgentApiJson.Read<WalkSpeedRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        if (body.Speed is < 1 or > 50)
            throw new AgentApiException(400, "speed must be between 1 and 50");

        return await RunActionAsync(() =>
        {
            RequirePlaying();
            _bot.Player.WalkSpeed = body.Speed;
            return BuildState();
        }, ct).ConfigureAwait(false);
    }

    private async Task<object?> GotoAsync(AgentApiRequest request, CancellationToken ct)
    {
        GotoRequest body = AgentApiJson.Read<GotoRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        string player = RequireText(body.Player, "player", 64);

        return await RunActionAsync(() =>
        {
            RequirePlaying();
            _bot.Player.Goto(player);
            return BuildState();
        }, ct).ConfigureAwait(false);
    }

    // --------------------------------------------------------------- combat

    private async Task<object?> AttackAsync(AgentApiRequest request, CancellationToken ct)
    {
        AttackRequest body = AgentApiJson.Read<AttackRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        string monster = RequireText(body.Monster, "monster", 96);

        return await RunActionAsync(() =>
        {
            RequirePlaying();
            bool attacked = int.TryParse(monster, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                ? _bot.Combat.Attack(id)
                : _bot.Combat.Attack(monster);
            return (object)new { attacked, state = BuildState() };
        }, ct).ConfigureAwait(false);
    }

    private async Task<object?> KillAsync(AgentApiRequest request, CancellationToken ct)
    {
        KillRequest body = AgentApiJson.Read<KillRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        string monster = RequireText(body.Monster, "monster", 96);
        // A window below one second or above ten minutes is a mistake, not a
        // request: clamp it to the nearest bound, and report what was used.
        int requested = body.TimeoutMs <= 0 ? 120000 : body.TimeoutMs;
        int timeoutMs = Math.Clamp(requested, 1000, 600000);

        return await RunActionAsync(() =>
        {
            RequirePlaying();
            bool timedOut = false;
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutMs);

            try
            {
                if (int.TryParse(monster, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                    _bot.Kill.Monster(id, timeout.Token);
                else
                    _bot.Kill.Monster(monster, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                // The monster survived the window; the caller decides whether to
                // retry, retreat, or raise the timeout.
                timedOut = true;
            }

            return (object)new { timedOut, timeoutMs, state = BuildState() };
        }, ct, timeoutSeconds: (timeoutMs / 1000) + 30).ConfigureAwait(false);
    }

    private async Task<object?> RestAsync(AgentApiRequest request, CancellationToken ct)
    {
        RestRequest body = AgentApiJson.Read<RestRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);

        return await RunActionAsync(() =>
        {
            RequirePlaying();
            _bot.Player.Rest(body.Full);
            return BuildState();
        }, ct).ConfigureAwait(false);
    }

    // ----------------------------------------------------------------- shop

    private async Task<object?> LoadShopAsync(AgentApiRequest request, CancellationToken ct)
    {
        ShopLoadRequest body = AgentApiJson.Read<ShopLoadRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        if (body.Id is < 1 or > 1_000_000)
            throw new AgentApiException(400, "id must be a positive shop id");

        return await RunActionAsync(() =>
        {
            RequirePlaying();
            _bot.Shops.Load(body.Id);
            return BuildShop();
        }, ct).ConfigureAwait(false);
    }

    private async Task<object?> BuyShopItemAsync(AgentApiRequest request, CancellationToken ct)
    {
        ShopBuyRequest body = AgentApiJson.Read<ShopBuyRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        string? name = string.IsNullOrWhiteSpace(body.Name) ? null : body.Name.Trim();
        if (name is null && body.Id <= 0)
            throw new AgentApiException(400, "name or id is required");
        if (name is { Length: > 100 })
            throw new AgentApiException(400, "name is too long");
        if (body.Quantity is 0 or < -1 or > 999)
            throw new AgentApiException(400, "quantity must be -1 (bundle) or between 1 and 999");

        return await RunActionAsync(() =>
        {
            RequirePlaying();
            if (!Safe(() => _bot.Shops.IsLoaded, false))
                throw new AgentApiException(409, "shop_not_loaded: load the shop first");

            ShopItem? item = Safe(() => _bot.Shops.Items.FirstOrDefault(i =>
                    (body.Id > 0 ? i.ID == body.Id : i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) &&
                    (body.ShopItemId <= 0 || i.ShopItemID == body.ShopItemId)),
                null);

            if (item is null)
                throw new AgentApiException(404, "shop_item_not_found");

            List<string> unmet = Safe(() => _bot.Shops.GetUnmetPurchaseRequirements(item, body.Quantity), new List<string>());
            if (unmet.Count > 0)
                throw new AgentApiException(409, "purchase_requirements_unmet: " + string.Join("; ", unmet));

            int bundle = body.Quantity == -1 ? Math.Max(1, item.Quantity) : body.Quantity;
            if (!Safe(() => _bot.Inventory.HasSpaceFor(item, bundle), true))
                throw new AgentApiException(409, "inventory_full");

            int before = ReadInventoryRows().FirstOrDefault(i => i.Id == item.ID)?.Quantity ?? 0;
            int shopItemId = body.ShopItemId > 0 ? body.ShopItemId : item.ShopItemID;
            if (body.Id > 0)
                _bot.Shops.BuyItem(body.Id, shopItemId, body.Quantity);
            else
                _bot.Shops.BuyItem(item.Name, body.Quantity);
            int after = ReadInventoryRows().FirstOrDefault(i => i.Id == item.ID)?.Quantity ?? 0;

            return (object)new
            {
                sent = true,
                bought = after > before,
                item = new
                {
                    id = item.ID,
                    shopItemId,
                    name = item.Name,
                    quantityBefore = before,
                    quantityAfter = after,
                },
                shop = BuildShop(),
            };
        }, ct).ConfigureAwait(false);
    }

    private object CloseShop()
    {
        RequirePlaying();
        _bot.Shops.Close();
        return new { closed = true };
    }

    // --------------------------------------------------------------- quests

    private async Task<object?> LoadQuestsAsync(AgentApiRequest request, CancellationToken ct)
    {
        LoadQuestsRequest body = AgentApiJson.Read<LoadQuestsRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        int[] ids = ValidateQuestIds(body.Ids);

        return await RunActionAsync(() =>
        {
            RequirePlaying();

            if (ids.Length == 1)
            {
                _bot.Quests.EnsureLoad(ids[0]);
            }
            else
            {
                _bot.Quests.Load(ids);
                _bot.Wait.ForTrue(() => ids.All(id => _bot.Quests.Tree.Any(q => q.ID == id)), 30);
            }

            List<Quest> found = _bot.Quests.Tree.Where(q => ids.Contains(q.ID)).ToList();
            return (object)new
            {
                requested = ids,
                loaded = found.Count,
                quests = found.Select(ToQuestDto).ToList(),
            };
        }, ct, timeoutSeconds: 120).ConfigureAwait(false);
    }

    /// <summary>
    /// Is this quest finished? Not by status: a quest finished long ago loads with
    /// status null. The game answers instead, the way the script library does it
    /// (CoreBots.cs:4890): finished when the quest has no slot, or when
    /// world.getQuestValue(slot) reached the quest's own value.
    /// </summary>
    private async Task<object?> CheckQuestsAsync(AgentApiRequest request, CancellationToken ct)
    {
        LoadQuestsRequest body = AgentApiJson.Read<LoadQuestsRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        int[] ids = ValidateQuestIds(body.Ids, 300);

        return await RunActionAsync(() =>
        {
            RequirePlaying();

            for (int start = 0; start < ids.Length; start += 30)
            {
                int[] chunk = ids.Skip(start).Take(30).ToArray();
                _bot.Quests.Load(chunk);
                _bot.Wait.ForTrue(() => chunk.All(id => _bot.Quests.Tree.Any(q => q.ID == id)), 30);
            }

            List<Quest> tree = Safe(() => _bot.Quests.Tree, new List<Quest>());
            List<object> rows = new();
            foreach (int id in ids)
            {
                Quest? quest = tree.FirstOrDefault(q => q.ID == id);
                if (quest is null)
                {
                    rows.Add(new { id, state = "not_loaded", slot = -1, needed = 0, questValue = 0, complete = false });
                    continue;
                }

                bool noSlot = quest.Slot < 0;
                int questValue = noSlot
                    ? 0
                    : Safe(() => _bot.Flash.CallGameFunction<int>("world.getQuestValue", quest.Slot), 0);
                rows.Add(new
                {
                    id,
                    name = quest.Name,
                    state = "loaded",
                    slot = quest.Slot,
                    needed = quest.Value,
                    questValue,
                    complete = noSlot || questValue >= quest.Value,
                });
            }

            return (object)new { count = rows.Count, quests = rows };
        }, ct, timeoutSeconds: 600).ConfigureAwait(false);
    }

    private async Task<object?> AcceptQuestAsync(AgentApiRequest request, CancellationToken ct)
    {
        QuestActionRequest body = AgentApiJson.Read<QuestActionRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        int id = RequireQuestId(body.Id);

        return await RunActionAsync(() =>
        {
            RequirePlaying();
            bool accepted = body.Ensure ? _bot.Quests.EnsureAccept(id) : _bot.Quests.Accept(id);
            return (object)new
            {
                id,
                accepted,
                inProgress = _bot.Quests.IsInProgress(id),
                quest = TryGetQuestDto(id),
            };
        }, ct, timeoutSeconds: 180).ConfigureAwait(false);
    }

    private async Task<object?> CompleteQuestAsync(AgentApiRequest request, CancellationToken ct)
    {
        QuestActionRequest body = AgentApiJson.Read<QuestActionRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        int id = RequireQuestId(body.Id);
        int itemId = body.ItemId < -1 ? -1 : body.ItemId;

        return await RunActionAsync(() =>
        {
            RequirePlaying();
            bool completed = body.Ensure
                ? _bot.Quests.EnsureComplete(id, itemId, body.Special)
                : _bot.Quests.Complete(id, itemId, body.Special);
            return (object)new
            {
                id,
                completed,
                inProgress = _bot.Quests.IsInProgress(id),
                quest = TryGetQuestDto(id),
            };
        }, ct, timeoutSeconds: 180).ConfigureAwait(false);
    }

    private async Task<object?> AbandonQuestAsync(AgentApiRequest request, CancellationToken ct)
    {
        QuestActionRequest body = AgentApiJson.Read<QuestActionRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        int id = RequireQuestId(body.Id);

        return await RunActionAsync(() =>
        {
            RequirePlaying();
            bool abandoned = _bot.Quests.Abandon(id);
            return (object)new
            {
                id,
                abandoned,
                inProgress = _bot.Quests.IsInProgress(id),
                quest = TryGetQuestDto(id),
            };
        }, ct, timeoutSeconds: 60).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- gates

    /// <summary>
    /// Runs one mutating action with the character to itself. Two agents that
    /// both send a move must not have their packets interleave.
    /// </summary>
    private async Task<object?> RunActionAsync(Func<object> action, CancellationToken ct, int timeoutSeconds = 0)
    {
        if (!await _actionGate.WaitAsync(TimeSpan.FromMilliseconds(_options.ActionGateWaitMs), ct).ConfigureAwait(false))
        {
            // The character is mid-action. Retrying is the agent's decision, so
            // this request reports the conflict instead of burning its timeout.
            throw new AgentApiException(409, "another_action_in_progress");
        }

        try
        {
            int seconds = timeoutSeconds > 0 ? timeoutSeconds : _options.ActionTimeoutSeconds;
            Task<object> work = Task.Run(action, ct);
            Task finished = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(seconds), ct)).ConfigureAwait(false);
            if (finished != work)
                throw new AgentApiException(504, "action_timed_out");
            return await work.ConfigureAwait(false);
        }
        finally
        {
            _actionGate.Release();
        }
    }

    private async Task<object?> RunScriptActionAsync(Func<Task<object>> action, CancellationToken ct, int timeoutSeconds = 120)
    {
        if (!await _scriptGate.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), ct).ConfigureAwait(false))
            throw new AgentApiException(409, "another_script_action_in_progress");

        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _scriptGate.Release();
        }
    }

    // -------------------------------------------------------------- security

    /// <summary>
    /// Constant-time token check over the Authorization header. A custom header
    /// value (no scheme) is accepted too, for clients that cannot set
    /// Authorization, and is compared the same way.
    /// </summary>
    public bool IsAuthorized(AgentApiRequest request)
    {
        if (_options.Token.Length == 0)
            return true;

        string provided = (request.Authorization ?? string.Empty).Trim();
        int space = provided.IndexOf(' ');
        if (space > 0)
            provided = provided[(space + 1)..].Trim();

        byte[] expected = Encoding.UTF8.GetBytes(_options.Token);
        byte[] actual = Encoding.UTF8.GetBytes(provided);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    // ------------------------------------------------------------- validation

    private void RequirePlaying()
    {
        if (!Safe(() => _bot.Player.Playing, false))
            throw new AgentApiException(409, "player_not_playing");
    }

    /// <summary>Endpoints answered by GET.</summary>
    internal static readonly IReadOnlySet<string> GetRoutes = new HashSet<string>
    {
        "/", "/health", "/state", "/map", "/map/npcs", "/players", "/inventory", "/monsters",
        "/servers", "/shop", "/quests", "/quest", "/quests/cached", "/quests/completed",
        "/scripts", "/script", "/script/options",
        "/logs", "/client", "/bank",
    };

    /// <summary>Endpoints answered by POST, including the short script aliases.</summary>
    internal static readonly IReadOnlySet<string> PostRoutes = new HashSet<string>
    {
        "/join", "/jump", "/walk", "/walk-speed", "/goto", "/attack", "/kill", "/cancel-attack",
        "/exit-combat", "/rest", "/reload-map", "/login", "/logout", "/relogin", "/select-server",
        "/quests/load", "/quests/check", "/quests/accept", "/quests/complete", "/quests/abandon",
        "/shop/load", "/shop/buy", "/shop/close",
        "/scripts/write", "/scripts/run", "/scripts/pause", "/scripts/resume", "/scripts/stop",
        "/script/options",
        "/bank/open", "/bank/deposit", "/bank/withdraw", "/client/start", "/pause", "/resume", "/stop",
    };

    /// <summary>
    /// Turns a map reader failure into a status the agent can act on.
    ///
    /// A character that is not standing in a map is a state, so it answers 409 like
    /// every other state. A machine with no decompiler is missing a tool, so it
    /// answers 503. Anything else means the map file could not be fetched or read.
    /// </summary>
    internal static int StatusFor(string code) => code switch
    {
        "map_not_loaded" => 409,
        "ffdec_missing" or "java_missing" or "decompiler_unavailable" => 503,
        _ => 502,
    };

    internal static int QueryInt(AgentApiRequest request, string key, int fallback = 0, bool required = false)
    {
        if (!request.Query.TryGetValue(key, out string? raw) || string.IsNullOrWhiteSpace(raw))
        {
            if (required)
                throw new AgentApiException(400, $"{key} must be an integer");
            return fallback;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new AgentApiException(400, $"{key} must be an integer");
        return value;
    }

    private static string RequireText(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new AgentApiException(400, $"{name} is required");
        return Normalize(value, name, maxLength);
    }

    /// <summary>
    /// A game object name: at least two characters, because a single letter is a
    /// truncated map or monster name rather than a real one.
    /// </summary>
    private static string RequireName(string? value, string name, int maxLength)
    {
        string text = RequireText(value, name, maxLength);
        if (text.Length < 2)
            throw new AgentApiException(400, $"{name} is too short");
        return text;
    }

    private static string OptionalText(string? value, string fallback, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? fallback : Normalize(value, "value", maxLength);

    private static string Normalize(string value, string name, int maxLength)
    {
        string text = value.Trim();
        if (text.Length == 0)
            throw new AgentApiException(400, $"{name} is required");
        if (text.Length > maxLength)
            throw new AgentApiException(400, $"{name} is too long (max {maxLength})");
        if (text.Any(char.IsControl))
            throw new AgentApiException(400, $"{name} contains control characters");
        return text;
    }

    private static string RequireSecret(string? value, string name, int maxLength)
    {
        // A password is never trimmed: leading and trailing spaces can be real.
        if (string.IsNullOrEmpty(value))
            throw new AgentApiException(400, $"{name} is required");
        if (value.Length > maxLength)
            throw new AgentApiException(400, $"{name} is too long (max {maxLength})");
        if (value.Any(char.IsControl))
            throw new AgentApiException(400, $"{name} contains control characters");
        return value;
    }

    private static int[] ValidateQuestIds(int[]? ids, int maxIds = 30)
    {
        if (ids is null || ids.Length == 0)
            throw new AgentApiException(400, "ids is required");
        if (ids.Length > maxIds)
            throw new AgentApiException(400, $"at most {maxIds} quest ids per request");
        if (ids.Any(id => id <= 0))
            throw new AgentApiException(400, "quest ids must be positive");
        return ids.Distinct().ToArray();
    }

    private static int RequireQuestId(int id)
    {
        if (id <= 0)
            throw new AgentApiException(400, "id must be a positive quest id");
        return id;
    }

    /// <summary>
    /// Reads one bot value without letting a half-loaded game fail the request.
    /// The generated bindings throw while a map or the avatar is still loading;
    /// an agent still needs the rest of the snapshot to decide what to do.
    /// </summary>
    private static T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    // ----------------------------------------------------------- request DTOs

    private sealed record JoinRequest
    {
        public string Map { get; init; } = string.Empty;
        public string? Cell { get; init; }
        public string? Pad { get; init; }
        public bool IgnoreCheck { get; init; }
    }

    private sealed record JumpRequest
    {
        public string Cell { get; init; } = string.Empty;
        public string? Pad { get; init; }
        public bool AutoCorrect { get; init; } = true;
        public bool ClientOnly { get; init; }
    }

    private sealed record WalkRequest
    {
        public int X { get; init; }
        public int Y { get; init; }
        public int Speed { get; init; } = 8;
    }

    private sealed record WalkSpeedRequest
    {
        public int Speed { get; init; } = 8;
    }

    private sealed record GotoRequest
    {
        public string Player { get; init; } = string.Empty;
    }

    private sealed record AttackRequest
    {
        public string Monster { get; init; } = string.Empty;
    }

    private sealed record KillRequest
    {
        public string Monster { get; init; } = string.Empty;
        public int TimeoutMs { get; init; } = 120000;
    }

    private sealed record RestRequest
    {
        public bool Full { get; init; }
    }

    private sealed record ReloginRequest
    {
        public string? Server { get; init; }
    }

    private sealed record LoginRequest
    {
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string? Server { get; init; }
    }

    private sealed record SelectServerRequest
    {
        public string Server { get; init; } = string.Empty;
    }

    private sealed record LoadQuestsRequest
    {
        public int[] Ids { get; init; } = Array.Empty<int>();
    }

    private sealed record QuestActionRequest
    {
        public int Id { get; init; }
        public int ItemId { get; init; } = -1;
        public bool Special { get; init; }
        public bool Ensure { get; init; } = true;
    }

    private sealed record ShopLoadRequest
    {
        public int Id { get; init; }
    }

    private sealed record ShopBuyRequest
    {
        public string Name { get; init; } = string.Empty;
        public int Id { get; init; }
        public int ShopItemId { get; init; } = -1;
        public int Quantity { get; init; } = -1;
    }

    private sealed record ScriptWriteRequest
    {
        public string Name { get; init; } = string.Empty;
        public string? Content { get; init; }
        public bool Overwrite { get; init; }
    }

    private sealed record ScriptRunRequest
    {
        public string Name { get; init; } = string.Empty;
        public bool Force { get; init; }

        /// <summary>Default true: start without opening the script's options window.</summary>
        public bool Silent { get; init; } = true;
    }
}
