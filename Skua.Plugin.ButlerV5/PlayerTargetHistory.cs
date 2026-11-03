using System.Text.Json;

namespace ButlerV5;

/// <summary>
/// Stores recently used external player targets so they can be reused without retyping.
/// The history is shared by ButlerV5 instances on this PC.
/// </summary>
public static class PlayerTargetHistory
{
    private const int MaxEntries = 20;
    private static readonly object Sync = new();
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Skua",
        "butlerv5_player_targets.json");

    public static List<string> Load()
    {
        lock (Sync)
            return LoadUnsafe();
    }

    public static List<string> Add(string target)
    {
        string normalized = target.Trim();
        if (normalized.Length == 0)
            return Load();

        lock (Sync)
        {
            List<string> history = LoadUnsafe();
            history.RemoveAll(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            history.Insert(0, normalized);
            if (history.Count > MaxEntries)
                history.RemoveRange(MaxEntries, history.Count - MaxEntries);

            try
            {
                string? directory = Path.GetDirectoryName(HistoryPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(HistoryPath, JsonSerializer.Serialize(history));
            }
            catch (Exception ex)
            {
                DebugLog.Log("TargetHistory", $"could not save target history: {ex.Message}");
            }

            return history;
        }
    }

    private static List<string> LoadUnsafe()
    {
        try
        {
            if (!File.Exists(HistoryPath))
                return new();

            List<string>? history = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(HistoryPath));
            return history?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntries)
                .ToList() ?? new();
        }
        catch (Exception ex)
        {
            DebugLog.Log("TargetHistory", $"could not load target history: {ex.Message}");
            return new();
        }
    }
}
