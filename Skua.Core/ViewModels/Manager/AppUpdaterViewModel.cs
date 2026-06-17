using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;
using System.Diagnostics;
using System;

namespace Skua.Core.ViewModels.Manager;

public partial class AppUpdaterViewModel : ObservableObject
{
    [ObservableProperty]
    private string _updateStatus = "Ready to check for updates.";

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private int _progressValue;

    private UpdateInfo? _updateInfo;

    [RelayCommand]
    private async Task CheckForUpdate()
    {
        IsChecking = true;
        UpdateStatus = "Checking for updates...";
        ProgressValue = 0;
        try
        {
            var mgr = new UpdateManager(new GithubSource("https://github.com/NinjaXz/VibeSkua", null, false));
            _updateInfo = await mgr.CheckForUpdatesAsync();

            if (_updateInfo == null)
            {
                UpdateStatus = "You are up to date!";
                UpdateAvailable = false;
            }
            else
            {
                UpdateStatus = $"Update {_updateInfo.TargetFullRelease.Version} available!";
                UpdateAvailable = true;
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Error checking updates: {ex.Message}";
            UpdateAvailable = false;
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAndInstall()
    {
        if (_updateInfo == null) return;

        IsChecking = true;
        UpdateStatus = "Downloading update...";
        try
        {
            var mgr = new UpdateManager(new GithubSource("https://github.com/NinjaXz/VibeSkua", null, false));
            
            Action<int> progressObj = (progress) => 
            {
                ProgressValue = progress;
                UpdateStatus = $"Downloading... {progress}%";
            };

            await mgr.DownloadUpdatesAsync(_updateInfo, progressObj);

            UpdateStatus = "Installing update and restarting...";
            mgr.ApplyUpdatesAndRestart(_updateInfo);
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Error updating: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
    }
}
