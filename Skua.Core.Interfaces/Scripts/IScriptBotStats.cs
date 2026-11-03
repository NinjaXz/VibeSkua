using System.ComponentModel;

namespace Skua.Core.Interfaces;

/// <summary>
/// Represents a set of statistics related to script bot activity and live gameplay activity.
/// </summary>
public interface IScriptBotStats : INotifyPropertyChanged
{
    /// <summary>The number of times the player has died.</summary>
    int Deaths { get; set; }

    /// <summary>The number of drops picked up.</summary>
    int Drops { get; set; }

    /// <summary>The number of monsters killed.</summary>
    int Kills { get; set; }

    /// <summary>The number of quests accepted (not unique).</summary>
    int QuestsAccepted { get; set; }

    /// <summary>The number of quests completed and turned in (not unique).</summary>
    int QuestsCompleted { get; set; }

    /// <summary>The number of times the player has been relogged in.</summary>
    int Relogins { get; set; }

    /// <summary>The max inventory space.</summary>
    int InventorySpace { get; }

    /// <summary>The filled inventory space.</summary>
    int InventoryFilledSpace { get; }

    /// <summary>The free inventory space.</summary>
    int InventoryFreeSpace { get; }

    /// <summary>The max bank space.</summary>
    int BankSpace { get; }

    /// <summary>The filled bank space.</summary>
    int BankFilledSpace { get; }

    /// <summary>The free bank space.</summary>
    int BankFreeSpace { get; }

    /// <summary>The elapsed time since script statistics were started or reset.</summary>
    TimeSpan Time { get; }

    /// <summary>Connected gameplay time accumulated during this application session.</summary>
    TimeSpan SessionTime { get; }

    /// <summary>Positive experience gained per hour during the current session.</summary>
    int ExperiencePerHour { get; }

    /// <summary>Positive gold balance increases per hour during the current session.</summary>
    int GoldPerHour { get; }

    /// <summary>Monster kills per minute during the current session.</summary>
    double KillsPerMinute { get; }

    /// <summary>Player deaths per minute during the current session.</summary>
    double DeathsPerMinute { get; }

    /// <summary>Collected item quantity per hour during the current session.</summary>
    int ItemsPerHour { get; }

    /// <summary>Estimated time remaining to the next level, when a valid rate is available.</summary>
    TimeSpan? TimeToLevel { get; }

    /// <summary>
    /// Updates gameplay metrics from the live player state. This is called by the client timer and does not depend on a running script.
    /// </summary>
    void UpdateGameplaySnapshot(int experience, int requiredExperience, int level, int gold, bool connected);

    /// <summary>Resets all values.</summary>
    void Reset();

    /// <summary>Gets inventory and bank space.</summary>
    void GetSpace();
}