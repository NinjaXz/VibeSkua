using Skua.Core.Models.Items;

namespace Skua.Core.Services.AgentApi;

/// <summary>
/// Rank and rate read models: class rank per owned class, faction reputation ranks, and the
/// session rates the client already measures. An agent plans a class grind from these numbers;
/// without them it can only guess whether a gate is hours away or days away.
/// </summary>
public partial class AgentApiRouter
{
    private sealed record ClassRow(
        string Name,
        int Id,
        int ClassPoints,
        int Rank,
        int NextRankClassPoints,
        bool Equipped,
        bool InBank);

    /// <summary>
    /// Every owned class with its class points and rank, from the bag and the bank. A class item's
    /// quantity field carries its accumulated class points, so rank is arithmetic, not a guess.
    /// </summary>
    private object BuildClasses()
    {
        List<ClassRow> rows =
        [
            .. ReadClassRows(_bot.Inventory.Items, inBank: false),
            .. ReadClassRows(_bot.Bank.Items, inBank: true),
        ];

        return new
        {
            count = rows.Count,
            currentClass = Safe(() => _bot.Player.CurrentClass?.Name ?? string.Empty, string.Empty),
            currentClassRank = Safe(() => _bot.Stats.CurrentClassRank, 0),
            currentClassPoints = Safe(() => _bot.Stats.CurrentClassPoints, 0),
            maxedClassCount = rows.Count(r => r.Rank >= 10),
            classes = rows,
        };
    }

    private static List<ClassRow> ReadClassRows(IEnumerable<InventoryItem>? items, bool inBank)
    {
        return Safe(
            () => (items ?? Enumerable.Empty<InventoryItem>())
                .Where(i => i is not null && i.Category == ItemCategory.Class)
                .Select(i => new ClassRow(
                    i.Name,
                    i.ID,
                    i.Quantity,
                    ClassRank.RankOf(i.Quantity),
                    ClassRank.NextRankPoints(i.Quantity),
                    Safe(() => i.Equipped, false),
                    inBank))
                .OrderByDescending(r => r.ClassPoints)
                .ToList(),
            new List<ClassRow>());
    }

    /// <summary>
    /// Faction reputation ranks. Class shops gate on a rank, so this is the number that says how far
    /// a class gate is. Optional <c>name</c> query filters by a case insensitive substring.
    /// </summary>
    private object BuildReps(AgentApiRequest request)
    {
        string filter = request.Query.TryGetValue("name", out string? raw) ? raw.Trim().ToLowerInvariant() : string.Empty;
        List<FactionRow> all = ReadFactionRows();
        List<FactionRow> rows = string.IsNullOrEmpty(filter)
            ? all
            : all.Where(f => f.Name.ToLowerInvariant().Contains(filter, StringComparison.Ordinal)).ToList();

        return new
        {
            count = rows.Count,
            filter = filter,
            factions = rows,
        };
    }

    private sealed record FactionRow(string Name, int Id, int Rank, int Rep, int RequiredRep, int TotalRep);

    private List<FactionRow> ReadFactionRows() =>
        Safe(
            () => (_bot.Reputation.FactionList ?? Enumerable.Empty<Models.Factions.Faction>())
                .Select(f => new FactionRow(f.Name, f.ID, f.Rank, f.Rep, f.RequiredRep, f.TotalRep))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            new List<FactionRow>());

    /// <summary>
    /// Session rates the client measures on its own timer: class rank and class points, the rank 10
    /// estimate, and XP, gold, kill, drop and death rates. These are the numbers that turn a plan
    /// into hours instead of vibes.
    /// </summary>
    private object BuildStats()
    {
        TimeSpan? rankTen = Safe(() => _bot.Stats.TimeToClassRank10, (TimeSpan?)null);
        TimeSpan? nextLevel = Safe(() => _bot.Stats.TimeToLevel, (TimeSpan?)null);

        return new
        {
            classRank = Safe(() => _bot.Stats.CurrentClassRank, 0),
            classPoints = Safe(() => _bot.Stats.CurrentClassPoints, 0),
            classPointsPerHour = Safe(() => _bot.Stats.ClassPointsPerHour, 0),
            isMaxClassRank = Safe(() => _bot.Stats.IsMaxClassRank, false),
            classRankProgressVisible = Safe(() => _bot.Stats.IsClassRankProgressVisible, true),
            classRank10Progress = Safe(() => _bot.Stats.ClassRank10Progress, 0d),
            classRank10ProgressText = Safe(() => _bot.Stats.ClassRank10ProgressText, string.Empty),
            secondsToClassRank10 = rankTen.HasValue ? (long?)rankTen.Value.TotalSeconds : null,
            experienceProgress = Safe(() => _bot.Stats.ExperienceProgress, 0d),
            secondsToNextLevel = nextLevel.HasValue ? (long?)nextLevel.Value.TotalSeconds : null,
            isMaxLevel = Safe(() => _bot.Stats.IsMaxLevel, false),
            experiencePerHour = Safe(() => _bot.Stats.ExperiencePerHour, 0),
            goldPerHour = Safe(() => _bot.Stats.GoldPerHour, 0L),
            killsPerMinute = Safe(() => _bot.Stats.KillsPerMinute, 0d),
            deathsPerMinute = Safe(() => _bot.Stats.DeathsPerMinute, 0d),
            itemsPerHour = Safe(() => _bot.Stats.ItemsPerHour, 0),
            kills = Safe(() => _bot.Stats.Kills, 0),
            deaths = Safe(() => _bot.Stats.Deaths, 0),
            drops = Safe(() => _bot.Stats.Drops, 0),
            questsAccepted = Safe(() => _bot.Stats.QuestsAccepted, 0),
            questsCompleted = Safe(() => _bot.Stats.QuestsCompleted, 0),
            relogins = Safe(() => _bot.Stats.Relogins, 0),
            sessionSeconds = (long)Safe(() => _bot.Stats.SessionTime.TotalSeconds, 0d),
            totalSeconds = (long)Safe(() => _bot.Stats.Time.TotalSeconds, 0d),
            inventoryFreeSpace = Safe(() => _bot.Stats.InventoryFreeSpace, 0),
            bankFreeSpace = Safe(() => _bot.Stats.BankFreeSpace, 0),
        };
    }
}
