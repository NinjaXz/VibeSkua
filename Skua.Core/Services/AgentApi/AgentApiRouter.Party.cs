using System.Text.RegularExpressions;

namespace Skua.Core.Services.AgentApi;

/// <summary>
/// The party channel: the only way two accounts stand in the same room.
///
/// A map name is not a room. Two accounts that join the same map by name land in
/// different rooms, so a strong account cannot help a weak one on that map name
/// alone. The game fixes it with a party: one account leads, the others accept,
/// and a summon pulls a member into the leader's room. These routes send those
/// packets; deciding when to send them is the agent's job, not the client's.
/// </summary>
public sealed partial class AgentApiRouter
{
    /// <summary>
    /// A player name goes straight into a game packet, where % and the line breaks
    /// are structure. Anything else in a name would let a caller write packets.
    /// </summary>
    private static readonly Regex PartyNamePattern =
        // \z, not $: $ also matches before a trailing newline, and a newline inside a
        // game packet ends a line, so it would let a caller write extra packets.
        new(@"^[A-Za-z0-9][A-Za-z0-9_\-]{0,19}\z", RegexOptions.Compiled);

    private object BuildParty()
    {
        RequireGameSession();
        RequirePlaying();

        string leader = Safe(() => _bot.Flash.GetGameObject<string>("world.partyOwner"), string.Empty) ?? string.Empty;
        string[] members = Safe(
            () => _bot.Flash.GetGameObject<string[]>("world.partyMembers") ?? Array.Empty<string>(),
            Array.Empty<string>());

        return new
        {
            leader,
            me = Safe(() => _bot.Player.Username, string.Empty),
            members,
            inParty = leader.Length > 0,
            size = members.Length + (leader.Length > 0 ? 1 : 0),
            note = "GET /state on two accounts answers the same map name and a different map.roomId until a summon lands them together",
        };
    }

    private async Task<object?> PostPartyAsync(string path, AgentApiRequest request, CancellationToken ct)
    {
        PartyRequest body = ReadPartyRequest(request);

        string? name = path switch
        {
            "/party/invite" or "/party/summon" or "/party/kick" => RequirePartyName(body.Name),
            _ => null,
        };

        // The accept id is a game number, so it is read where the game keeps it when
        // the caller did not bring one. Reading it outside the gate costs nothing.
        string partyId = path == "/party/accept" && body.PartyId <= 0
            ? ReadPartyId().ToString()
            : body.PartyId.ToString();

        return await RunActionAsync(
            () =>
            {
                RequireGameSession();
                RequirePlaying();

                string packet = path switch
                {
                    "/party/on" => "%xt%zm%cmd%1%partyon%",
                    "/party/leave" => "%xt%zm%gp%1%pl%",
                    "/party/invite" => $"%xt%zm%gp%1%pi%{name}%",
                    "/party/summon" => $"%xt%zm%gp%1%ps%{name}%",
                    "/party/kick" => $"%xt%zm%gp%1%pk%{name}%",
                    "/party/accept" => $"%xt%zm%gp%1%pa%{partyId}%",
                    _ => throw new AgentApiException(404, "not_found"),
                };

                _bot.Send.Packet(packet);

                return (object)new
                {
                    sent = packet,
                    name,
                    acceptedPartyId = path == "/party/accept" ? partyId : string.Empty,
                };
            },
            ct,
            timeoutSeconds: 60).ConfigureAwait(false);
    }

    private PartyRequest ReadPartyRequest(AgentApiRequest request)
        => AgentApiJson.Read<PartyRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);

    private static string RequirePartyName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !PartyNamePattern.IsMatch(name))
        {
            throw new AgentApiException(400, "name must be a player name: letters, digits, _ or -, up to 20, and no packet characters");
        }

        return name;
    }

    private int ReadPartyId()
        => Safe(() => _bot.Flash.GetGameObject<int>("world.partyID"), 0);

    private sealed record PartyRequest(string? Name = null, int PartyId = 0);
}
