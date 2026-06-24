using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.Core.ViewModels.Manager;

public partial class ScriptUpdaterViewModel : BotControlViewModelBase
{
    public ScriptUpdaterViewModel(IGetScriptsService scriptsService)
        : base("Scripts")
    {
        _scriptsService = scriptsService;
        _progress = new Progress<string>(p => ProgressStatus = p);
        
        StrongReferenceMessenger.Default.Register<ScriptUpdaterViewModel, UpdateScriptsMessage>(this, ReceiveUpdateScriptsMessage);
    }

    private readonly IGetScriptsService _scriptsService;
    private readonly IProgress<string> _progress;

    [ObservableProperty]
    private string _status = "Ready to update scripts.";

    [ObservableProperty]
    private string? _progressStatus = null;

    [ObservableProperty]
    private bool _isBusy;

    protected override void OnDeactivated()
    {
        StrongReferenceMessenger.Default.UnregisterAll(this);
        base.OnDeactivated();
    }

    private async void ReceiveUpdateScriptsMessage(ScriptUpdaterViewModel recipient, UpdateScriptsMessage message)
    {
        if (message.Reset)
        {
            await recipient.ResetScripts(default);
            return;
        }

        await recipient.UpdateScripts(default);
    }

    [RelayCommand]
    public async Task ResetScripts(CancellationToken token)
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "Resetting scripts...";
        try
        {
            string skuaPath = ClientFileSources.SkuaScriptsDIR;
            
            // Defensive deletion: retry to overcome transient OS file locks or dangling file handles
            int retries = 5;
            while (retries > 0)
            {
                try
                {
                    if (Directory.Exists(skuaPath))
                        Directory.Delete(skuaPath, true);
                    break; // Success
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    retries--;
                    if (retries == 0) throw;
                    
                    // Force GC to clean up any dangling file streams from the script parser
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    await Task.Delay(300, token);
                }
            }

            if (!Directory.Exists(skuaPath))
                Directory.CreateDirectory(skuaPath);

            if (File.Exists(ClientFileSources.SkuaScriptsCommitFile))
                File.Delete(ClientFileSources.SkuaScriptsCommitFile);

            await UpdateScriptsCore(token);
        }
        catch (Exception ex)
        {
            Status = $"Error resetting scripts: {ex.Message}";
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task UpdateScripts(CancellationToken token)
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "Updating scripts...";
        try
        {
            await UpdateScriptsCore(token);
        }
        catch (Exception ex)
        {
            Status = $"Error updating scripts: {ex.Message}";
            IsBusy = false;
        }
    }

    private async Task UpdateScriptsCore(CancellationToken token)
    {
        try
        {
            await _scriptsService.RefreshScriptsAsync(_progress, token);

            int count = await Task.Run(async () => await _scriptsService.DownloadAllWhereAsync(s => !s.Downloaded || s.Outdated));
            ProgressStatus = $"Downloaded {count} scripts.";
            Status = "Scripts are up to date.";
        }
        catch (OperationCanceledException)
        {
            ProgressStatus = "Task cancelled.";
            Status = "Update cancelled.";
        }
        catch (Exception ex)
        {
            ProgressStatus = "Error occurred.";
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            await Task.Delay(1000);
            IsBusy = false;
            ProgressStatus = null;
        }
    }
}
