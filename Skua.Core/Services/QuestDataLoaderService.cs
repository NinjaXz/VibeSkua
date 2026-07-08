using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Quests;
using System.Dynamic;

namespace Skua.Core.Services;

public class QuestDataLoaderService : IQuestDataLoaderService
{
    public QuestDataLoaderService(IScriptQuest quests, IScriptPlayer player, IFlashUtil flash, IScriptWait wait)
    {
        _quests = quests;
        _flash = flash;
        _player = player;
        _wait = wait;
    }

    private readonly IScriptQuest _quests;
    private readonly IFlashUtil _flash;
    private readonly IScriptPlayer _player;
    private readonly IScriptWait _wait;
    private readonly Dictionary<string, List<QuestData>?> _cachedQuests = new();

    public void ClearCache()
    {
        _cachedQuests.Clear();
    }

    public async Task<List<QuestData>> GetFromFileAsync(string fileName)
    {
        string skuaFile = Path.Combine(ClientFileSources.SkuaDIR, fileName);
        string scriptsFile = Path.Combine(ClientFileSources.SkuaScriptsDIR, fileName);
        string targetFile = File.Exists(skuaFile) ? skuaFile : (File.Exists(scriptsFile) ? scriptsFile : skuaFile);

        if (!File.Exists(targetFile))
            return new();

        if (_cachedQuests.TryGetValue($"CachedQuests_{targetFile}", out List<QuestData>? quests))
            return quests ?? new();

        string text = await File.ReadAllTextAsync(targetFile);
        quests = JsonConvert.DeserializeObject<List<QuestData>>(text);
        _cachedQuests.Add($"CachedQuests_{targetFile}", quests);
        return quests ?? new();
    }

    private async Task SaveQuestDataToFileAsync(string fileName, List<QuestData> quests, CancellationToken token)
    {
        string serialized = JsonConvert.SerializeObject(quests.Distinct().OrderBy(q => q.ID), Formatting.Indented);
        string skuaFile = Path.Combine(ClientFileSources.SkuaDIR, fileName);
        string scriptsFile = Path.Combine(ClientFileSources.SkuaScriptsDIR, fileName);

        if (!Directory.Exists(ClientFileSources.SkuaDIR))
            Directory.CreateDirectory(ClientFileSources.SkuaDIR);
        if (!Directory.Exists(ClientFileSources.SkuaScriptsDIR))
            Directory.CreateDirectory(ClientFileSources.SkuaScriptsDIR);

        CancellationToken writeToken = token.IsCancellationRequested ? CancellationToken.None : token;
        await File.WriteAllTextAsync(skuaFile, serialized, writeToken);
        try { await File.WriteAllTextAsync(scriptsFile, serialized, writeToken); } catch { }
    }

    public async Task<List<QuestData>> UpdateAsync(string fileName, bool all, IProgress<string>? progress, CancellationToken token)
    {
        return await Task.Run(async () =>
        {
            if (!_player.LoggedIn)
                return _quests.Cached = await GetFromFileAsync(fileName);

            // Clear cache to ensure we get fresh data during updates
            string cacheKey = $"CachedQuests_{Path.Combine(ClientFileSources.SkuaDIR, fileName)}";
            string scriptsCacheKey = $"CachedQuests_{Path.Combine(ClientFileSources.SkuaScriptsDIR, fileName)}";
            _cachedQuests.Remove(cacheKey);
            _cachedQuests.Remove(scriptsCacheKey);

            // Load existing data first
            List<QuestData> existingQuestData = await GetFromFileAsync(fileName);
            existingQuestData = existingQuestData.Where(q => IsValidQuestData(q)).ToList();
            _quests.Cached = all ? new List<QuestData>() : existingQuestData;

            int startId = 1;
            int endId = 13000;

            if (all)
            {
                // Rebuild: rebuild to the last QID in the file
                endId = existingQuestData.Count > 0 ? existingQuestData.Max(q => q.ID) : 13000;
                startId = 1;
            }
            else
            {
                // Update: take the last qid and go ++100
                int lastQid = existingQuestData.Count > 0 ? existingQuestData.Max(q => q.ID) : 0;
                startId = lastQid + 1;
                endId = lastQid + 100;
            }

            List<QuestData> quests = new();
            for (int i = startId; i <= endId; i += 29)
            {
                if (token.IsCancellationRequested)
                    break;

                _flash.SetGameObject("world.questTree", new ExpandoObject());
                int questCount = Math.Min(29, endId - i + 1);
                int rangeEnd = i + questCount - 1;
                progress?.Report($"Loading Quests {i}-{rangeEnd}...");

                _quests.Load(Enumerable.Range(i, questCount).ToArray());

                if (!_wait.ForQuestLoad(i, rangeEnd, 100))
                {
                    progress?.Report($"No quests found in range {i}-{rangeEnd}. Continuing...");
                    continue;
                }

                List<Quest> loadedQuests = _quests.Tree.Where(q => q.ID >= i && q.ID <= rangeEnd && IsValidQuest(q)).ToList();
                if (loadedQuests.Count == 0)
                {
                    progress?.Report($"No valid quests found in range {i}-{rangeEnd}. Continuing...");
                    continue;
                }

                quests.AddRange(loadedQuests.Select(q => ConvertToQuestData(q)));
                if (!token.IsCancellationRequested)
                    await Task.Delay(1500);
            }

            // Handle cancellation gracefully and merge data appropriately
            if (!all)
            {
                // For incremental updates, merge with existing cached data
                quests.AddRange(_quests.Cached);
            }
            else if (token.IsCancellationRequested)
            {
                if (quests.Count == 0)
                {
                    if (existingQuestData.Count > 0)
                    {
                        progress?.Report("Update cancelled - keeping existing quest data");
                        return _quests.Cached = existingQuestData;
                    }
                }
                else
                {
                    if (quests.Any())
                    {
                        int maxNewId = quests.Max(q => q.ID);
                        IEnumerable<QuestData> olderExistingData = existingQuestData.Where(q => q.ID > maxNewId);
                        quests.AddRange(olderExistingData);
                        progress?.Report($"Update cancelled - saved {quests.Count} quests (partial data + existing)");
                    }
                    else
                    {
                        progress?.Report("Update cancelled - keeping existing quest data");
                        return _quests.Cached = existingQuestData;
                    }
                }
            }

            quests = quests.Where(q => IsValidQuestData(q)).Distinct().ToList();
            await SaveQuestDataToFileAsync(fileName, quests, token);
            progress?.Report($"Getting quests from file {fileName}");

            _cachedQuests.Remove(cacheKey);
            _cachedQuests.Remove(scriptsCacheKey);

            HashSet<int> existingQuestIds = quests.Select(q => q.ID).ToHashSet();
            quests.AddRange(_quests.Cached.Where(q => !existingQuestIds.Contains(q.ID) && IsValidQuestData(q)));
            quests = quests.Where(q => IsValidQuestData(q)).Distinct().ToList();
            await SaveQuestDataToFileAsync(fileName, quests, token);
            progress?.Report($"Getting quests from file {fileName}");

            _cachedQuests.Remove(cacheKey);
            _cachedQuests.Remove(scriptsCacheKey);
            return _quests.Cached = await GetFromFileAsync(fileName);
        });
    }

    public async Task<List<QuestData>> UpdateRangeAsync(string fileName, int startId, int endId, IProgress<string>? progress, CancellationToken token)
    {
        return await Task.Run(async () =>
        {
            if (!_player.LoggedIn)
                return _quests.Cached = await GetFromFileAsync(fileName);

            string cacheKey = $"CachedQuests_{Path.Combine(ClientFileSources.SkuaDIR, fileName)}";
            string scriptsCacheKey = $"CachedQuests_{Path.Combine(ClientFileSources.SkuaScriptsDIR, fileName)}";
            _cachedQuests.Remove(cacheKey);
            _cachedQuests.Remove(scriptsCacheKey);

            _quests.Cached = await GetFromFileAsync(fileName);
            List<QuestData> existingQuestData = _quests.Cached.Where(q => IsValidQuestData(q)).ToList();

            List<QuestData> quests = new();
            for (int i = startId; i <= endId; i += 29)
            {
                if (token.IsCancellationRequested)
                    break;

                _flash.SetGameObject("world.questTree", new ExpandoObject());
                int questCount = Math.Min(29, endId - i + 1);
                int rangeEnd = i + questCount - 1;
                progress?.Report($"Loading Quests {i}-{rangeEnd}...");

                _quests.Load(Enumerable.Range(i, questCount).ToArray());

                if (!_wait.ForQuestLoad(i, rangeEnd, 100))
                {
                    progress?.Report($"No quests found in range {i}-{rangeEnd}. Continuing...");
                    continue;
                }

                List<Quest> loadedQuests = _quests.Tree.Where(q => q.ID >= i && q.ID <= rangeEnd && IsValidQuest(q)).ToList();
                if (loadedQuests.Count == 0)
                {
                    progress?.Report($"No valid quests found in range {i}-{rangeEnd}. Continuing...");
                    continue;
                }

                quests.AddRange(loadedQuests.Select(q => ConvertToQuestData(q)));
                if (!token.IsCancellationRequested)
                    await Task.Delay(1500);
            }

            HashSet<int> existingQuestIds = quests.Select(q => q.ID).ToHashSet();
            quests.AddRange(existingQuestData.Where(q => !existingQuestIds.Contains(q.ID) && IsValidQuestData(q)));
            quests = quests.Where(q => IsValidQuestData(q)).Distinct().ToList();
            await SaveQuestDataToFileAsync(fileName, quests, token);
            progress?.Report($"Getting quests from file {fileName}");

            _cachedQuests.Remove(cacheKey);
            _cachedQuests.Remove(scriptsCacheKey);

            return _quests.Cached = await GetFromFileAsync(fileName);
        });
    }

    private bool IsValidQuest(Quest q)
    {
        if (q == null || q.ID <= 0)
            return false;
        if (string.IsNullOrWhiteSpace(q.Name))
            return false;
        string name = q.Name.Trim();
        if (name.Equals("Undefined", StringComparison.OrdinalIgnoreCase) || name.Equals("Empty", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private bool IsValidQuestData(QuestData q)
    {
        if (q == null || q.ID <= 0)
            return false;
        if (string.IsNullOrWhiteSpace(q.Name))
            return false;
        string name = q.Name.Trim();
        if (name.Equals("Undefined", StringComparison.OrdinalIgnoreCase) || name.Equals("Empty", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private QuestData ConvertToQuestData(Quest q)
    {
        return new()
        {
            ID = q.ID,
            Name = q.Name,
            AcceptRequirements = q.AcceptRequirements,
            Field = q.Field,
            Gold = q.Gold,
            Index = q.Index,
            Level = q.Level,
            Once = q.Once,
            RequiredClassID = q.RequiredClassID,
            RequiredClassPoints = q.RequiredClassPoints,
            RequiredFactionId = q.RequiredFactionId,
            RequiredFactionRep = q.RequiredFactionRep,
            Requirements = q.Requirements,
            Rewards = q.Rewards,
            SimpleRewards = q.SimpleRewards,
            Slot = q.Slot,
            Upgrade = q.Upgrade,
            Value = q.Value,
            XP = q.XP
        };
    }
}
