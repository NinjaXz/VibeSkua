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

    public static string PathFor(string username) =>
        Path.Combine(SyncFile.Directory, $"{username.ToLowerInvariant()}{Suffix}");

    /// <summary>Publish the union of active quests and explicit auto-complete registrations.</summary>
    public static bool Publish(IScriptInterface bot, string username)
    {
        try
        {
            int[] questIds = bot.Quests.Active
                .Select(quest => quest.ID)
                .Concat(bot.Quests.Registered)
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            string content = JsonSerializer.Serialize(questIds);
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
            DebugLog.Log("QuestSync", $"published {questIds.Length} active/registered quest(s) for {username}");
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
    public static int[]? Read(string masterUsername)
    {
        try
        {
            string path = PathFor(masterUsername);
            if (!File.Exists(path))
                return null;

            int[]? questIds = JsonSerializer.Deserialize<int[]>(File.ReadAllText(path));
            return questIds?
                .Where(id => id > 0)
                .Distinct()
                .ToArray();
        }
        catch (Exception ex)
        {
            DebugLog.Log("QuestSync", $"could not read quest list from {masterUsername}: {ex.Message}");
            return null;
        }
    }
}
