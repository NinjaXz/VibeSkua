using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System;
using Skua.Core.Models;

namespace Skua.Core.ViewModels;

public partial class ScriptSchedulerViewModel : BotControlViewModelBase
{
    private readonly IScriptManager _manager;
    private readonly IFileDialogService _fileDialog;
    private readonly IDiscordWebhookService _discord;
    private readonly ISettingsService _settingsService;
    private readonly IWindowService _windowService;
    private readonly IDialogService _dialogService;
    private Stopwatch _scriptStopwatch = new();
    private System.Threading.CancellationTokenSource? _queueCts;
    
    public ScriptSchedulerViewModel(IScriptManager manager, IFileDialogService fileDialog, IDiscordWebhookService discord, ISettingsService settingsService, IWindowService windowService, IDialogService dialogService) : base("Scheduler")
    {
        _manager = manager;
        _fileDialog = fileDialog;
        _discord = discord;
        _settingsService = settingsService;
        _windowService = windowService;
        _dialogService = dialogService;
        
        StrongReferenceMessenger.Default.Register<ScriptSchedulerViewModel, ScriptStoppedMessage, int>(this, (int)MessageChannels.ScriptStatus, (r, m) => r.OnScriptStopped());
        StrongReferenceMessenger.Default.Register<ScriptSchedulerViewModel, QueueScriptMessage, int>(this, (int)MessageChannels.ScriptStatus, (r, m) => r.OnQueueScript(m));
    }

    private void OnQueueScript(QueueScriptMessage message)
    {
        if (IsRunningQueue)
        {
            _dialogService.ShowMessageBox("Cannot add scripts while the playlist is running.", "Scheduler Running");
            return;
        }

        if (!string.IsNullOrEmpty(message.Path))
        {
            var item = new ScriptItemViewModel(message.Path);
            int count = ScriptQueue.Count(x => x.Path == message.Path);
            if (count > 0)
                item.Name = $"{Path.GetFileNameWithoutExtension(message.Path)} ({count + 1}){Path.GetExtension(message.Path)}";
            
            ScriptQueue.Add(item);
        }
    }

    [ObservableProperty]
    private ObservableCollection<ScriptItemViewModel> _scriptQueue = new();

    [ObservableProperty]
    private bool _isRunningQueue;

    [ObservableProperty]
    private int _currentIndex = 0;

    [RelayCommand]
    private void OpenScriptRepo()
    {
        _windowService.ShowManagedWindow("Script Repo");
    }

    [RelayCommand]
    private void RemoveScript(ScriptItemViewModel item)
    {
        if (IsRunningQueue)
        {
            _dialogService.ShowMessageBox("Cannot remove scripts while the playlist is running.", "Scheduler Running");
            return;
        }

        if (ScriptQueue.Contains(item))
            ScriptQueue.Remove(item);
    }

    [RelayCommand]
    private async Task EditScriptConfig(ScriptItemViewModel item)
    {
        if (_manager.ScriptRunning)
        {
            _dialogService.ShowMessageBox("Script currently running. Stop the script to change its options.", "Script Running");
            return;
        }

        try
        {
            object compiled = await Task.Run(() => _manager.Compile(File.ReadAllText(item.Path))!);
            _manager.OverrideStorage = item.Storage;
            _manager.LoadScriptConfig(compiled);
            if (_manager.Config!.Options.Count > 0 || _manager.Config.MultipleOptions.Count > 0)
                _manager.Config.Configure();
            else
                _dialogService.ShowMessageBox("The loaded script has no options to configure.", "No Options");
            
            _manager.OverrideStorage = null;
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Script cannot be configured as it has compilation errors:\r\n{ex}", "Script Error");
        }
    }

    public class ArmySchedulerMessage
    {
        public System.Collections.Generic.IEnumerable<ScriptItemViewModel> Queue { get; }
        public bool Handled { get; set; } = false;
        public ArmySchedulerMessage(System.Collections.Generic.IEnumerable<ScriptItemViewModel> queue) { Queue = queue; }
    }

    public class ArmySchedulerStopMessage
    {
        public bool Handled { get; set; } = false;
    }

    [RelayCommand]
    private void StartQueue()
    {
        if (ScriptQueue.Count == 0 || IsRunningQueue) return;
        
        var msg = new ArmySchedulerMessage(ScriptQueue);
        StrongReferenceMessenger.Default.Send(msg);
        if (msg.Handled) return;

        IsRunningQueue = true;
        _queueCts = new System.Threading.CancellationTokenSource();
        _discord.SuppressDefaultNotifications = true;
        CurrentIndex = 0;
        
        foreach (var item in ScriptQueue)
            item.Status = "Queued";
            
        RunNextScript();
    }

    [RelayCommand]
    private async Task StopQueue()
    {
        var msg = new ArmySchedulerStopMessage();
        StrongReferenceMessenger.Default.Send(msg);
        if (msg.Handled) return;

        IsRunningQueue = false;
        _queueCts?.Cancel();
        _discord.SuppressDefaultNotifications = false;
        if (CurrentIndex < ScriptQueue.Count)
        {
            ScriptQueue[CurrentIndex].Status = "Stopped";
            ScriptQueue[CurrentIndex].Duration = GetFormattedDuration();
        }
        await _manager.StopScript();
        _ = _discord.SendEmbedAsync("Scheduler Stopped", "**Scheduler** playlist execution manually halted.", 0xFF0000);
    }

    public class SavedScriptItem
    {
        public string Path { get; set; } = string.Empty;
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    [RelayCommand]
    private void SavePlaylist()
    {
        if (ScriptQueue.Count == 0)
        {
            _dialogService.ShowMessageBox("The playlist is empty.", "Save Playlist");
            return;
        }

        string? path = _fileDialog.Save("JSON Files (*.json)|*.json|All files (*.*)|*.*");
        if (path != null)
        {
            var data = ScriptQueue.Select(x => new SavedScriptItem { Path = x.Path, Id = x.Id, Name = x.Name }).ToList();
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            _dialogService.ShowMessageBox("Playlist saved successfully.", "Save Playlist");
        }
    }

    [RelayCommand]
    private void LoadPlaylist()
    {
        if (IsRunningQueue)
        {
            _dialogService.ShowMessageBox("Cannot load a playlist while the queue is running.", "Scheduler Running");
            return;
        }

        string? path = _fileDialog.OpenFile("JSON Files (*.json)|*.json|All files (*.*)|*.*");
        if (path != null && File.Exists(path))
        {
            try
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<SavedScriptItem>>(File.ReadAllText(path));
                if (data != null)
                {
                    ScriptQueue.Clear();
                    foreach (var item in data)
                    {
                        if (File.Exists(item.Path))
                        {
                            var vm = new ScriptItemViewModel(item.Path) { Id = item.Id };
                            if (!string.IsNullOrEmpty(item.Name))
                                vm.Name = item.Name;
                            ScriptQueue.Add(vm);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessageBox($"Failed to load playlist: {ex.Message}", "Load Error");
            }
        }
    }

    private string GetFormattedDuration()
    {
        var ts = _scriptStopwatch.Elapsed;
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private async void RunNextScript()
    {
        if (!IsRunningQueue || _queueCts?.IsCancellationRequested == true) return;

        IScriptPlayer? player = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<IScriptPlayer>();
        string botName = GetBotUsername(player);

        if (player != null && !player.LoggedIn)
        {
            _ = _discord.SendEmbedAsync("Scheduler Paused", "**Scheduler** is waiting for account to reconnect before running next script...", 0xFFD700);
            while (!player.LoggedIn && IsRunningQueue && _queueCts?.IsCancellationRequested != true)
            {
                await Task.Delay(1500);
            }
            if (!IsRunningQueue || _queueCts?.IsCancellationRequested == true)
                return;
            await Task.Delay(3000);
            botName = GetBotUsername(player);
        }

        if (CurrentIndex >= ScriptQueue.Count)
        {
            IsRunningQueue = false;
            _discord.SuppressDefaultNotifications = false;
            _ = _discord.SendEmbedAsync("Scheduler Finished", "All scripts in the **Scheduler** playlist have completed!", 0x00FF00);
            return;
        }

        var nextScript = ScriptQueue[CurrentIndex];
        
        if (File.Exists(nextScript.Path))
        {
            if (_manager.ScriptRunning)
            {
                string currentLoaded = _manager.LoadedScript ?? string.Empty;
                bool isSameScript = false;
                try
                {
                    if (!string.IsNullOrEmpty(currentLoaded))
                    {
                        string fullLoaded = Path.GetFullPath(currentLoaded);
                        string fullNext = Path.GetFullPath(nextScript.Path);
                        isSameScript = string.Equals(fullLoaded, fullNext, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    isSameScript = string.Equals(currentLoaded, nextScript.Path, StringComparison.OrdinalIgnoreCase);
                }

                if (isSameScript)
                {
                    nextScript.Status = "Running";
                    _scriptStopwatch.Restart();
                    _ = _discord.SendEmbedAsync($"Script Resumed [{CurrentIndex + 1}/{ScriptQueue.Count}]", $"**{botName}** has resumed execution of **{nextScript.Name}** after reconnection.", 0x00FF00);
                    return;
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine($"Stopping lingering script ({currentLoaded}) before launching scheduled script ({nextScript.Name})...");
                    try { await _manager.StopScript(); } catch { }
                    await Task.Delay(1500);
                }
            }

            nextScript.Status = "Running";
            _scriptStopwatch.Restart();
            
            _ = _discord.SendEmbedAsync($"Script Started [{CurrentIndex + 1}/{ScriptQueue.Count}]", $"**{botName}** has begun execution of **{nextScript.Name}**.", 0x00FF00);
            
            try
            {
                _manager.OverrideStorage = nextScript.Storage;
                _manager.SetLoadedScript(nextScript.Path);
                _manager.SilentConfig = true;
                Exception? startEx;
                try { startEx = await _manager.StartScript(); }
                finally { _manager.SilentConfig = false; }

                if (startEx != null)
                {
                    _manager.OverrideStorage = null;
                    nextScript.Status = "Failed";
                    nextScript.Duration = GetFormattedDuration();
                    var actualStartEx = startEx is System.Reflection.TargetInvocationException && startEx.InnerException != null ? startEx.InnerException : startEx;
                    System.Diagnostics.Trace.WriteLine($"Scheduler script failed to start: {actualStartEx.Message}");
                    var fields = new List<object> { new { name = "Error", value = $"```{actualStartEx.Message}```", inline = false } };
                    _ = _discord.SendEmbedAsync($"Script Error [{CurrentIndex + 1}/{ScriptQueue.Count}]", $"**{botName}** failed to start **{nextScript.Name}**.", 0xFF0000, fields);
                    CurrentIndex++;
                    if (!IsRunningQueue || _queueCts?.IsCancellationRequested == true) return;
                    await Task.Delay(3000);
                    RunNextScript();
                }
            }
            catch (Exception ex)
            {
                _manager.OverrideStorage = null;
                nextScript.Status = "Crashed";
                nextScript.Duration = GetFormattedDuration();
                var actualEx = ex is System.Reflection.TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                System.Diagnostics.Trace.WriteLine($"Scheduler script crash: {actualEx.Message}");
                var fields = new List<object> { new { name = "Error", value = $"```{actualEx.Message}```", inline = false } };
                _ = _discord.SendEmbedAsync($"Script Error [{CurrentIndex + 1}/{ScriptQueue.Count}]", $"**{botName}** crashed while starting **{nextScript.Name}**.", 0xFF0000, fields);
                CurrentIndex++;
                if (!IsRunningQueue || _queueCts?.IsCancellationRequested == true) return;
                await Task.Delay(3000);
                RunNextScript();
            }
        }
        else
        {
            nextScript.Status = "File Not Found";
            CurrentIndex++;
            if (!IsRunningQueue || _queueCts?.IsCancellationRequested == true) return;
            await Task.Delay(1500);
            RunNextScript();
        }
    }

    private void OnScriptStopped()
    {
        _manager.OverrideStorage = null;

        if (!IsRunningQueue || _queueCts?.IsCancellationRequested == true) return;

        if (CurrentIndex < ScriptQueue.Count)
        {
            _scriptStopwatch.Stop();
            IScriptPlayer? player = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<IScriptPlayer>();
            string botName = GetBotUsername(player);

            if (player != null && !player.LoggedIn)
            {
                ScriptQueue[CurrentIndex].Status = "Disconnected";
            }
            else
            {
                ScriptQueue[CurrentIndex].Status = "Completed";
                ScriptQueue[CurrentIndex].Duration = GetFormattedDuration();
                
                var fields = new List<object>
                {
                    new { name = "Duration", value = ScriptQueue[CurrentIndex].Duration, inline = true }
                };
                _ = _discord.SendEmbedAsync($"Farming Session Concluded [{CurrentIndex + 1}/{ScriptQueue.Count}]", $"**{botName}** has finished execution of **{ScriptQueue[CurrentIndex].Name}**.", 0xFF0000, fields);

                CurrentIndex++;
            }
        }

        Task.Run(async () => 
        {
            try
            {
                await Task.Delay(2000, _queueCts!.Token);
                RunNextScript();
            }
            catch (TaskCanceledException) { }
        });
    }

    private string GetBotUsername(IScriptPlayer? player)
    {
        if (player != null && !string.IsNullOrWhiteSpace(player.Username) && player.Username != "loginInfo.strUsername")
            return player.Username;
        try
        {
            var servers = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<IScriptServers>();
            if (servers != null && !string.IsNullOrWhiteSpace(servers.CachedUsername))
                return servers.CachedUsername!;
        }
        catch { }
        return "VibeSkua Bot";
    }
}
