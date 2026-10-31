using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.GitHub;
using Skua.Core.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

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
            progress?.Report("Loading scripts...");
            List<ScriptInfo> scripts = await Task.Run(() => GetScriptsInfo(refresh, token), token).ConfigureAwait(false);

            progress?.Report($"Found {scripts.Count} scripts.");
            
            _scripts.ReplaceRange(scripts);
            progress?.Report($"Loaded {scripts.Count} scripts.");
            OnPropertyChanged(nameof(Scripts));
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

    private Dictionary<string, DateTime>? _cachedDates;

    private async Task<List<ScriptInfo>> GetScriptsInfo(bool refresh, CancellationToken token)
    {
        if (_scripts.Count != 0 && !refresh)
            return _scripts.ToList();

        string cacheFile = ClientFileSources.SkuaScriptsCacheFile;
        List<ScriptInfo>? scripts = null;

        // 1. Fast path: Always read from local disk cache immediately if available and not explicitly refreshing
        if (!refresh && File.Exists(cacheFile))
        {
            try
            {
                string cachedJson = await File.ReadAllTextAsync(cacheFile, token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(cachedJson))
                {
                    scripts = JsonConvert.DeserializeObject<List<ScriptInfo>>(cachedJson);
                }
            }
            catch { }
        }

        // 2. Fetch from GitHub if no cache exists or explicit refresh requested
        if (scripts == null || !scripts.Any())
        {
            try
            {
                using HttpResponseMessage response = await ValidatedHttpExtensions.GetAsync(HttpClients.GitHubRaw, _rawScriptsJsonUrl, token).ConfigureAwait(false);
                string content = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(content))
                    throw new InvalidDataException("scripts.json is empty or null");

                scripts = JsonConvert.DeserializeObject<List<ScriptInfo>>(content);
                if (scripts == null || !scripts.Any())
                    throw new InvalidDataException("scripts.json contains no valid scripts");

                // Save cache to disk
                try
                {
                    string dir = Path.GetDirectoryName(cacheFile)!;
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    await File.WriteAllTextAsync(cacheFile, content, token).ConfigureAwait(false);
                }
                catch { }
            }
            catch when (!refresh && File.Exists(cacheFile))
            {
                // Gracefully fallback to existing cache if offline or GitHub request fails during background refresh
                try
                {
                    string cachedJson = await File.ReadAllTextAsync(cacheFile, token).ConfigureAwait(false);
                    scripts = JsonConvert.DeserializeObject<List<ScriptInfo>>(cachedJson);
                }
                catch { }
            }
        }
        else if (!refresh && File.Exists(cacheFile) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile)) > TimeSpan.FromHours(12))
        {
            // Cache was served immediately, but is older than 12 hours: trigger background refresh silently!
            _ = Task.Run(async () =>
            {
                try
                {
                    using HttpResponseMessage response = await ValidatedHttpExtensions.GetAsync(HttpClients.GitHubRaw, _rawScriptsJsonUrl, CancellationToken.None).ConfigureAwait(false);
                    string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var freshScripts = JsonConvert.DeserializeObject<List<ScriptInfo>>(content);
                        if (freshScripts != null && freshScripts.Any())
                        {
                            string dir = Path.GetDirectoryName(cacheFile)!;
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            await File.WriteAllTextAsync(cacheFile, content).ConfigureAwait(false);
                        }
                    }
                }
                catch { }
            });
        }

        // 3. Apply creation dates from memory-cached dictionary
        try
        {
            if (_cachedDates == null)
            {
                string primaryDatesFile = ClientFileSources.SkuaScriptDatesFile;
                string fallbackDatesFile = Path.Combine(AppContext.BaseDirectory, "ScriptDates.json");
                string bundledFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Skua.Core", "ScriptDates.json");

                string fileToRead = File.Exists(primaryDatesFile)
                    ? primaryDatesFile
                    : File.Exists(fallbackDatesFile)
                        ? fallbackDatesFile
                        : File.Exists(bundledFile) ? bundledFile : string.Empty;

                if (!string.IsNullOrEmpty(fileToRead))
                {
                    string datesJson = await File.ReadAllTextAsync(fileToRead, token).ConfigureAwait(false);
                    var rawDict = JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(datesJson);
                    if (rawDict != null)
                    {
                        _cachedDates = new Dictionary<string, DateTime>(rawDict, StringComparer.OrdinalIgnoreCase);

                        if (!File.Exists(primaryDatesFile))
                        {
                            try
                            {
                                string dir = Path.GetDirectoryName(primaryDatesFile)!;
                                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                                File.Copy(fileToRead, primaryDatesFile, overwrite: true);
                            }
                            catch { }
                        }
                    }
                }
            }

            if (_cachedDates != null && scripts != null)
            {
                foreach (var s in scripts)
                {
                    string fwd = s.FilePath.Replace('\\', '/');
                    string bck = s.FilePath.Replace('/', '\\');
                    if (_cachedDates.TryGetValue(s.FilePath, out var date) ||
                        _cachedDates.TryGetValue(fwd, out date) ||
                        _cachedDates.TryGetValue(bck, out date))
                    {
                        s.CreationDate = date;
                    }
                }
            }
        }
        catch { }

        return scripts ?? new List<ScriptInfo>();
    }

    public async Task UpdateScriptDatesAsync(IProgress<string>? progress, CancellationToken token)
    {
        string datesFile = ClientFileSources.SkuaScriptDatesFile;
        string fallbackDatesFile = Path.Combine(AppContext.BaseDirectory, "ScriptDates.json");
        Dictionary<string, DateTime> dict = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (File.Exists(datesFile))
            {
                dict = JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(await File.ReadAllTextAsync(datesFile, token)) ?? new();
            }
            else if (File.Exists(fallbackDatesFile))
            {
                dict = JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(await File.ReadAllTextAsync(fallbackDatesFile, token)) ?? new();
            }
        }
        catch { }

        int newScripts = 0;
        int processedSinceSave = 0;
        HttpClient client = HttpClients.GetGHClient();

        foreach (var script in Scripts)
        {
            if (token.IsCancellationRequested)
                break;

            if (!dict.ContainsKey(script.FilePath) || script.CreationDate == null || script.CreationDate == DateTime.MinValue)
            {
                try
                {
                    progress?.Report($"Fetching date for {script.FileName}...");
                    string requestUrl = $"https://api.github.com/repos/auqw/Scripts/commits?path={script.FilePath.Replace(" ", "%20")}";
                    using HttpResponseMessage response = await client.GetAsync(requestUrl, token);

                    if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == (HttpStatusCode)429)
                    {
                        progress?.Report("GitHub API rate limit reached. Progress saved.");
                        break;
                    }

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
                            processedSinceSave++;

                            if (processedSinceSave >= 10)
                            {
                                try
                                {
                                    string dir = Path.GetDirectoryName(datesFile)!;
                                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                                    await File.WriteAllTextAsync(datesFile, JsonConvert.SerializeObject(dict, Formatting.Indented), CancellationToken.None);
                                    processedSinceSave = 0;
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { }
            }
            else
            {
                script.CreationDate = dict[script.FilePath];
            }
        }

        if (newScripts > 0 || processedSinceSave > 0)
        {
            try
            {
                string dir = Path.GetDirectoryName(datesFile)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(datesFile, JsonConvert.SerializeObject(dict, Formatting.Indented), CancellationToken.None);
            }
            catch { }

            if (token.IsCancellationRequested)
                progress?.Report($"Cancelled. Updated {newScripts} script dates.");
            else
                progress?.Report($"Updated {newScripts} script dates.");
            OnPropertyChanged(nameof(Scripts));
        }
        else
        {
            if (token.IsCancellationRequested)
                progress?.Report("Update dates cancelled.");
            else
                progress?.Report("All script dates are already up to date.");
        }
    }

    public async Task DownloadScriptAsync(ScriptInfo info, CancellationToken token = default)
    {
        string scriptsRoot = Path.GetFullPath(ClientFileSources.SkuaScriptsDIR) + Path.DirectorySeparatorChar;
        if (!info.LocalFile.StartsWith(scriptsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Refusing to download '{info.FilePath}': it resolves outside the scripts folder.");
        if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out Uri? downloadUri)
            || downloadUri.Scheme != Uri.UriSchemeHttps
            || !downloadUri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Refusing to download '{info.FilePath}': its URL is not on raw.githubusercontent.com.");

        string? localDir = Path.GetDirectoryName(info.LocalFile);
        if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
            Directory.CreateDirectory(localDir);

        using HttpResponseMessage response = await ValidatedHttpExtensions.GetAsync(HttpClients.GitHubRaw, info.DownloadUrl, token);
        byte[] scriptBytes = await response.Content.ReadAsByteArrayAsync(token);

        // Write beside the target and move it in, so a script being started never reads a half-written file.
        string temporaryFile = $"{info.LocalFile}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryFile, scriptBytes, token);
            File.Move(temporaryFile, info.LocalFile, true);
        }
        finally
        {
            try { File.Delete(temporaryFile); } catch { }
        }

        info.CachedDownloaded = true;
        info.CachedLocalSize = scriptBytes.Length;
    }

    public async Task<int> DownloadAllWhereAsync(
        Func<ScriptInfo, bool> pred,
        IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        List<ScriptInfo> toUpdate = _scripts.Where(pred).ToList();
        if (toUpdate.Count == 0)
            return 0;

        int total = toUpdate.Count;
        int completed = 0;
        int failed = 0;

        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = token
        };

        try
        {
            await Parallel.ForEachAsync(toUpdate, parallelOptions, async (s, ct) =>
            {
                try
                {
                    await DownloadScriptAsync(s, ct);
                    int current = Interlocked.Increment(ref completed);
                    progress?.Report($"Downloading ({current}/{total}): {s.Name}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    progress?.Report($"Failed to download {s.Name}: {ex.Message}");
                }
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        return completed;
    }

    public async Task DeleteScriptAsync(ScriptInfo info)
    {
        await Task.Run(() =>
        {
            try
            {
                if (File.Exists(info.LocalFile))
                    File.Delete(info.LocalFile);
                info.CachedDownloaded = false;
                info.CachedLocalSize = 0;
            }
            catch { }
        });
    }

    public async Task<long> CheckAdvanceSkillSetsUpdates()
    {
        try
        {
            string remoteContent = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GitHubRaw, _skillsSetsRawUrl);
            if (!File.Exists(ClientFileSources.SkuaAdvancedSkillsFile))
                return remoteContent.Length;

            string localContent = await File.ReadAllTextAsync(ClientFileSources.SkuaAdvancedSkillsFile).ConfigureAwait(false);
            return string.Equals(localContent, remoteContent, StringComparison.Ordinal) ? 0 : remoteContent.Length;
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
            string? directory = Path.GetDirectoryName(ClientFileSources.SkuaAdvancedSkillsFile);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string destination = ClientFileSources.SkuaAdvancedSkillsFile;
            if (File.Exists(destination))
            {
                string existingContent = await File.ReadAllTextAsync(destination).ConfigureAwait(false);
                if (string.Equals(existingContent, content, StringComparison.Ordinal))
                    return true;
            }

            string temporaryFile = destination + ".tmp";
            await File.WriteAllTextAsync(temporaryFile, content).ConfigureAwait(false);
            File.Move(temporaryFile, destination, true);
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
            string scriptsQuestFile = Path.Combine(ClientFileSources.SkuaScriptsDIR, "QuestData.json");
            if (!Directory.Exists(ClientFileSources.SkuaScriptsDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaScriptsDIR);

            if (File.Exists(ClientFileSources.SkuaQuestsFile))
            {
                string localContent = await File.ReadAllTextAsync(ClientFileSources.SkuaQuestsFile);
                List<Skua.Core.Models.Quests.QuestData> localQuests = JsonConvert.DeserializeObject<List<Skua.Core.Models.Quests.QuestData>>(localContent) ?? new();
                List<Skua.Core.Models.Quests.QuestData> remoteQuests = JsonConvert.DeserializeObject<List<Skua.Core.Models.Quests.QuestData>>(content) ?? new();
                
                HashSet<int> remoteIds = remoteQuests.Select(q => q.ID).ToHashSet();
                remoteQuests.AddRange(localQuests.Where(q => !remoteIds.Contains(q.ID)));
                
                string serialized = JsonConvert.SerializeObject(remoteQuests.OrderBy(q => q.ID), Formatting.Indented);
                await File.WriteAllTextAsync(ClientFileSources.SkuaQuestsFile, serialized);
                try { await File.WriteAllTextAsync(scriptsQuestFile, serialized); } catch { }
            }
            else
            {
                await File.WriteAllTextAsync(ClientFileSources.SkuaQuestsFile, content);
                try { await File.WriteAllTextAsync(scriptsQuestFile, content); } catch { }
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