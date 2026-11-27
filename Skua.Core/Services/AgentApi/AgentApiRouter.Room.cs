namespace Skua.Core.Services.AgentApi;

/// <summary>
/// The room pin: what makes two accounts stand together.
///
/// A map name does not identify a room. In a public room the game picks the room from
/// each account's own quest state, so the account that came to help can land in the
/// next room over and help nobody. With the private-room option on, the client joins
/// "map-number" instead, so every account pinned to one number is in the same room no
/// matter what its quests say. The library story and class scripts turn the option off,
/// which is why an agent needs to re-arm it from outside the script.
/// </summary>
public sealed partial class AgentApiRouter
{
    private object BuildRoom()
    {
        RequireGameSession();
        RequirePlaying();

        bool pinned = Safe(() => _bot.Options.PrivateRooms, false);
        int number = Safe(() => _bot.Options.PrivateNumber, 0);
        string map = Safe(() => _bot.Map.Name, string.Empty);

        return new
        {
            privateRooms = pinned,
            number,
            map,
            roomId = Safe(() => _bot.Map.RoomID, 0),
            roomTag = pinned && number > 0 ? $"{map}-{number}" : string.Empty,
            note = "roomTag is what a join asks for when privateRooms is true: pin the same number on every account of one party and the two roomId values match",
        };
    }

    private async Task<object?> PostRoomAsync(AgentApiRequest request, CancellationToken ct)
    {
        RoomRequest body = AgentApiJson.Read<RoomRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        bool toPublic = body.Public;
        int number = body.Number;

        // A number below the library's own floor, or long enough to overflow the room
        // field, makes the game answer with a room nobody can find.
        if (!toPublic && (number < 1000 || number > 999999))
        {
            throw new AgentApiException(400, "number must be between 1000 and 999999, or send {\"public\": true} for public rooms");
        }

        return await RunActionAsync(
            () =>
            {
                RequireGameSession();
                RequirePlaying();

                _bot.Options.PrivateRooms = !toPublic;
                if (!toPublic)
                {
                    _bot.Options.PrivateNumber = number;
                }

                int kept = Safe(() => _bot.Options.PrivateNumber, 0);
                string map = Safe(() => _bot.Map.Name, string.Empty);

                // The number alone moves nobody: the client would only take a private room
                // of its own. This is the packet the library's army scripts rely on, and it
                // is the only thing measured so far that puts several accounts in one room -
                // one account makes the room, the rest teleport into it by number.
                string teleported = string.Empty;
                if (!toPublic)
                {
                    string who = Safe(() => _bot.Player.Username, string.Empty);
                    teleported = $"%xt%zm%cmd%1%tfer%{who}%whitemap-{kept}%";
                    _bot.Send.Packet(teleported);
                }

                return (object)new
                {
                    privateRooms = !toPublic,
                    number = kept,
                    map,
                    teleported,
                    roomTag = toPublic ? string.Empty : $"{map}-{kept}",
                    warning = toPublic
                        ? "public rooms are chosen from each account's own quest state: two accounts on one map name can still stand in different rooms"
                        : string.Empty,
                };
            },
            ct,
            timeoutSeconds: 60).ConfigureAwait(false);
    }

    private sealed record RoomRequest
    {
        public int Number { get; init; }

        public bool Public { get; init; }
    }
}
