using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;

namespace Skua.Core.Services;

public class DiscordWebhookService : IDiscordWebhookService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IScreenshotService _screenshotService;
    private readonly IServiceProvider _serviceProvider;
    private readonly HttpClient _httpClient;
    private System.Threading.Timer? _pingTimer;
    private bool _initialized;
    private DateTime? _scriptStartTime;
    public bool SuppressDefaultNotifications { get; set; }

    // Queue system
    private readonly ConcurrentQueue<Func<Task>> _messageQueue = new();
    private readonly SemaphoreSlim _queueSemaphore = new(0);
    private readonly CancellationTokenSource _cts = new();

    // Snapshot state
    private int _startGold;
    private int _startExp;
    private int _startLevel;

    public DiscordWebhookService(ISettingsService settingsService, IScreenshotService screenshotService, IServiceProvider serviceProvider)
    {
        _settingsService = settingsService;
        _screenshotService = screenshotService;
        _serviceProvider = serviceProvider;
        _httpClient = new HttpClient();
        
        // Start background queue processor
        _ = ProcessQueueAsync();
    }

    public void Initialize()
    {
        if (_initialized) return;
        
        StrongReferenceMessenger.Default.Register<DiscordWebhookService, ScriptStartedMessage, int>(this, (int)MessageChannels.ScriptStatus, (r, m) => r.OnScriptStarted(m));
        StrongReferenceMessenger.Default.Register<DiscordWebhookService, ScriptStoppedMessage, int>(this, (int)MessageChannels.ScriptStatus, (r, m) => r.OnScriptStopped(m));
        StrongReferenceMessenger.Default.Register<DiscordWebhookService, ScriptErrorMessage, int>(this, (int)MessageChannels.ScriptStatus, (r, m) => r.OnScriptError(m));
        StrongReferenceMessenger.Default.Register<DiscordWebhookService, ReloginTriggeredMessage, int>(this, (int)MessageChannels.GameEvents, (r, m) => r.OnRelogin(m));
        StrongReferenceMessenger.Default.Register<DiscordWebhookService, ItemDroppedMessage, int>(this, (int)MessageChannels.GameEvents, (r, m) => r.OnItemDropped(m));
        
        _initialized = true;
    }

    private async Task ProcessQueueAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            await _queueSemaphore.WaitAsync(_cts.Token);
            if (_messageQueue.TryDequeue(out var action))
            {
                try
                {
                    await action();
                    // Rate limit: Max 2 messages per second
                    await Task.Delay(500, _cts.Token);
                }
                catch { }
            }
        }
    }

    private void EnqueueAction(Func<Task> action)
    {
        _messageQueue.Enqueue(action);
        _queueSemaphore.Release();
    }

    private string GetBotName()
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot != null && bot.Player != null && bot.Player.Playing && !string.IsNullOrWhiteSpace(bot.Player.Username))
        {
            return bot.Player.Username;
        }
        return "VibeSkua Bot";
    }

    private void OnScriptStarted(ScriptStartedMessage msg)
    {
        _scriptStartTime = DateTime.Now;
        var bot = _serviceProvider.GetService<IScriptInterface>();
        
        if (bot != null && bot.Player.Playing)
        {
            _startGold = bot.Player.Gold;
            _startExp = bot.Player.XP;
            _startLevel = bot.Player.Level;
        }
        else
        {
            _startGold = 0;
            _startExp = 0;
            _startLevel = 0;
        }

        string scriptName = "a script";
        if (bot != null && !string.IsNullOrWhiteSpace(bot.Manager?.LoadedScript))
        {
            scriptName = System.IO.Path.GetFileNameWithoutExtension(bot.Manager.LoadedScript);
        }

        var settings = _settingsService.GetClient();
        if (settings != null && settings.WebhookNotifyStarted && !SuppressDefaultNotifications)
        {
            EnqueueAction(async () => await SendEmbedAsync("Script Started", $"**{GetBotName()}** has begun execution of **{scriptName}**.", 0x00FF00)); // Green
        }
            
        StartPingTimer();
    }

    private void OnScriptStopped(ScriptStoppedMessage msg)
    {
        var settings = _settingsService.GetClient();
        if (settings == null || !settings.WebhookNotifyStopped || SuppressDefaultNotifications)
        {
            _scriptStartTime = null;
            StopPingTimer();
            return;
        }

        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot != null && bot.Player.Playing)
        {
            if (_scriptStartTime.HasValue)
            {
                var timeElapsed = DateTime.Now - _scriptStartTime.Value;
                int goldEarned = bot.Player.Gold - _startGold;
                int expEarned = bot.Player.XP - _startExp;

                string scriptName = "a script";
                if (bot.Manager.LoadedScript != null)
                {
                    scriptName = System.IO.Path.GetFileNameWithoutExtension(bot.Manager.LoadedScript);
                }

                string botName = GetBotName();

                var fields = new List<object>();
                
                if (goldEarned != 0) fields.Add(new { name = "Gold Earned", value = goldEarned > 0 ? $"{goldEarned:N0}" : "0", inline = true });
                if (expEarned != 0) fields.Add(new { name = "XP Earned", value = expEarned > 0 ? $"{expEarned:N0}" : "0", inline = true });
                fields.Add(new { name = "Time Elapsed", value = $"{timeElapsed.Hours:D2}:{timeElapsed.Minutes:D2}:{timeElapsed.Seconds:D2}", inline = false });

                EnqueueAction(async () => await SendEmbedAsync("Farming Session Concluded", $"**{botName}** has stopped execution of **{scriptName}**.", 0xFF0000, fields));
            }
        }

        _scriptStartTime = null;
        StopPingTimer();
    }

    private void OnScriptError(ScriptErrorMessage msg)
    {
        var settings = _settingsService.GetClient();
        if (settings != null && settings.WebhookNotifyCrashed)
        {
            var fields = new List<object>
            {
                new { name = "Exception", value = $"```{msg.Exception.Message}```", inline = false }
            };
            EnqueueAction(async () => await SendEmbedAsync("Script Error", $"**{GetBotName()}** crashed unexpectedly.", 0xFF0000, fields)); // Red
        }
            
        StopPingTimer();
    }

    private void OnRelogin(ReloginTriggeredMessage msg)
    {
        var settings = _settingsService.GetClient();
        if (settings != null && settings.WebhookNotifyRelogged)
        {
            string title = msg.WasKicked ? "Kicked & Relogging" : "Disconnected & Relogging";
            EnqueueAction(async () => await SendEmbedAsync(title, $"**{GetBotName()}** is attempting to reconnect.", 0xFFA500)); // Orange
        }
    }

    private void StartPingTimer()
    {
        var settings = _settingsService.GetClient();
        if (settings == null || settings.WebhookPingInterval <= 0) return;
        
        _pingTimer?.Dispose();
        var intervalMs = (int)TimeSpan.FromMinutes(settings.WebhookPingInterval).TotalMilliseconds;
        _pingTimer = new System.Threading.Timer(OnPingTimerElapsed, null, intervalMs, intervalMs);
    }

    private void StopPingTimer()
    {
        _pingTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnPingTimerElapsed(object? state)
    {
        var settings = _settingsService.GetClient();
        if (settings == null || settings.WebhookPingInterval <= 0)
        {
            StopPingTimer();
            return;
        }
        
        EnqueueAction(async () => await SendEmbedAsync("Health Ping", $"**{GetBotName()}** is running flawlessly.", 0x808080)); // Gray
    }

    public async Task TestWebhookAsync()
    {
        EnqueueAction(async () => await SendEmbedAsync("Webhook Test Successful!", "Your VibeSkua Discord Webhooks are perfectly configured.", 0x00FF00));
    }

    public async Task SendMessageAsync(string message)
    {
        EnqueueAction(async () => await PostWebhookAsync(new { content = message }));
    }

    public async Task SendScreenshotAsync(string message)
    {
        EnqueueAction(async () => 
        {
            try
            {
                var url = _settingsService.GetClient()?.DiscordWebhookUrl;
                if (string.IsNullOrWhiteSpace(url)) return;

                byte[] screenshotBytes = await _screenshotService.TakeScreenshotAsync();
                using var formData = new MultipartFormDataContent();
                
                var embed = new
                {
                    description = message,
                    color = 0xFFD700, // Gold
                    image = new { url = "attachment://screenshot.png" }
                };
                
                var payloadJson = JsonSerializer.Serialize(new { embeds = new[] { embed } });
                formData.Add(new StringContent(payloadJson), "payload_json");

                if (screenshotBytes != null && screenshotBytes.Length > 0)
                {
                    var imageContent = new ByteArrayContent(screenshotBytes);
                    imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    formData.Add(imageContent, "file", "screenshot.png");
                }

                await _httpClient.PostAsync(url, formData);
            }
            catch { }
        });
    }

    private async Task SendEmbedAsync(string title, string description, int color, List<object>? fields = null)
    {
        var embed = new
        {
            title = title,
            description = description,
            color = color,
            fields = fields?.ToArray()
        };

        var payload = new { embeds = new[] { embed } };
        await PostWebhookAsync(payload);
    }

    private async Task PostWebhookAsync(object payload)
    {
        try
        {
            var url = _settingsService.GetClient()?.DiscordWebhookUrl;
            if (string.IsNullOrWhiteSpace(url)) return;

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(url, content);
        }
        catch { }
    }

    private void OnItemDropped(ItemDroppedMessage msg)
    {
        var settings = _settingsService.GetClient();
        if (settings == null || !settings.WebhookNotifyItemDrops) return;

        var dropList = settings.WebhookNotifyItemDropsList?.ToLower().Split(',') ?? Array.Empty<string>();
        foreach (var item in dropList)
        {
            var trimmed = item.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && msg.Item.Name.ToLower().Contains(trimmed))
            {
                string wikiUrl = "https://aqwwiki.wikidot.com/" + Uri.EscapeDataString(msg.Item.Name);
                _ = SendScreenshotAsync($"🏆 **Rare Drop!** - **{GetBotName()}** just got **[{msg.Item.Name}]({wikiUrl})** x{msg.Item.Quantity}!");
                break;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _pingTimer?.Dispose();
        _queueSemaphore.Dispose();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
