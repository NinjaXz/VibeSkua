using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.GitHub;
using Skua.Core.Utils;

namespace Skua.Core.Services;

public partial class GetScriptsService : ObservableObject, IGetScriptsService
{
    private readonly IDialogService _dialogService;
    private const string _rawScriptsJsonUrl = "auqw/Scripts/refs/heads/Skua/scripts.json";
    private const string _skillsSetsRawUrl = "auqw/Scripts/refs/heads/Skua/Skills/AdvancedSkills.json";
    private const string _questDataRawUrl = "auqw/Scripts/refs/heads/Skua/QuestData.json";
    private const string _junkItemsRawUrl = "auqw/Scripts/refs/heads/Skua/JunkItems.json";
    private const string _repoOwner = "auqw";
    private const string _repoName = "Scripts";
    private const string _repoBranch = "Skua";

    [ObservableProperty]
    private RangedObservableCollection<ScriptInfo> _scripts = new();

    public GetScriptsService(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public async ValueTask<List<ScriptInfo>> GetScriptsAsync(IProgress<string>? progress, CancellationToken token)
    {
        if (_scripts.Any())
            return _scripts.ToList();

        await GetScripts(progress, false, token);

        return _scripts.ToList();
    }

    public async Task RefreshScriptsAsync(IProgress<string>? progress, CancellationToken token)
    {
        await GetScripts(progress, true, token);
    }

    private Task? _fetchTask;

    private Task GetScripts(IProgress<string>? progress, bool refresh, CancellationToken token)
    {
        if (_fetchTask != null)
            return _fetchTask;

        _fetchTask = GetScriptsInternal(progress, refresh, token);
        return _fetchTask;
    }

    private async Task GetScriptsInternal(IProgress<string>? progress, bool refresh, CancellationToken token)
    {
        try
        {
            Scripts.Clear();

            progress?.Report("Fetching scripts...");
            List<ScriptInfo> scripts = await GetScriptsInfo(refresh, token);

            progress?.Report($"Found {scripts.Count} scripts.");
            _scripts.AddRange(scripts);

            progress?.Report($"Fetched {scripts.Count} scripts.");
            OnPropertyChanged(nameof(Scripts));

            // Fetch dates in the background and notify UI again when done
            await UpdateScriptDatesAsync(progress, token);
        }
        catch (TaskCanceledException)
        {
            progress?.Report("Task Cancelled.");
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            _dialogService.ShowMessageBox(
                "Unable to connect to GitHub.\r\n" +
                "Please check your internet connection and try again.\r\n\r\n" +
                "If the problem persists, GitHub may be temporarily unavailable.",
                "Network Error");
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Something went wrong when retrieving scripts.\r\nPlease, try again later.\r\n Error: {ex}", "Search Scripts Error");
        }
        finally
        {
            _fetchTask = null;
        }
    }

    private async Task<List<ScriptInfo>> GetScriptsInfo(bool refresh, CancellationToken token)
    {
        if (_scripts.Count != 0 && !refresh)
            return _scripts.ToList();

        using HttpResponseMessage response = await ValidatedHttpExtensions.GetAsync(HttpClients.GitHubRaw, _rawScriptsJsonUrl, token);
        string content = await response.Content.ReadAsStringAsync(token);
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidDataException("scripts.json is empty or null");

        List<ScriptInfo>? scripts = JsonConvert.DeserializeObject<List<ScriptInfo>>(content);
        if (scripts == null || !scripts.Any()) throw new InvalidDataException("scripts.json contains no valid scripts");

        try
        {
            string datesFile = Path.Combine(AppContext.BaseDirectory, "ScriptDates.json");
            if (File.Exists(datesFile))
            {
                var dict = JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(File.ReadAllText(datesFile));
                if (dict != null)
                {
                    foreach (var s in scripts)
                    {
                        if (dict.TryGetValue(s.FilePath, out var date))
                            s.CreationDate = date;
                    }
                }
            }
        } catch { }

        return scripts;
    }

    public async Task UpdateScriptDatesAsync(IProgress<string>? progress, CancellationToken token)
    {
        string datesFile = Path.Combine(AppContext.BaseDirectory, "ScriptDates.json");
        Dictionary<string, DateTime> dict = new();
        if (File.Exists(datesFile))
        {
            try { dict = JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(await File.ReadAllTextAsync(datesFile, token)) ?? new(); } catch { }
        }

        int newScripts = 0;
        using HttpClient client = new();
        client.DefaultRequestHeaders.Add("User-Agent", "Skua-App");

        foreach (var script in Scripts)
        {
            if (!dict.ContainsKey(script.FilePath))
            {
                try
                {
                    progress?.Report($"Fetching date for {script.FileName}...");
                    using HttpResponseMessage response = await client.GetAsync($"https://api.github.com/repos/auqw/Scripts/commits?path={script.FilePath.Replace(" ", "%20")}", token);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync(token);
                        dynamic? commits = JsonConvert.DeserializeObject<dynamic>(content);
                        if (commits != null && commits.Count > 0)
                        {
                            var lastCommit = commits[commits.Count - 1];
                            DateTime date = lastCommit.commit.committer.date;
                            dict[script.FilePath] = date;
                            script.CreationDate = date;
                            newScripts++;
                        }
                    }
                }
                catch { }
            }
        }

        if (newScripts > 0)
        {
            try { await File.WriteAllTextAsync(datesFile, JsonConvert.SerializeObject(dict, Formatting.Indented), token); } catch { }
            progress?.Report($"Updated {newScripts} script dates.");
            OnPropertyChanged(nameof(Scripts));
        }
        else
        {
            progress?.Report("All script dates are already up to date.");
        }
    }

    public async Task DownloadScriptAsync(ScriptInfo info)
    {
        DirectoryInfo parent = Directory.GetParent(info.LocalFile)!;
        if (!parent.Exists)
            parent.Create();

        using HttpResponseMessage response = await ValidatedHttpExtensions.GetAsync(HttpClients.GitHubRaw, info.DownloadUrl);
        byte[] scriptBytes = await response.Content.ReadAsByteArrayAsync();
        await File.WriteAllBytesAsync(info.LocalFile, scriptBytes);
    }

    public async Task<int> DownloadAllWhereAsync(Func<ScriptInfo, bool> pred)
    {
        List<ScriptInfo> toUpdate = _scripts.Where(pred).ToList();
        
        // Defensive: Throttle concurrent downloads to prevent HttpClient timeouts and GitHub rate-limiting
        using SemaphoreSlim semaphore = new SemaphoreSlim(15);
        var tasks = toUpdate.Select(async s =>
        {
            await semaphore.WaitAsync();
            try
            {
                await DownloadScriptAsync(s);
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        await Task.WhenAll(tasks);
        return toUpdate.Count;
    }

    public async Task DeleteScriptAsync(ScriptInfo info)
    {
        await Task.Run(() =>
        {
            try
            {
                File.Delete(info.LocalFile);
            }
            catch { }
        });
    }

    public async Task<long> CheckAdvanceSkillSetsUpdates()
    {
        try
        {
            long localSize = 0;
            if (File.Exists(ClientFileSources.SkuaAdvancedSkillsFile))
            {
                FileInfo fileInfo = new(ClientFileSources.SkuaAdvancedSkillsFile);
                localSize = fileInfo.Length;
            }

            string content = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GitHubRaw, _skillsSetsRawUrl);
            long remoteSize = content.Length;

            return remoteSize != localSize ? remoteSize : 0;
        }
        catch
        {
            return -1;
        }
    }

    public async Task<bool> UpdateSkillSetsFile()
    {
        try
        {
            string content = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GitHubRaw, _skillsSetsRawUrl);
            await File.WriteAllTextAsync(ClientFileSources.SkuaAdvancedSkillsFile, content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateQuestDataFile()
    {
        try
        {
            string content = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GitHubRaw, _questDataRawUrl);
            if (File.Exists(ClientFileSources.SkuaQuestsFile))
            {
                string localContent = await File.ReadAllTextAsync(ClientFileSources.SkuaQuestsFile);
                List<Skua.Core.Models.Quests.QuestData> localQuests = JsonConvert.DeserializeObject<List<Skua.Core.Models.Quests.QuestData>>(localContent) ?? new();
                List<Skua.Core.Models.Quests.QuestData> remoteQuests = JsonConvert.DeserializeObject<List<Skua.Core.Models.Quests.QuestData>>(content) ?? new();
                
                HashSet<int> remoteIds = remoteQuests.Select(q => q.ID).ToHashSet();
                remoteQuests.AddRange(localQuests.Where(q => !remoteIds.Contains(q.ID)));
                
                await File.WriteAllTextAsync(ClientFileSources.SkuaQuestsFile, JsonConvert.SerializeObject(remoteQuests.OrderBy(q => q.ID), Formatting.Indented));
            }
            else
            {
                await File.WriteAllTextAsync(ClientFileSources.SkuaQuestsFile, content);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<long> CheckJunkItemsUpdates()
    {
        try
        {
            long localSize = 0;
            if (File.Exists(ClientFileSources.SkuaJunkItemsFile))
            {
                FileInfo fileInfo = new(ClientFileSources.SkuaJunkItemsFile);
                localSize = fileInfo.Length;
            }

            string content = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GitHubRaw, _junkItemsRawUrl);
            long remoteSize = content.Length;

            return remoteSize != localSize ? remoteSize : 0;
        }
        catch
        {
            return -1;
        }
    }

    public async Task<bool> UpdateJunkItemsFile()
    {
        try
        {
            string content = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GitHubRaw, _junkItemsRawUrl);
            await File.WriteAllTextAsync(ClientFileSources.SkuaJunkItemsFile, content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> GetLastCommitShaAsync(CancellationToken token)
    {
        try
        {
            string url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/commits/{_repoBranch}";
            using HttpResponseMessage response = await HttpClients.MakeGitHubApiRequestAsync(url);
            string content = await response.Content.ReadAsStringAsync(token);
            GitHubCommit? commit = JsonConvert.DeserializeObject<GitHubCommit>(content);
            return commit?.Sha;
        }
        catch
        {
            return null;
        }
    }

    private async Task<HashSet<string>> GetChangedFilesAsync(string oldSha, string newSha, CancellationToken token)
    {
        try
        {
            string url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/compare/{oldSha}...{newSha}";
            using HttpResponseMessage response = await HttpClients.MakeGitHubApiRequestAsync(url);
            string content = await response.Content.ReadAsStringAsync(token);
            GitHubCompare? compare = JsonConvert.DeserializeObject<GitHubCompare>(content);

            return compare?.Files == null
                ? new HashSet<string>()
                : compare.Files
                .Where(f => f.Status != "removed")
                .Select(f => f.FileName)
                .ToHashSet();
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Error getting changed files: {ex.Message}", "Debug Info");
            return new HashSet<string>();
        }
    }

    private string? GetStoredCommitSha()
    {
        try
        {
            if (File.Exists(ClientFileSources.SkuaScriptsCommitFile))
                return File.ReadAllText(ClientFileSources.SkuaScriptsCommitFile).Trim();
        }
        catch { }
        return null;
    }

    private async Task StoreCommitShaAsync(string sha)
    {
        try
        {
            await File.WriteAllTextAsync(ClientFileSources.SkuaScriptsCommitFile, sha);
        }
        catch { }
    }

    public IEnumerable<ScriptInfo> GetOutdatedScripts()
    {
        return _scripts.Where(s => s.Outdated).ToList();
    }

    public async Task<int> IncrementalUpdateScriptsAsync(IProgress<string>? progress, CancellationToken token)
    {
        try
        {
            progress?.Report("Checking for updates...");

            string? currentSha = await GetLastCommitShaAsync(token);
            if (string.IsNullOrEmpty(currentSha))
            {
                progress?.Report("Failed to get latest commit. Performing full refresh...");
                await RefreshScriptsAsync(progress, token);
                return 0;
            }

            string? storedSha = GetStoredCommitSha();
            if (string.IsNullOrEmpty(storedSha))
            {
                progress?.Report("First time setup. Downloading all scripts...");
                await RefreshScriptsAsync(progress, token);
                await StoreCommitShaAsync(currentSha);
                return _scripts.Count;
            }

            if (storedSha == currentSha)
            {
                progress?.Report("Scripts are up to date.");
                return 0;
            }

            progress?.Report("Fetching changed files...");
            HashSet<string> changedFiles = await GetChangedFilesAsync(storedSha, currentSha, token);

            if (changedFiles.Count == 0)
            {
                progress?.Report("No script changes detected.");
                await StoreCommitShaAsync(currentSha);
                return 0;
            }

            HashSet<string> scriptChangedFiles = changedFiles
                .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && f != "scripts.json")
                .ToHashSet();

            if (scriptChangedFiles.Count == 0)
            {
                progress?.Report("No script changes detected (only metadata files changed).");
                await StoreCommitShaAsync(currentSha);
                return 0;
            }

            progress?.Report($"Found {scriptChangedFiles.Count} changed scripts. Updating...");

            List<ScriptInfo> scripts = await GetScriptsInfo(true, token);
            List<ScriptInfo> scriptsToUpdate = scripts.Where(s => scriptChangedFiles.Contains(s.FilePath)).ToList();

            int updated = 0;
            foreach (ScriptInfo? script in scriptsToUpdate)
            {
                if (token.IsCancellationRequested)
                    break;

                try
                {
                    await DownloadScriptAsync(script);
                    updated++;
                    progress?.Report($"Updated {updated}/{scriptsToUpdate.Count}: {script.Name}");
                }
                catch (Exception ex)
                {
                    progress?.Report($"Failed to update {script.Name}: {ex.Message}");
                }
            }

            await StoreCommitShaAsync(currentSha);
            progress?.Report($"Update complete. {updated} scripts updated.");
            return updated;
        }
        catch (TaskCanceledException)
        {
            progress?.Report("Update cancelled.");
            return 0;
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Error during incremental update: {ex.Message}\r\nFalling back to full refresh.", "Update Error");
            await RefreshScriptsAsync(progress, token);
            return 0;
        }
    }
}