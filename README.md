# VibeSkua
> **Note:** This project is **Vibe Coded**—built through AI-assisted development, and pure momentum.

A feature-rich, high-performance fork of [auqw/skua](https://github.com/auqw/skua) built from V1.4.3.0, made for advanced automation, stability, and streamlined multi-client management.

## Skua Architecture Comparison
The following overview compares the systems and core features between the original `auqw/skua` repository and VibeSkua.

### Quality of Life & Features

| Feature | Original | This Fork |
| :--- | :--- | :--- |
| **Discord Integration** | Lacked native capability. | `DiscordWebhookService` integrated natively. Overhauled with a robust threaded queue structure to prevent dropped packets, featuring a fresh embed design tracking detailed session stats (Script Name, Gold/XP Earned, Time Elapsed). |
| **Headless Mode** | Full-screen rendering; high resource demand per instance. | Introduced a 1x1 hidden pixel viewport, forcing Flash to bypass geometry/blitting and significantly reducing resource consumption. |
| **Script Scheduling** | Required manual initialization and supervision with static script options. | Added autonomous script queuing with independent option profiles, custom display names, save/load playlist states, and resilient queue processing that gracefully handles broken scripts without locking the engine. |
| **Account Tabs** | Required running individual instances which clutters the screen. | Embedded `EmbeddedMainWindow.xaml` with dynamic SWF patching for a unified, tabbed WPF interface. |
| **Script Sorting** | Basic navigation options. | Expanded `ScriptRepoViewModel.cs` to support dynamic sorting by Name, Date, or script category (Ascending/Descending). |
| **Pause Functionality** | Could only fully Stop scripts, entirely losing current progression. | Built a native `Pause` feature that safely freezes the execution thread in place, letting you interact with menus and seamlessly resume later. |
| **Smart Grid View** | Required managing dozens of overlapping individual windows. | Consolidates all active accounts into a clean, clutter-free grid inside a single window to monitor a full army at once. |
| **Instance Dashboard** | Lacked a native farming statistics dashboard. | Pinned a native Side Dashboard directly to the game frame to track Kills, Drops, and Quests at a glance. |
| **Function Based Skills** | Relied on static, hardcoded skill sequences without situational awareness. | Integrated a conditional combat engine that evaluates health, cooldowns, and missing auras natively via C# before casting. |
| **Streamer Mode** | Basic privacy capabilities. | Actively scrubs character names, guild tags, room numbers, and disables chat via background asynchronous Flash injection. Fixed OBS/Discord screen share capture to prevent blank/grey screens. |
| **Auto-Relogin Resilience** | Basic relogin handling prone to freezing during network timeouts. | Redesigned with asynchronous task scheduling, dynamic alternative server selection, and fallback socket injection. |
| **Army Control** | Required managing each client independently. | Features a Centralized Playlist Orchestrator to broadcast schedules, "Load Script to All", strict login validation, and a robust IPC system ensuring synchronized execution to the exact millisecond. |
| **Loadouts Manager** | Non-existent or fully manual. | Fully automates item equipping and dynamic (Forge) enhancements natively via C#. Features a smart banking algorithm, missing gear alerts, and seamless state restoration. |
| **Custom Scripts Loader** | Only supported official repository scripts. | Natively load individual local scripts or custom script directories directly into the UI, complete with safe file deletion prompts and ghost entry cleanup. |
| **Wiki Integration** | Required manual searching on a browser. | Directly click on item requirements within the Quest UI to instantly redirect to the corresponding AQW Wiki page. |
| **Custom Hotkeys** | Relied on static, hardcoded keyboard shortcuts. | Replaced static keybinds with a dynamic `IHotKeyService` leveraging `NHotkey.Wpf`. Integrates natively with `ISettingsService` to allow full user customization of core application commands across the entire WPF interface. |

### Performance & Engine Optimizations

* **SWF Memory Caching:** Implemented `PreloadSwf()` in `FlashUtil.cs` to cache files directly in RAM, accelerating instance launches. Enforced `WMode="direct"` for hardware-accelerated rendering.
* **Optimized Memory Management:** Mitigated memory leaks in the WPF client by properly detaching from the `StrongReferenceMessenger` and correctly managing `WeakEventManager` hooks, preventing slowdowns over long sessions.
* **Fluid Asynchronous Operations:** Replaced blocking `Thread.Sleep` calls with non-blocking `await Task.Delay` across automated hunting loops, freeing up thread-pool resources and reducing UI micro-stutters.
* **Clean Task Lifecycles & UI Decoupling:** Overhauled background processes (like the `DailyTracker` and `SingleInstanceWatcher`) to respect cancellation tokens and fully decouple heavy game-state checks from the main rendering thread.
* **Network Proxy Optimization:** Refactored `CaptureProxy.cs` to utilize `Encoding.UTF8.GetBytes()` for packet conversion, minimizing latency during high-traffic sessions.
* **GitHub Script Caching Engine:** Engineered `ScriptDates.json` to store metadata and track SHA hashes. Intelligent API querying conserves rate limits and provides graceful UI fallbacks on connection failure.
* **Background Connection Stability:** Repositions inactive clients off-screen and uses a `WPF DispatcherTimer` to ping the `isLoggedIn` COM interface every 500ms, preventing OS-level socket throttling.
* **Active Memory Management:** Introduced `MemoryUtils.cs` to periodically trim the application’s working set, ensuring RAM stability during long, multi-day farming sessions.
* **Function Based Skills Architecture:** Bypassed the legacy JSON sequence parsing system entirely. Scripters can inject raw C# classes (`ISkillProvider`) directly into the combat thread at runtime. Overhauled `MasterSkillProvider.cs` with an O(1) dictionary dispatcher and an aggressive top-down priority scanner to eliminate thread-sleep lag, allowing microsecond-accurate evaluations and instant fallbacks to auto-attacks.
* **Asynchronous Flash Injection:** Built a background loop to actively override ActionScript 3 variables (e.g., `world.strMapName`) every 500ms to maintain privacy in Streamer Mode.
* **Resilient Socket Fallbacks:** Upgraded the relogin protocol to actively poll Flash XML payloads (`mcLogin.sl.iList.numChildren`) instead of static delays, using direct `ConnectIP()` as a safety fallback.
* **Release Portability:** Updated plugins like Daily Tracker with PostBuild MSBuild targets to automatically compile and bundle into the release folder during `BuildRelease.bat`.
* **Velopack Deployment Architecture:** Fully migrated the deployment infrastructure to Velopack. Enables rapid silent installations, automatic desktop shortcut provisioning, and a built-in Updater Tab within the Manager for seamless background auto-updating via the GitHub Releases API.
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
