namespace Skua.Core.Models;

/// <summary>
/// A completed connected gameplay session saved for historical statistics.
/// </summary>
public sealed record StatsSessionRecord(
    DateTimeOffset EndedAt,
    TimeSpan Duration,
    long ExperienceGained,
    long NetGoldChange,
    long Kills,
    long Deaths,
    long ItemsCollected,
    long ClassPointsGained);
