# VibeSkua
> **Note:** This project is **Vibe Coded**—built through AI-assisted development, and pure momentum.

A feature-rich, high-performance fork of [auqw/skua](https://github.com/auqw/skua) built from V1.4.3.0, made for advanced automation, stability, and streamlined multi-client management.

## Skua Architecture Comparison
The following overview compares the systems and core features between the original `auqw/skua` repository and VibeSkua.

### Quality of Life & Features
| Feature | Original | This Fork |
| :--- | :--- | :--- |
| **Discord Integration** | Lacked native capability. | `DiscordWebhookService` integrated natively. Features rich visual embed cards (`Script Started`, `Farming Session Concluded`, `Scheduler Paused`), automatic rate-limiting (`HTTP 429`) retry loops, a threaded queue structure to prevent dropped packets, and `CachedUsername` preservation so webhooks and script status alerts always display your character's real username after disconnections. |
| **Headless Mode** | Full-screen rendering; high resource demand per instance. | Introduced a 1x1 hidden pixel viewport, forcing Flash to bypass geometry/blitting and significantly reducing resource consumption. |
| **Script Scheduling** | Required manual initialization and supervision with static script options. | Added autonomous script queuing with independent option profiles, custom display names, save/load playlist states, `SilentConfig` unattended execution (`popup modal windows automatically suppressed so overnight farming never stalls`), smart auto-relogin collision protection (`resumption recognition prevents skipped items when reconnecting during playlists`), plain-English error unwrapping, and automatic paused state monitoring (`Scheduler Paused`). |
| **Account Tabs** | Required running individual instances which clutters the screen. | Embedded `EmbeddedMainWindow.xaml` with dynamic SWF patching for a unified, tabbed WPF interface. Features instantaneous multi-account closing (`asynchronous background process killing shuts down 7+ tabs in milliseconds without UI freezing`). |
| **Script Sorting** | Basic navigation options. | Expanded `ScriptRepoViewModel.cs` to support dynamic sorting by Name, Date, or script category (Ascending/Descending). |
| **Pause Functionality** | Could only fully Stop scripts, entirely losing current progression. | Built a native `Pause` feature that safely freezes the execution thread in place, letting you interact with menus and resume later. |
| **Smart Grid View** | Required managing dozens of overlapping individual windows. | Consolidates all active accounts into a clean, clutter-free grid inside a single window to monitor a full army at once. |
| **Instance Dashboard** | Lacked a native farming statistics dashboard. | Pinned a native Side Dashboard directly to the game frame to track Kills, Drops, and Quests at a glance. |
| **Function Based Skills** | Relied on static, hardcoded skill sequences without situational awareness. | Integrated a conditional combat engine (`ISkillProvider`) that evaluates health, cooldowns, and missing auras natively via C# before casting. Features a smart two-step survival priority check (`emergency heals/shields evaluated before attack weaving`), automatic high-damage boss recognition (`7-second encounter checks switch to defensive stances on heavy burst damage`), dynamic mid-script class adaptation (`instant combat routine swapping`), an active Action Bar UI Scavenger (`30 FPS and readiness checks force-clear stuck green cooldown spinners`), and adaptive routines for all end-game classes (`Void Highlord, ArchMage, Chrono Assassin, Legion Revenant, Chaos Avenger, etc.`). |
| **Streamer Mode** | Basic privacy capabilities. | Actively scrubs character names, guild tags, room numbers, and disables chat via background asynchronous Flash injection. Fixed OBS/Discord screen share capture to prevent blank/grey screens. |
| **Auto-Relogin Resilience** | Basic relogin handling prone to freezing during network timeouts. | Redesigned with asynchronous task scheduling, dynamic alternative server selection, and fallback socket injection. Upgraded with staggered multi-account batch launches (`500ms–900ms stagger paired with retry jitter to prevent server rate-limiting`), an active `"Stuck on Login"` monitor (`automatic UI reset and rescue if authentication hangs above 5 seconds`), and synchronized character data buffering (`waiting for server list readiness before joining to completely eliminate "Character Data Could Not Be Loaded" errors`). |
| **Army Control & Navigation** | Required managing each client independently. | Features a Centralized Playlist Orchestrator to broadcast schedules, "Load Script to All", strict login validation, separate Map and Cell input boxes for exact room placement (`teleport across rooms inside your current map without reloading`), exact spawn/pad targeting (`e.g. Boss, Left`), a dedicated `"Jump All to Player..."` option (`instant 1-click /goto commands across all tabs`), and a robust IPC system ensuring synchronized execution to the exact millisecond. |
| **Smart Quest Sync & Updater** | Out-of-sync local files requiring external updater scripts and full loops. | Dual-folder `QuestData.json` synchronization (`AppData\Roaming\Skua` and `Scripts` folders stay automatically synced). Built-in Smart Incremental Updater (`Rebuild`, `Update +100 Buffer`, `Range` modes) checks IDs rapidly without triggering GitHub rate limits or freezing the client, automatically discarding invalid or removed quests. |
| **Map & Room Loading Reliability** | Hardcoded short timeouts causing scripts to drop commands or freeze during black loading screens. | Aligned loop thresholds with exact timeout math (`6.0s map load and 3.0s action ceilings`). Characters wait cleanly for Flash multi-client handshakes and always jump directly to the intended destination room (`e.g. Boss, Left`) without dropping into entrance cells (`m1, Left`) or going AFK across room boundaries. Features smart spawn recovery after death and instant script stopping during respawn delays. |
| **Reorganized Navigation & UI** | Cluttered or unorganized top menu navigation. | Reorganized left-to-right based on workflow priority (`Scripts`, `Options`, `Tools & Helpers`, `Combat`, `Bank`, `Diagnostics`). Features instant batch-loading for the Daily Tracker window, categorized daily vs. weekly ultra boss lockouts, and a unified developer diagnostics workspace (`Logs, Console, Spammer, Logger, Interceptor`). |
| **Loadouts Manager** | Non-existent or fully manual. | Fully automates item equipping and dynamic (Forge) enhancements natively via C#. Features a smart banking algorithm, missing gear alerts, and state restoration. |
| **Custom Scripts Loader** | Only supported official repository scripts. | Natively load individual local scripts or custom script directories directly into the UI, complete with safe file deletion prompts and ghost entry cleanup. |
| **Wiki Integration** | Required manual searching on a browser. | Directly click on item requirements within the Quest UI to instantly redirect to the corresponding AQW Wiki page. |
| **Custom Hotkeys** | Relied on static, hardcoded keyboard shortcuts. | Replaced static keybinds with a dynamic `IHotKeyService` leveraging `NHotkey.Wpf`. Integrates natively with `ISettingsService` to allow full user customization of core application commands across the entire WPF interface. |

### Performance & Engine Optimizations

* **Combat Cooldown Deadlock & Race Elimination:** Reworked Global Cooldown (`GCD`) index checks in `AdvancedSkillCommand.cs` by verifying skill readiness (`isOK` and Flash `canUseSkill`) before advancing rotation indices, completely eliminating endless Auto-Attack loops (`Only using autoattack`). Enforced a 3.5-second bounded safety ceiling in `ScriptSkill.cs` (`Wait.ForTrue`) so background threads break cleanly and trigger `OnTargetReset()` during monster death or UI lockups without deadlocking or freezing your character.
* **ActionScript 3 (SWF) Garbage Collection & Animation Protection:** Throttled Flash display tree inspection modules (`DisableFX` and `HidePlayers`) down to synchronized 2 FPS checks, cutting string allocations and Flash garbage collection overhead by over 93% to prevent overnight out-of-memory crashes. Disabled destructive animation clipping (`OptimizePlayers`) so room and character poses keep playing smoothly during map transitions, and added strict null safety checks in `RemoteRegistry.as` (`destroy()` and `ext_destroy()`) to eliminate Flash `Error #1009` crashes during C# COM object cleanups.
* **C# / .NET Core Concurrency & Memory Upgrades:** Added automatic Large Object Heap cache clearing for dynamic XML game queries (`getGameObject`), fast multi-file `#include` dependency preprocessing, accurate `ScriptWait` loop math (`dividing timeout arguments by 100 instead of 1000`), and thread-safe COM dispatching across UI boundaries while keeping background script threads thread-safe.
* **Multi-Account Instant Teardown:** Replaced sequential COM handshakes on the UI thread with asynchronous background process killing (`TabbedHostWindow.xaml.cs`), allowing instantaneous 1-millisecond window closures and parallel `Skua.exe` termination for 7+ active tabs without UI freezing.
* **SWF Memory Caching:** Implemented `PreloadSwf()` in `FlashUtil.cs` to cache files directly in RAM, accelerating instance launches. Enforced `WMode="direct"` for hardware-accelerated rendering.
* **Optimized Memory Management:** Mitigated memory leaks in the WPF client by properly detaching from `StrongReferenceMessenger` and correctly managing `WeakEventManager` hooks, preventing slowdowns over long sessions.
* **Fluid Asynchronous Operations:** Replaced blocking `Thread.Sleep` calls with non-blocking `await Task.Delay` across automated hunting loops, freeing up thread-pool resources and reducing UI micro-stutters.
* **Clean Task Lifecycles & UI Decoupling:** Overhauled background processes (like `DailyTracker` and `SingleInstanceWatcher`) to respect cancellation tokens and fully decouple heavy game-state checks from the main rendering thread.
* **Network Proxy Optimization:** Refactored `CaptureProxy.cs` to utilize `Encoding.UTF8.GetBytes()` for packet conversion, minimizing latency during high-traffic sessions.
* **GitHub Script Caching Engine:** Engineered `ScriptDates.json` to store metadata and track SHA hashes. Intelligent API querying conserves rate limits and provides graceful UI fallbacks on connection failure.
* **Background Connection Stability:** Repositions inactive clients off-screen and uses a `WPF DispatcherTimer` to ping the `isLoggedIn` COM interface every 500ms, preventing OS-level socket throttling.
* **Active Memory Management:** Introduced `MemoryUtils.cs` to periodically trim the application’s working set, ensuring RAM stability during long, multi-day farming sessions.
* **Asynchronous Flash Injection:** Built a background loop to actively override ActionScript 3 variables (e.g., `world.strMapName`) every 500ms to maintain privacy in Streamer Mode.
* **Release Portability:** Updated plugins like Daily Tracker with PostBuild MSBuild targets to automatically compile and bundle into the release folder during `BuildRelease.bat`.
* **Velopack Deployment Architecture:** Fully migrated the deployment infrastructure to Velopack. Enables rapid silent installations, automatic desktop shortcut provisioning, and a built-in Updater Tab within the Manager for background auto-updating via the GitHub Releases API.
* **And Many More:** Dozens of underlying architectural, thread-safety, and runtime stability enhancements across the entire framework.

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
