using System.Collections.Generic;
using System.Threading.Tasks;

namespace Skua.Core.Interfaces;

public interface IDiscordWebhookService
{
    void Initialize();
    Task SendMessageAsync(string message);
    Task SendScreenshotAsync(string message);
    Task SendEmbedAsync(string title, string description, int color, List<object>? fields = null);
    Task TestWebhookAsync();
    bool SuppressDefaultNotifications { get; set; }
}
