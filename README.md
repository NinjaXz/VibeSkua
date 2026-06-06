# Skua Enhanced

A feature-rich, high-performance derivative of [auqw/skua](https://github.com/auqw/skua), engineered for advanced automation, stability, and streamlined multi-client management.

## Skua Architecture Comparison
The following overview compares the systems and core features between the original `auqw/skua` repository and this enhanced fork.

### Quality of Life & Features

| Feature | Original | This Fork |
| :--- | :--- | :--- |
| **Discord Integration** | Lacked native capability; relied on external scripts. | `DiscordWebhookService` integrated natively. Supports automated alerts for status changes, rare drops (with screenshots), and live-pings. |
| **Headless Mode** | Full-screen rendering; high CPU/GPU demand per instance. | Introduced a 1x1 hidden pixel viewport, forcing Flash to bypass geometry/blitting and significantly reducing resource consumption. |
| **Script Scheduling** | Required manual initialization and supervision. | Added `ScriptSchedulerViewModel` for autonomous script queuing via `QueueScriptMessage` at specific dates and times. |
| **UI Environment** | Spawned multiple external Win32 windows. | Embedded `EmbeddedMainWindow.xaml` with dynamic SWF patching for a unified, tabbed WPF interface. |
| **Script Sorting** | Basic navigation options. | Expanded `ScriptRepoViewModel.cs` to support dynamic sorting by Name, Date, or File Size (Ascending/Descending). |

### Performance & Engine Optimizations

* **SWF Memory Caching:** Implemented `PreloadSwf()` in `FlashUtil.cs` to cache files directly in RAM, accelerating instance launches. Enforced `WMode="direct"` for hardware-accelerated rendering.
* **Network Proxy Optimization:** Refactored `CaptureProxy.cs` to utilize `Encoding.UTF8.GetBytes()` for packet conversion, minimizing latency during high-traffic sessions.
* **GitHub Script Caching Engine:** Engineered `ScriptDates.json` to store metadata and track SHA hashes. Intelligent API querying conserves rate limits and provides graceful UI fallbacks on connection failure.
* **Background Connection Stability:** Repositions inactive clients off-screen and uses a `WPF DispatcherTimer` to ping the `isLoggedIn` COM interface every 500ms, preventing OS-level socket throttling.
* **Active Memory Management:** Introduced `MemoryUtils.cs` to periodically trim the application’s working set, ensuring RAM stability during long, multi-day farming sessions.
* **Client Version Validation:** Removed intrusive update popups within `ScriptManager.cs` to ensure completely uninterrupted, headless automation.

---

### Disclaimer
*This project is a derivative of [auqw/skua](https://github.com/auqw/skua) and is licensed under the MIT License. It is intended for educational and quality-of-life improvement purposes only. Use of this software may violate the Terms of Service of the associated game; the author assumes no responsibility for any account actions taken by game developers.*
