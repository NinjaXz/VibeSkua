using CommunityToolkit.Mvvm.Input;
using Skua.Core.Utils;
using System.Diagnostics;

namespace Skua.Core.ViewModels;

public class AboutViewModel : BotControlViewModelBase
{
    private string _markDownContent = "Loading content...";

    public AboutViewModel() : base("About")
    {
        _markDownContent = string.Empty;

        Task.Run(async () => await GetAboutContent());

        NavigateCommand = new RelayCommand<string>(NavigateToUrl);
    }

    public string MarkdownDoc
    {
        get => _markDownContent; set => SetProperty(ref _markDownContent, value);
    }

    public IRelayCommand NavigateCommand { get; }

    private void NavigateToUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return;

        try
        {
            if (url.StartsWith("./"))
            {
                Process.Start(new ProcessStartInfo($"https://github.com/auqw/Skua/blob/master/{url.Substring(2)}") { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        catch
        {
            /* ignored */
        }
    }

    private async Task GetAboutContent()
    {
        MarkdownDoc = @"# VibeSkua
> **Note:** This project is **Vibe Coded**—built entirely through AI-assisted development, and pure momentum.

A feature-rich, high-performance derivative of auqw/skua, engineered for advanced automation, stability, and streamlined multi-client management.

### Key Features
* **Discord Integration:** Native support for automated alerts, rare drops, and live-pings.
* **Headless Mode:** Hidden 1x1 pixel viewport for drastically reduced CPU/GPU usage per instance.
* **Autonomous Scheduling:** Built-in queue to schedule scripts at specific dates and times.
* **Unified UI:** Embedded WPF interface with tabbed, multi-client management.
* **Engine Optimizations:** SWF Memory Caching, background connection stability, and automatic memory trimming for long sessions.

### Disclaimer
**Educational & Personal Use Only.** This project is provided ""as-is"" under the MIT License. Use of this software may violate the Terms of Service of the associated game. By using this tool, you acknowledge that you do so entirely at your own risk.";
        await Task.CompletedTask;
    }
}