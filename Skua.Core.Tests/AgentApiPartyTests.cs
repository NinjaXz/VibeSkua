using System.Text.Json;
using Skua.Core.Services.AgentApi;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// The party channel: the only thing that puts two accounts in the same room.
/// A map name is not a room, so a carry that only joins a map name carries nobody.
/// </summary>
public class AgentApiPartyTests : AgentApiContractTests
{
    private void Playing()
    {
        World.Set("Player.Playing", true);
        World.Set("Player.LoggedIn", true);
    }

    private void SentPackets(List<string> into)
    {
        World.Set("Send.Packet", new Action<string, string>((packet, _) => into.Add(packet)));
    }

    private JsonElement Data(AgentApiResponse response) => Json(response.Data!);

    [Fact]
    public async Task PartyInvite_SendsTheGamePartyPacket()
    {
        Playing();
        List<string> sent = new();
        SentPackets(sent);

        AgentApiResponse response = await Router().HandleAsync(
            Post("/party/invite", """{"name":"Not_a_grill"}"""));

        Assert.Equal(200, response.Status);
        Assert.Contains("%xt%zm%gp%1%pi%Not_a_grill%", sent);
        Assert.Equal("Not_a_grill", Data(response).GetProperty("name").GetString());
    }

    [Fact]
    public async Task PartySummon_PullsTheMemberIntoThisRoom()
    {
        Playing();
        List<string> sent = new();
        SentPackets(sent);

        AgentApiResponse response = await Router().HandleAsync(
            Post("/party/summon", """{"name":"Jmd123"}"""));

        Assert.Equal(200, response.Status);
        Assert.Contains("%xt%zm%gp%1%ps%Jmd123%", sent);
    }

    [Fact]
    public async Task PartyAccept_UsesTheIdFromTheGameWhenTheBodyHasNone()
    {
        Playing();
        List<string> sent = new();
        SentPackets(sent);
        World.Set("Flash.GetGameObject", new Func<string, object?>(path => path == "world.partyID" ? 4242 : null));

        AgentApiResponse response = await Router().HandleAsync(Post("/party/accept", "{}"));

        Assert.Equal(200, response.Status);
        Assert.Contains("%xt%zm%gp%1%pa%4242%", sent);
    }

    [Fact]
    public async Task PartyName_KeepsPacketCharactersOutOfThePacket()
    {
        Playing();
        // Each value stays valid JSON on purpose: a broken body proves nothing about the name check.
        foreach (string bad in new[] { "Bo%b", "Bob\\n", "a b", "xxxxxxxxxxxxxxxxxxxxx", "Bo|b", "Bo;b" })
        {
            AgentApiResponse response = await Router().HandleAsync(
                Post("/party/invite", $$"""{"name":"{{bad}}"}"""));
            Assert.True(response.Status == 400, bad + " -> " + response.Status + " " + (response.Error ?? ""));
            Assert.Contains("name", response.Error ?? "");
        }
    }

    [Fact]
    public async Task PartyState_ReadsLeaderAndMembersFromTheGame()
    {
        Playing();
        World.Set("Flash.GetGameObject", new Func<string, object?>(path => path switch
        {
            "world.partyOwner" => "Not_a_dumpling",
            "world.partyMembers" => new[] { "Not_a_grill", "Jmd123" },
            _ => null,
        }));
        World.Set("Player.Name", "Not_a_dumpling");

        AgentApiResponse response = await Router().HandleAsync(Get("/party"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.Equal("Not_a_dumpling", data.GetProperty("leader").GetString());
        Assert.Equal(2, data.GetProperty("members").GetArrayLength());
        Assert.True(data.GetProperty("inParty").GetBoolean());
        Assert.Equal(3, data.GetProperty("size").GetInt32());
    }

    [Fact]
    public async Task PartyWithoutACharacter_IsAConflictLikeEveryOtherSessionRoute()
    {
        AgentApiResponse response = await Router().HandleAsync(Post("/party/on", "{}"));

        Assert.Equal(409, response.Status);
        Assert.Contains("player_not_playing", response.Error ?? "");
    }

    [Fact]
    public async Task PartyOn_TurnsThePartyChannelOnBeforeAnInvite()
    {
        Playing();
        List<string> sent = new();
        SentPackets(sent);

        AgentApiResponse response = await Router().HandleAsync(Post("/party/on", "{}"));

        Assert.Equal(200, response.Status);
        Assert.Contains("%xt%zm%cmd%1%partyon%", sent);
    }
}
