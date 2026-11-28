using System.Text.Json;
using Skua.Core.Models.Factions;
using Skua.Core.Models.Items;
using Skua.Core.Services.AgentApi;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// Class rank and reputation visibility. An agent that cannot see class rank cannot plan a
/// class grind: owning a class is not the same as ranking it, and a shop gate is a faction rank.
/// The game stores a class item's class points in the item quantity, and rank 10 needs 302,500 CP.
/// </summary>
public class AgentApiRankTests : AgentApiContractTests
{
    private JsonElement Data(AgentApiResponse response) => Json(response.Data!);

    private void Playing()
    {
        World.Set("Player.Playing", true);
        World.Set("Player.LoggedIn", true);
    }

    private static InventoryItem ClassItem(string name, int classPoints, bool equipped = false) =>
        new()
        {
            ID = name.GetHashCode() & 0xFFFF,
            CharItemID = 1,
            Name = name,
            Quantity = classPoints,
            CategoryString = "Class",
            Equipped = equipped,
        };

    [Theory]
    [InlineData(0, 1)]
    [InlineData(899, 1)]
    [InlineData(900, 2)]
    [InlineData(3600, 3)]
    [InlineData(10000, 4)]
    [InlineData(22500, 5)]
    [InlineData(44100, 6)]
    [InlineData(78400, 7)]
    [InlineData(129600, 8)]
    [InlineData(202500, 9)]
    [InlineData(302500, 10)]
    [InlineData(1_000_000, 10)]
    public void ClassRank_MatchesTheGameThresholds(int classPoints, int expectedRank)
    {
        Assert.Equal(expectedRank, ClassRank.RankOf(classPoints));
    }

    [Fact]
    public async Task Classes_ListsOwnedClassesWithClassPointsAndRank()
    {
        Playing();
        World.Set("Inventory.Items", new List<InventoryItem>
        {
            ClassItem("Imperial Chunin", 12000),
            ClassItem("Healer", 500, equipped: true),
            new() { ID = 7, Name = "Health Potion", Quantity = 3, CategoryString = "Potion" },
        });
        World.Set("Bank.Items", new List<InventoryItem> { ClassItem("Chaos Slayer Mystic", 900) });

        AgentApiResponse response = await Router().HandleAsync(Get("/classes"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        JsonElement classes = data.GetProperty("classes");
        Assert.Equal(3, classes.GetArrayLength());

        JsonElement chunin = classes.EnumerateArray().First(e => e.GetProperty("name").GetString() == "Imperial Chunin");
        Assert.Equal(12000, chunin.GetProperty("classPoints").GetInt32());
        Assert.Equal(4, chunin.GetProperty("rank").GetInt32());
        Assert.Equal(22500, chunin.GetProperty("nextRankClassPoints").GetInt32());
        Assert.False(chunin.GetProperty("equipped").GetBoolean());
        Assert.False(chunin.GetProperty("inBank").GetBoolean());

        JsonElement healer = classes.EnumerateArray().First(e => e.GetProperty("name").GetString() == "Healer");
        Assert.Equal(1, healer.GetProperty("rank").GetInt32());
        Assert.True(healer.GetProperty("equipped").GetBoolean());

        JsonElement slayer = classes.EnumerateArray().First(e => e.GetProperty("name").GetString() == "Chaos Slayer Mystic");
        Assert.Equal(2, slayer.GetProperty("rank").GetInt32());
        Assert.True(slayer.GetProperty("inBank").GetBoolean());

        Assert.DoesNotContain(classes.EnumerateArray(), e => e.GetProperty("name").GetString() == "Health Potion");
    }

    [Fact]
    public async Task Classes_MarksTheEquippedClassAndItsRank()
    {
        Playing();
        World.Set("Inventory.Items", new List<InventoryItem> { ClassItem("Imperial Chunin", 302500, equipped: true) });
        World.Set("Stats.CurrentClassRank", 10);
        World.Set("Stats.IsMaxClassRank", true);

        AgentApiResponse response = await Router().HandleAsync(Get("/classes"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.Equal(10, data.GetProperty("currentClassRank").GetInt32());
        Assert.True(data.GetProperty("maxedClassCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Reps_ListsFactionRanksAndPointsAndFiltersByName()
    {
        Playing();
        World.Set("Reputation.FactionList", new List<Faction>
        {
            new() { ID = 34, Name = "Yokai", Rank = 6, Rep = 1200, RequiredRep = 5000, TotalRep = 41200 },
            new() { ID = 12, Name = "Swordhaven", Rank = 10, Rep = 0, RequiredRep = 0, TotalRep = 500000 },
        });

        AgentApiResponse response = await Router().HandleAsync(Get("/reps"));

        Assert.Equal(200, response.Status);
        JsonElement factions = Data(response).GetProperty("factions");
        Assert.Equal(2, factions.GetArrayLength());

        JsonElement yokai = factions.EnumerateArray().First(f => f.GetProperty("name").GetString() == "Yokai");
        Assert.Equal(6, yokai.GetProperty("rank").GetInt32());
        Assert.Equal(1200, yokai.GetProperty("rep").GetInt32());
        Assert.Equal(5000, yokai.GetProperty("requiredRep").GetInt32());
        Assert.Equal(41200, yokai.GetProperty("totalRep").GetInt32());

        AgentApiResponse filtered = await Router().HandleAsync(Get("/reps?name=yok"));
        Assert.Equal(200, filtered.Status);
        Assert.Equal(1, Data(filtered).GetProperty("factions").GetArrayLength());
    }

    [Fact]
    public async Task Stats_ExposesClassRankProgressPointsAndRates()
    {
        Playing();
        World.Set("Stats.CurrentClassRank", 6);
        World.Set("Stats.CurrentClassPoints", 50000);
        World.Set("Stats.ClassPointsPerHour", 12000);
        World.Set("Stats.IsMaxClassRank", false);
        World.Set("Stats.ClassRank10Progress", 0.16);
        World.Set("Stats.ClassRank10ProgressText", "50,000 / 302,500 CP");
        World.Set("Stats.TimeToClassRank10", TimeSpan.FromMinutes(211));
        World.Set("Stats.ExperiencePerHour", 184000);
        World.Set("Stats.GoldPerHour", 900000L);
        World.Set("Stats.KillsPerMinute", 12.5);
        World.Set("Stats.DeathsPerMinute", 0.1);
        World.Set("Stats.Kills", 4200);
        World.Set("Stats.Deaths", 12);
        World.Set("Stats.QuestsCompleted", 33);
        World.Set("Stats.Relogins", 2);
        World.Set("Stats.SessionTime", TimeSpan.FromHours(3));

        AgentApiResponse response = await Router().HandleAsync(Get("/stats"));

        Assert.Equal(200, response.Status);
        JsonElement data = Data(response);
        Assert.Equal(6, data.GetProperty("classRank").GetInt32());
        Assert.Equal(50000, data.GetProperty("classPoints").GetInt32());
        Assert.Equal(12000, data.GetProperty("classPointsPerHour").GetInt32());
        Assert.False(data.GetProperty("isMaxClassRank").GetBoolean());
        Assert.Equal(0.16, data.GetProperty("classRank10Progress").GetDouble(), 3);
        Assert.Equal("50,000 / 302,500 CP", data.GetProperty("classRank10ProgressText").GetString());
        Assert.Equal(12660, data.GetProperty("secondsToClassRank10").GetInt64());
        Assert.Equal(184000, data.GetProperty("experiencePerHour").GetInt32());
        Assert.Equal(900000L, data.GetProperty("goldPerHour").GetInt64());
        Assert.Equal(12.5, data.GetProperty("killsPerMinute").GetDouble(), 3);
        Assert.Equal(4200, data.GetProperty("kills").GetInt32());
        Assert.Equal(33, data.GetProperty("questsCompleted").GetInt32());
        Assert.Equal(2, data.GetProperty("relogins").GetInt32());
        Assert.Equal(10800, data.GetProperty("sessionSeconds").GetInt64());
    }

    [Fact]
    public async Task Stats_AnsweredUnknown_WhenTheBindingThrows()
    {
        Playing();
        World.ThrowOn("Stats.CurrentClassRank");

        AgentApiResponse response = await Router().HandleAsync(Get("/stats"));

        Assert.Equal(200, response.Status);
        Assert.Equal(0, Data(response).GetProperty("classRank").GetInt32());
    }
}
