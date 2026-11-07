using Skua.Core.Models.Quests;

namespace Skua.Core.Interfaces;

public interface IQuestDataLoaderService
{
    Task<List<QuestData>> GetFromFileAsync(string fileName);

    Task<List<QuestData>> UpdateAsync(string fileName, bool all, IProgress<string>? progress, CancellationToken token, bool probePastCeiling = false);
    void ClearCache();
    Task<List<QuestData>> UpdateRangeAsync(string fileName, int startId, int endId, IProgress<string>? progress, CancellationToken token);

}
