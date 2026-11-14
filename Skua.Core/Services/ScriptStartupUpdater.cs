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
    /// <param name="reportSuccess">Also tell the user how many scripts were downloaded when all of them succeed (the game client does, the Manager stays quiet, as in Skua).</param>
    public static async Task DownloadMissingAndOutdatedAsync(IGetScriptsService scripts, IDialogService dialogService, bool reportSuccess = false)
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
        else if (reportSuccess && downloaded > 0)
        {
            dialogService.ShowMessageBox(
                $"Downloaded {downloaded} script(s).\r\n" +
                "You can disable auto script updates in Options > Application.",
                "Script Update");
        }
    }
}
