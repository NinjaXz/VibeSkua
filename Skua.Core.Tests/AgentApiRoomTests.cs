using System.Text.Json;
using Skua.Core.Services.AgentApi;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// Room pinning: the difference between "same map name" and "same room".
/// </summary>
public class AgentApiRoomTests : AgentApiContractTests
{
    private void Playing()
    {
        World.Set("Player.Playing", true);
        World.Set("Player.LoggedIn", true);
        World.Set("Map.Name", "battleon");
        World.Set("Map.RoomID", 250123);
    }

    private JsonElement Data(AgentApiResponse response) => Json(response.Data!);

    [Fact]
    public async Task Room_PinSetsTheClientPrivateRoomOptionAndNamesTheRoomToJoin()
    {
        Playing();
        World.Set("Player.Username", "not_a_dumpling");
        List<string> sent = new();
        World.Set("Send.Packet", new Action<string, string>((packet, _) => sent.Add(packet)));

        AgentApiResponse response = await Router().HandleAsync(Post("/room", """{"number":250123}"""));

        Assert.Equal(200, response.Status);
        Assert.Equal(true, World.Received("Options.PrivateRooms"));
        Assert.Equal(250123, World.Received("Options.PrivateNumber"));
        Assert.Contains("%xt%zm%cmd%1%tfer%not_a_dumpling%whitemap-250123%", sent);
        JsonElement data = Data(response);
        Assert.True(data.GetProperty("privateRooms").GetBoolean());
        Assert.Equal("battleon-250123", data.GetProperty("roomTag").GetString());
        Assert.Contains("whitemap-250123", data.GetProperty("teleported").GetString());
    }

    [Fact]
    public async Task Room_ANumberTheGameCannotUseIsRefusedBeforeItTouchesTheClient()
    {
        Playing();
        foreach (string body in new[] { """{"number":999}""", """{"number":1000000}""", "{}" })
        {
            AgentApiResponse response = await Router().HandleAsync(Post("/room", body));

            Assert.Equal(400, response.Status);
            Assert.Contains("number", response.Error ?? "");
        }

        Assert.Null(World.Received("Options.PrivateRooms"));
    }

    [Fact]
    public async Task Room_PublicRoomsTurnThePinOffAndSayWhatThatCosts()
    {
        Playing();

        AgentApiResponse response = await Router().HandleAsync(Post("/room", """{"public":true}"""));

        Assert.Equal(200, response.Status);
        Assert.Equal(false, World.Received("Options.PrivateRooms"));
        Assert.Contains("quest state", Data(response).GetProperty("warning").GetString());
    }

    [Fact]
    public async Task Room_ReadAnswersWhatTheClientIsPinnedTo()
    {
        Playing();
        World.Set("Options.PrivateRooms", true);
        World.Set("Options.PrivateNumber", 250123);

        AgentApiResponse response = await Router().HandleAsync(Get("/room"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.True(data.GetProperty("privateRooms").GetBoolean());
        Assert.Equal(250123, data.GetProperty("number").GetInt32());
        Assert.Equal(250123, data.GetProperty("roomId").GetInt32());
        Assert.Equal("battleon-250123", data.GetProperty("roomTag").GetString());
    }

    [Fact]
    public async Task Room_WithoutACharacterIsAConflictLikeEveryOtherSessionRoute()
    {
        AgentApiResponse response = await Router().HandleAsync(Post("/room", """{"number":250123}"""));

        Assert.Equal(409, response.Status);
        Assert.Contains("player_not_playing", response.Error ?? "");
    }
}
