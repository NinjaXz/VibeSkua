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
    long ClassPointsGained,
    int ClassId)
{
    public double ExperiencePerHour => Duration.TotalHours > 0 ? ExperienceGained / Duration.TotalHours : 0;
    public double GoldPerHour => Duration.TotalHours > 0 ? NetGoldChange / Duration.TotalHours : 0;
    public double KillsPerMinute => Duration.TotalMinutes > 0 ? Kills / Duration.TotalMinutes : 0;
}
