using Skua.Core.Interfaces;
using Skua.Core.Models.GitHub;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Skua.Core.Services;

public static class ScriptStartupUpdater
{
    /// <summary>
    /// Downloads every missing or outdated script and tells the user if some could not be downloaded.
    /// </summary>
    public static async Task DownloadMissingAndOutdatedAsync(IGetScriptsService scripts, IDialogService dialogService)
    {
        Func<ScriptInfo, bool> needsDownload = s => !s.Downloaded || s.Outdated;
        int expected = scripts.Scripts.Count(needsDownload);
        int downloaded = await scripts.DownloadAllWhereAsync(needsDownload);

        if (downloaded < expected)
        {
            dialogService.ShowMessageBox(
                $"Downloaded {downloaded} of {expected} scripts.\r\n" +
                "Some scripts could not be downloaded, so scripts that use them may fail to compile.\r\n" +
                "Open Script Repo and press Download All to retry.",
                "Script Update");
        }
    }
}
