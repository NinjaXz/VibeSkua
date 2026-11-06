using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Skua.Core.Models;

/// <summary>
/// A manually captured snapshot of the live gameplay rates shown in Advanced Stats.
/// Rates are stored in <see cref="RateUnit"/>; <see cref="Character"/> is null for rows saved before it existed.
/// The *Display properties are for the history table only and are not saved.
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
    int ClassRank,
    string? Character = null)
{
    [JsonIgnore] public string SavedAtDisplay => SavedAt.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
    [JsonIgnore] public string CharacterDisplay => string.IsNullOrWhiteSpace(Character) ? "—" : Character;
    [JsonIgnore] public string SessionDisplay => string.Concat(
        ((long)SessionDuration.TotalHours).ToString(CultureInfo.InvariantCulture), ":",
        SessionDuration.Minutes.ToString("D2", CultureInfo.InvariantCulture), ":",
        SessionDuration.Seconds.ToString("D2", CultureInfo.InvariantCulture));
    [JsonIgnore] public string ExperienceDisplay => ExperienceRate.ToString("N0", CultureInfo.CurrentCulture);
    [JsonIgnore] public string GoldDisplay => GoldRate.ToString("N0", CultureInfo.CurrentCulture);
    [JsonIgnore] public string KillsDisplay => KillsRate.ToString("N2", CultureInfo.CurrentCulture);
    [JsonIgnore] public string DeathsDisplay => DeathsRate.ToString("N2", CultureInfo.CurrentCulture);
    [JsonIgnore] public string ItemsDisplay => ItemsRate.ToString("N1", CultureInfo.CurrentCulture);
    [JsonIgnore] public string ClassPointsDisplay => ClassPointsRate.ToString("N0", CultureInfo.CurrentCulture);
}
