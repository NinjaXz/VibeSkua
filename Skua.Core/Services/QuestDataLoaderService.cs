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
        fileName = Path.Combine(ClientFileSources.SkuaDIR, fileName);
        if (!File.Exists(fileName))
            return new();

        if (_cachedQuests.TryGetValue($"CachedQuests_{fileName}", out List<QuestData>? quests))
            return quests ?? new();

        string text = await File.ReadAllTextAsync(fileName);
        quests = JsonConvert.DeserializeObject<List<QuestData>>(text);
        _cachedQuests.Add($"CachedQuests_{fileName}", quests);
        return quests ?? new();
    }

    public async Task<List<QuestData>> UpdateAsync(string fileName, bool all, IProgress<string>? progress, CancellationToken token, bool probePastCeiling = false)
    {
        return await Task.Run(async () =>
        {
            if (!_player.LoggedIn)
                return _quests.Cached = await GetFromFileAsync(fileName);

            // Clear cache to ensure we get fresh data during updates
            string cacheKey = $"CachedQuests_{Path.Combine(ClientFileSources.SkuaDIR, fileName)}";
            _cachedQuests.Remove(cacheKey);

            // Load existing data first - we'll use this for incremental updates or if cancellation happens
            List<QuestData> existingQuestData = await GetFromFileAsync(fileName);
            _quests.Cached = all ? new List<QuestData>() : existingQuestData;

            int start = 1;
            if (!all && _quests.Cached.Count > 0)
                start = _quests.Cached.Max(q => q.ID) + 1;

            int safeMaximum = GetSafeQuestMaximum(existingQuestData);
            if (TryReadQuestMarker(Path.Combine(ClientFileSources.SkuaDIR, "QuestCeiling.txt"), out int knownBadId))
                progress?.Report($"Known quest ceiling: {safeMaximum} (quest {knownBadId} disconnects).");

            List<QuestData> quests = new();
            QuestRequestThrottle throttle = new();
            int emptyInARow = 0;

            if (!all && start > safeMaximum)
                progress?.Report("No new quests remain within the remembered safe ceiling. Enable Probe newer IDs to test beyond it.");

            for (int i = start; i <= safeMaximum; i += 29)
            {
                if (token.IsCancellationRequested)
                    break;

                int rangeEnd = Math.Min(i + 28, safeMaximum);
                progress?.Report($"Loading Quests {i}-{rangeEnd}...");

                List<Quest> loadedQuests = await FetchRangeWithFallbackAsync(i, rangeEnd, throttle, token);
                if (!_player.LoggedIn)
                {
                    progress?.Report($"Disconnected while loading quests {i}-{rangeEnd}. Stopping without guessing which ID caused it.");
                    break;
                }

                if (token.IsCancellationRequested)
                    break;

                if (loadedQuests.Count == 0)
                {
                    if (++emptyInARow >= MaxEmptyBatches)
                    {
                        progress?.Report($"{MaxEmptyBatches} consecutive empty batches, stopping at quest {i}.");
                        break;
                    }

                    progress?.Report($"No quests found in {i}-{rangeEnd}; continuing to the next batch.");
                    continue;
                }

                emptyInARow = 0;
                quests.AddRange(loadedQuests.Select(q => ConvertToQuestData(q)));
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
                    // If cancelled early in full update with no new data, keep existing data
                    if (existingQuestData.Count > 0)
                    {
                        progress?.Report("Update cancelled - keeping existing quest data");
                        return _quests.Cached = existingQuestData;
                    }
                }
                else
                {
                    // If we got some new data before cancellation, merge it with existing data
                    // Keep newer data (higher IDs) from new fetch, older data from existing
                    if (quests.Any())
                    {
                        int maxNewId = quests.Max(q => q.ID);
                        IEnumerable<QuestData> olderExistingData = existingQuestData.Where(q => q.ID > maxNewId);
                        quests.AddRange(olderExistingData);
                        progress?.Report($"Update cancelled - saved {quests.Count} quests (partial data + existing)");
                    }
                    else
                    {
                        // No new data was fetched, just keep existing data
                        progress?.Report("Update cancelled - keeping existing quest data");
                        return _quests.Cached = existingQuestData;
                    }
                }
            }

            // Don't pass cancelled token to file write operation
            CancellationToken writeToken = token.IsCancellationRequested ? CancellationToken.None : token;
            await File.WriteAllTextAsync(Path.Combine(ClientFileSources.SkuaDIR, fileName), JsonConvert.SerializeObject(quests.Distinct().OrderBy(q => q.ID), Formatting.Indented), writeToken);
            progress?.Report($"Getting quests from file {fileName}");

            // Clear cache again to force reading the newly written file
            _cachedQuests.Remove(cacheKey);

            HashSet<int> existingQuestIds = quests.Select(q => q.ID).ToHashSet();
            quests.AddRange(_quests.Cached.Where(q => !existingQuestIds.Contains(q.ID)));
            await File.WriteAllTextAsync(Path.Combine(ClientFileSources.SkuaDIR, fileName), JsonConvert.SerializeObject(quests.OrderBy(q => q.ID), Formatting.Indented), token);
            progress?.Report($"Getting quests from file {fileName}");

            _cachedQuests.Remove($"CachedQuests_{Path.Combine(ClientFileSources.SkuaDIR, fileName)}");
            List<QuestData> updatedQuestData = _quests.Cached = await GetFromFileAsync(fileName);

            if (probePastCeiling && _player.LoggedIn && !token.IsCancellationRequested)
                updatedQuestData = await ProbePastCeilingAsync(fileName, updatedQuestData, progress, token);

            return _quests.Cached = updatedQuestData;
        });
    }

    public async Task<List<QuestData>> UpdateRangeAsync(string fileName, int startId, int endId, IProgress<string>? progress, CancellationToken token)
    {
        return await Task.Run(async () =>
        {
            if (!_player.LoggedIn)
                return _quests.Cached = await GetFromFileAsync(fileName);

            _quests.Cached = await GetFromFileAsync(fileName);

            List<QuestData> quests = new();
            QuestRequestThrottle throttle = new();

            for (int i = startId; i <= endId; i += 29)
            {
                if (token.IsCancellationRequested)
                    break;

                int questCount = Math.Min(29, endId - i + 1);
                int rangeEnd = i + questCount - 1;
                progress?.Report($"Loading Quests {i}-{rangeEnd}...");

                List<Quest> loadedQuests = await FetchRangeWithFallbackAsync(i, rangeEnd, throttle, token);
                if (!_player.LoggedIn)
                {
                    progress?.Report($"Disconnected while loading quests {i}-{rangeEnd}. Stopping.");
                    break;
                }

                if (token.IsCancellationRequested)
                    break;

                if (loadedQuests.Count == 0)
                {
                    progress?.Report($"No quests found in {i}-{rangeEnd}; continuing through the requested range.");
                    continue;
                }

                quests.AddRange(loadedQuests.Select(q => ConvertToQuestData(q)));
            }

            HashSet<int> existingQuestIds = quests.Select(q => q.ID).ToHashSet();
            quests.AddRange(_quests.Cached.Where(q => !existingQuestIds.Contains(q.ID)));
            await File.WriteAllTextAsync(Path.Combine(ClientFileSources.SkuaDIR, fileName), JsonConvert.SerializeObject(quests.OrderBy(q => q.ID), Formatting.Indented), token);
            progress?.Report($"Getting quests from file {fileName}");

            _cachedQuests.Remove($"CachedQuests_{Path.Combine(ClientFileSources.SkuaDIR, fileName)}");

            return _quests.Cached = await GetFromFileAsync(fileName);
        });
    }

    private const int DefaultKnownQuestMaxId = 10882;
    private const int MaxQuestProbeId = 13000;
    private const int MaxEmptyBatches = 10;

    private sealed class QuestRequestThrottle
    {
        private const int RequestDelayMilliseconds = 15000;
        private const int CooldownEvery = 10;
        private const int CooldownMilliseconds = 30000;
        private int _requestCount;

        public async Task WaitAsync(CancellationToken token)
        {
            if (_requestCount > 0)
                await Task.Delay(RequestDelayMilliseconds, token);

            _requestCount++;
            if (_requestCount % CooldownEvery == 0)
                await Task.Delay(CooldownMilliseconds, token);
        }
    }

    private static bool TryReadQuestMarker(string path, out int questId)
    {
        questId = 0;
        try
        {
            return File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out questId) && questId > 0;
        }
        catch
        {
            return false;
        }
    }

    private static int GetSafeQuestMaximum(List<QuestData> existingQuestData)
    {
        string ceilingPath = Path.Combine(ClientFileSources.SkuaDIR, "QuestCeiling.txt");
        if (TryReadQuestMarker(ceilingPath, out int knownBadId))
            return Math.Max(0, knownBadId - 1);

        string testedPath = Path.Combine(ClientFileSources.SkuaDIR, "QuestMaxTested.txt");
        int cachedMaximum = existingQuestData.Count > 0 ? existingQuestData.Max(q => q.ID) : 0;
        int testedMaximum = TryReadQuestMarker(testedPath, out int lastTestedId) ? lastTestedId : 0;
        return Math.Max(DefaultKnownQuestMaxId, Math.Max(cachedMaximum, testedMaximum));
    }

    private async Task<List<Quest>> RequestQuestRangeAsync(int startId, int endId, QuestRequestThrottle throttle, CancellationToken token, bool waitBeforeRequest = true)
    {
        if (token.IsCancellationRequested || !_player.LoggedIn)
            return new();

        if (waitBeforeRequest)
            await throttle.WaitAsync(token);

        if (token.IsCancellationRequested || !_player.LoggedIn)
            return new();

        _flash.SetGameObject("world.questTree", new ExpandoObject());
        _quests.Load(Enumerable.Range(startId, endId - startId + 1).ToArray());

        // ForQuestLoad can complete against a stale tree-count increase, so it is
        // only an early signal. Allow the response to finish populating the tree
        // before taking the batch snapshot.
        _wait.ForQuestLoad(startId, endId, 100);
        await Task.Delay(3000, token);

        if (token.IsCancellationRequested || !_player.LoggedIn)
            return new();

        return _quests.Tree.Where(q => q.ID >= startId && q.ID <= endId).ToList();
    }

    private Task<List<Quest>> FetchRangeWithFallbackAsync(int startId, int endId, QuestRequestThrottle throttle, CancellationToken token)
    {
        // Keep normal Loader updates to exactly one getQuests request per batch.
        // Empty results are not retried with smaller batches because that multiplies
        // packet traffic and can trigger the server's logout protection.
        return RequestQuestRangeAsync(startId, endId, throttle, token);
    }

    private async Task<List<QuestData>> ProbePastCeilingAsync(string fileName, List<QuestData> existingData, IProgress<string>? progress, CancellationToken token)
    {
        string ceilingPath = Path.Combine(ClientFileSources.SkuaDIR, "QuestCeiling.txt");
        string testedPath = Path.Combine(ClientFileSources.SkuaDIR, "QuestMaxTested.txt");
        string dataPath = Path.Combine(ClientFileSources.SkuaDIR, fileName);

        int cachedMaximum = existingData.Count > 0 ? existingData.Max(q => q.ID) : 0;
        int testedMaximum = TryReadQuestMarker(testedPath, out int lastTestedId) ? lastTestedId : 0;
        int firstId = TryReadQuestMarker(ceilingPath, out int knownBadId)
            ? knownBadId
            : Math.Max(DefaultKnownQuestMaxId, Math.Max(cachedMaximum, testedMaximum)) + 1;

        if (firstId > MaxQuestProbeId)
        {
            progress?.Report($"Probe limit reached at quest {MaxQuestProbeId}.");
            return existingData;
        }

        progress?.Report($"Probing newer quest IDs from {firstId}, one at a time. The first rejected ID will disconnect the game and be remembered.");
        HashSet<int> existingIds = existingData.Select(q => q.ID).ToHashSet();
        QuestRequestThrottle throttle = new();
        int emptyInARow = 0;

        for (int id = firstId; id <= MaxQuestProbeId && !token.IsCancellationRequested; id++)
        {
            if (!_player.LoggedIn)
            {
                progress?.Report("Player is no longer logged in. Probe stopped.");
                break;
            }

            await throttle.WaitAsync(token);
            if (token.IsCancellationRequested || !_player.LoggedIn)
                break;

            // Persist the candidate immediately before sending so a disconnect leaves an exact ID to recover.
            File.WriteAllText(ceilingPath, id.ToString());

            List<Quest> loadedQuests;
            try
            {
                loadedQuests = await RequestQuestRangeAsync(id, id, throttle, token, false);
            }
            catch (Exception ex)
            {
                if (_player.LoggedIn && File.Exists(ceilingPath))
                    File.Delete(ceilingPath);
                progress?.Report($"Probe at quest {id} failed: {ex.Message}");
                break;
            }

            if (!_player.LoggedIn)
            {
                progress?.Report($"Disconnected at quest {id}. Quest {id - 1} is the remembered safe ceiling; normal updates will stop before it.");
                break;
            }

            if (token.IsCancellationRequested)
            {
                if (File.Exists(ceilingPath))
                    File.Delete(ceilingPath);
                break;
            }

            if (File.Exists(ceilingPath))
                File.Delete(ceilingPath);
            File.WriteAllText(testedPath, id.ToString());

            if (loadedQuests.Count > 0)
            {
                bool changed = false;
                foreach (Quest quest in loadedQuests)
                {
                    if (existingIds.Add(quest.ID))
                    {
                        existingData.Add(ConvertToQuestData(quest));
                        changed = true;
                    }
                }

                if (changed)
                {
                    existingData = existingData.OrderBy(q => q.ID).ToList();
                    _quests.Cached = existingData;
                    await File.WriteAllTextAsync(dataPath, JsonConvert.SerializeObject(existingData, Formatting.Indented), CancellationToken.None);
                    _cachedQuests.Remove($"CachedQuests_{dataPath}");
                    progress?.Report($"Found quest {id}. Saved {existingData.Count} quests.");
                }

                emptyInARow = 0;
            }
            else if (++emptyInARow >= MaxEmptyBatches)
            {
                progress?.Report($"{MaxEmptyBatches} consecutive empty quest IDs. Probe paused at {id}; the next probe will resume after it.");
                break;
            }
        }

        return _quests.Cached = existingData;
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
