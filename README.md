# VibeSkua
> **Note:** This project is **Vibe Coded**—built entirely through AI-assisted development, and pure momentum.

A feature-rich, high-performance derivative of [auqw/skua](https://github.com/auqw/skua), engineered for advanced automation, stability, and streamlined multi-client management.

## Skua Architecture Comparison
The following overview compares the systems and core features between the original `auqw/skua` repository and this enhanced fork.

### Quality of Life & Features

| Feature | Original | This Fork |
| :--- | :--- | :--- |
| **Discord Integration** | Lacked native capability. | `DiscordWebhookService` integrated natively. Supports automated alerts for status changes, rare drops (with screenshots), and live-pings. |
| **Headless Mode** | Full-screen rendering; high CPU/GPU demand per instance. | Introduced a 1x1 hidden pixel viewport, forcing Flash to bypass geometry/blitting and significantly reducing resource consumption. |
| **Script Scheduling** | Required manual initialization and supervision. | Added `ScriptSchedulerViewModel` for autonomous script queuing via `QueueScriptMessage` at specific dates and times. |
| **UI Environment** | Spawned multiple external Win32 windows. | Embedded `EmbeddedMainWindow.xaml` with dynamic SWF patching for a unified, tabbed WPF interface. |
| **Script Sorting** | Basic navigation options. | Expanded `ScriptRepoViewModel.cs` to support dynamic sorting by Name, Date, or File Size (Ascending/Descending). |
| **Pause Functionality** | Could only fully Stop scripts, entirely losing current progression. | Built a native `Pause` feature that safely freezes the execution thread in place, letting you interact with menus and seamlessly resume later. |
| **Smart Grid View** | Required managing dozens of overlapping individual windows. | Consolidates all active accounts into a clean, clutter-free grid inside a single window to monitor a full army at once. |
| **Instance Dashboard** | Vital farming stats buried deep in external plugin menus. | Pinned a highly readable, native Side Dashboard directly to the game frame to track Kills, Drops, and Quests at a glance. |

### Performance & Engine Optimizations

* **SWF Memory Caching:** Implemented `PreloadSwf()` in `FlashUtil.cs` to cache files directly in RAM, accelerating instance launches. Enforced `WMode="direct"` for hardware-accelerated rendering.
* **Network Proxy Optimization:** Refactored `CaptureProxy.cs` to utilize `Encoding.UTF8.GetBytes()` for packet conversion, minimizing latency during high-traffic sessions.
* **GitHub Script Caching Engine:** Engineered `ScriptDates.json` to store metadata and track SHA hashes. Intelligent API querying conserves rate limits and provides graceful UI fallbacks on connection failure.
* **Background Connection Stability:** Repositions inactive clients off-screen and uses a `WPF DispatcherTimer` to ping the `isLoggedIn` COM interface every 500ms, preventing OS-level socket throttling.
* **Active Memory Management:** Introduced `MemoryUtils.cs` to periodically trim the application’s working set, ensuring RAM stability during long, multi-day farming sessions.
* And alot more that i cannot remember.

## Building the Project

There are two ways to build the project:

1. **Automated:** Navigate to the root folder and run the **BuildRelease.bat** file. Once completed, your output files will be located in a newly created **"Build"** folder within the same directory.

2. **Manual (Terminal):** Navigate to the root folder, right-click, select **"Open in Terminal"**, and run the following command:

```bash
dotnet build Skua.sln -c Release -p:WarningLevel=0 --nologo
```

### Copyright & Disclaimer

**Educational & Personal Use Only:** This project is a derivative of [auqw/skua](https://github.com/auqw/skua) and is provided "as-is" under the MIT License. I do not claim ownership of the original assets, game data, or the intellectual property of the game developers.
 
**Disclaimer:** Use of this software may violate the Terms of Service of the associated game. The author assumes no responsibility for any account actions, bans, or other consequences taken by game developers against users of this software. By using this tool, you acknowledge that you do so entirely at your own risk. If your PC decides to commit a toaster bath, that is not my problem.
