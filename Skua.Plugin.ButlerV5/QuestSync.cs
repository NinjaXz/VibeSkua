using System.IO;
using System.Text.Json;
using Skua.Core.Interfaces;

namespace ButlerV5;

/// <summary>
/// Publishes the master's active and explicitly registered quest IDs so followers
/// can register the same quests locally and collect/complete them with their own drops.
/// </summary>
public static class QuestSync
{
    public const string Suffix = "_butlerv5_activequests";

    public sealed record QuestSyncEntry(int QuestId, int RewardId);

    public static string PathFor(string username) =>
        Path.Combine(SyncFile.Directory, $"{username.ToLowerInvariant()}{Suffix}");

    /// <summary>Publish the union of active quests and explicit auto-complete registrations.</summary>
    public static bool Publish(IScriptInterface bot, string username)
    {
        try
        {
            Dictionary<int, int> questRewards = new();
            foreach (Skua.Core.Models.Quests.Quest quest in bot.Quests.Active)
            {
                if (quest.ID > 0)
                    questRewards.TryAdd(quest.ID, -1);
            }

            IReadOnlyDictionary<int, int> registeredRewards = bot.Quests.RegisteredRewards;
            foreach (int questId in bot.Quests.Registered)
            {
                if (questId <= 0)
                    continue;

                int rewardId = registeredRewards.TryGetValue(questId, out int selectedReward) ? selectedReward : -1;
                questRewards[questId] = rewardId;
            }

            QuestSyncEntry[] entries = questRewards
                .OrderBy(pair => pair.Key)
                .Select(pair => new QuestSyncEntry(pair.Key, pair.Value))
                .ToArray();

            string content = JsonSerializer.Serialize(entries);
            string path = PathFor(username);
            try
            {
                if (File.Exists(path) && File.ReadAllText(path) == content)
                    return false;
            }
            catch
            {
            }

            Directory.CreateDirectory(SyncFile.Directory);
            File.WriteAllText(path, content);
            DebugLog.Log("QuestSync", $"published {entries.Length} active/registered quest(s) for {username}");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Log("QuestSync", $"publish failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Timestamp used by followers to detect a new quest list without rereading every tick.</summary>
    public static DateTime GetFileTime(string masterUsername)
    {
        try
        {
            string path = PathFor(masterUsername);
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>Read the master's quest IDs. Null means no valid publish exists yet.</summary>
    public static QuestSyncEntry[]? Read(string masterUsername)
    {
        try
        {
            string path = PathFor(masterUsername);
            if (!File.Exists(path))
                return null;

            QuestSyncEntry[]? entries = JsonSerializer.Deserialize<QuestSyncEntry[]>(File.ReadAllText(path));
            return entries?
                .Where(entry => entry.QuestId > 0)
                .GroupBy(entry => entry.QuestId)
                .Select(group => group.Last())
                .ToArray();
        }
        catch (Exception ex)
        {
            DebugLog.Log("QuestSync", $"could not read quest list from {masterUsername}: {ex.Message}");
            return null;
        }
    }
}
