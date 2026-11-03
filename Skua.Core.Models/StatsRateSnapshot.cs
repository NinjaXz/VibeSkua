using System;
using System.Globalization;

namespace Skua.Core.Models;

/// <summary>
/// A manually captured snapshot of the live gameplay rates shown in Advanced Stats.
/// </summary>
public sealed record StatsRateSnapshot(
    DateTimeOffset SavedAt,
    TimeSpan SessionDuration,
    string RateUnit,
    double ExperienceRate,
    double GoldRate,
    double KillsRate,
    double DeathsRate,
    double ItemsRate,
    double ClassPointsRate,
    int ClassRank)
{
    public string SavedAtDisplay => SavedAt.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string SessionDisplay => SessionDuration.ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture);
    public string ExperienceDisplay => $"{ExperienceRate:N0} {RateUnit}";
    public string GoldDisplay => $"{GoldRate:N0} {RateUnit}";
    public string KillsDisplay => $"{KillsRate:N2} {RateUnit}";
    public string DeathsDisplay => $"{DeathsRate:N2} {RateUnit}";
    public string ItemsDisplay => $"{ItemsRate:N1} {RateUnit}";
    public string ClassPointsDisplay => $"{ClassPointsRate:N0} {RateUnit}";
}
