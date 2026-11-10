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

    /// <summary>Current experience progress within the player's level, from 0 to 100.</summary>
    double ExperienceProgress { get; }

    /// <summary>Formatted current and required experience for the player's level.</summary>
    string ExperienceProgressText { get; }

    /// <summary>Whether the player has reached level 100.</summary>
    bool IsMaxLevel { get; }

    /// <summary>Whether level progress details should be displayed.</summary>
    bool IsLevelProgressVisible { get; }

    /// <summary>Progress toward the cumulative class rank 10 target, from 0 to 100.</summary>
    double ClassRank10Progress { get; }

    /// <summary>Formatted current class points and the cumulative rank 10 target.</summary>
    string ClassRank10ProgressText { get; }

    /// <summary>Positive experience gained per hour during the current session.</summary>
    int ExperiencePerHour { get; }

    /// <summary>Net gold balance change per hour during the current session, including spending.</summary>
    long GoldPerHour { get; }

    /// <summary>Monster kills per minute during the current session.</summary>
    double KillsPerMinute { get; }

    /// <summary>Player deaths per minute during the current session.</summary>
    double DeathsPerMinute { get; }

    /// <summary>Collected item quantity per hour during the current session.</summary>
    int ItemsPerHour { get; }

    /// <summary>Current equipped class point total.</summary>
    int CurrentClassPoints { get; }

    /// <summary>Current equipped class rank.</summary>
    int CurrentClassRank { get; }

    /// <summary>Whether the equipped class has reached rank 10.</summary>
    bool IsMaxClassRank { get; }

    /// <summary>Whether class rank progress details should be displayed.</summary>
    bool IsClassRankProgressVisible { get; }

    /// <summary>Class points gained per hour for the currently equipped class.</summary>
    int ClassPointsPerHour { get; }

    /// <summary>Estimated time remaining to the next level, when a valid rate is available.</summary>
    TimeSpan? TimeToLevel { get; }

    /// <summary>Estimated time remaining to class rank 10, using the 302,500 cumulative point target.</summary>
    TimeSpan? TimeToClassRank10 { get; }

    /// <summary>Records a successful item pickup from the game event stream.</summary>
    void RecordItemPickup(int quantity);

    /// <summary>
    /// Updates gameplay metrics from the live player state. This is called by the client timer and does not depend on a running script.
    /// </summary>
    void UpdateGameplaySnapshot(int experience, int requiredExperience, int level, int gold, int classId, int classPoints, int classPointsToRank, int classRank, bool connected);

    /// <summary>Resets all values.</summary>
    void Reset();

    /// <summary>Gets inventory and bank space.</summary>
    void GetSpace();
}