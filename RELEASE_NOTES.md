# VibeSkua v2.1.6

## What's New in 2.1.6

### Quick Account Launch (New Feature)
Launch one account, several, or a whole account group straight from the client's tab bar.
- **Searchable Multi-Account Picker**: Find accounts or account groups by name and launch them in one go, with batch progress and live status for each account.
- **Tabs or New Windows**: Launch accounts into tabs, or into separate VibeSkua windows.
- **Default Hotkey**: Open Quick Launch from anywhere with `Ctrl+Shift+L`.
- **Duplicate Protection**: An account that is already logged in or launching (in any VibeSkua window, including manually logged-in tabs) is not launched a second time.
- **Reliable Queueing**: Closing a tab or window finishes its queued launch, failed new-window launches can be retried, and a stuck "Launching" status clears when the window closes.

### Hotkey Editor
- Every registered action can now be bound, with **Browse All Actions** showing the full list.
- Assigned hotkeys show a description of what they do.
- Adding a hotkey is a single **Add** button, and unassigned or empty default hotkeys are kept and repaired.

### OMNISCOPE Plugin (New)
A live game intelligence plugin bundled with the client.
- **Live Telemetry**: Entities with search, filtering and an inspector, XP progress, active quests, inventory (equipped and favourited items), and drop, kill, quest and shop events, with a configurable polling rate.
- **Game Data Workspace**: Searchable catalogs of quests, shops, map items, drops and entities, including read-only shop purchase requirements.
- **Game and Map SWFs**: Downloads the live AQW client and map SWFs, decompiles them with FFDec and indexes the source. Data can be exported.

### Server Latency in the Manager
- Server names in the Manager's server list are coloured by measured latency (good, approaching the limit, high), with a tooltip explaining the colour.
- Latency thresholds can be changed in Manager Options, which now scrolls and has proper editors for each option type.

### Watchdog Improvements
- The relogin watchdog waits for a fully established session before carrying on.
- The script watchdog counts your own hits as progress, its rescues no longer reset their own stall timer, and it re-checks for progress before stepping in, so fewer false rescues.

### Avalonia 12 & Window Fixes
- The UI is upgraded to Avalonia 12 (window decorations, tab drag and drop, Army Queue drag and drop, clipboard).
- Embedded game resizing is fixed, the login screen fills the window when resizing, and clients in tabs no longer show a resize gutter.

### Other Fixes
- FFDec now ships with its own portable Java runtime, so no separate Java install is needed, and it launches correctly from paths with spaces.
- The Cosmetics and OMNISCOPE plugin windows report launch errors instead of failing.

**Full Changelog**: https://github.com/NinjaXz/VibeSkua/compare/v2.1.5...v2.1.6

# VibeSkua v2.1.5

## What's New in 2.1.5

### ⏱️ Manager Task Scheduler (New Feature)
A complete, built-in task automation and recurring job scheduler directly inside VibeSkua Manager.
- **Flexible Scheduling Modes**: Run automated botting tasks with customizable minute/hour intervals, or at precise daily scheduled times.
- **Multi-Account & Multi-Script Queueing**: Assign single accounts, multiple saved accounts, or account groups to execute an ordered queue of scripts sequentially.
- **Client Restart Between Scripts**: Optional setting to cleanly terminate and relaunch the game client between scripts or accounts, eliminating memory bloat, stale cache, and memory leaks during multi-hour schedules.
- **Windows Startup Integration**: Toggle option to automatically launch VibeSkua Manager and start the Task Scheduler on system boot so your schedules run uninterrupted.
- **12h / 24h Time Display & Next Run Countdowns**: Easily switch between 12-hour AM/PM and 24-hour military time formats across all scheduled jobs, with dynamic countdown timers showing exact time until the next scheduled trigger.
- **Interactive Run History & Live Diagnostics**: View detailed execution history for each job run, including script status, account progression, exit codes, and a live output log reader styled to match your client theme.

### 🛡️ Autonomous Script Watchdog
An intelligent background monitoring system for the game client that keeps farming scripts running without manual intervention.
- **Automatic Hang & Freeze Detection**: Continuously observes player state, combat flow, and script responsiveness to detect when a script is stuck.
- **Tiered Autonomous Recovery**: Automatically escalates through progressive recovery actions to unfreeze gameplay:
  - Clears stale packet and cell states.
  - Resets combat locks and targets.
  - Re-joins and repositions to the target room/cell.
  - Gracefully restarts the active script if the instance becomes unresponsive.
- **Customizable Watchdog Options**: Configure timeout thresholds and toggle the watchdog on or off in Application Options, complete with detailed descriptive tooltips.

### 📱 Android Remote Controller
Major updates and stability improvements for the VibeSkua Android Controller app (`skua-remote.apk`).
- **Robust Item Actions**: Fixed inventory/bank actions (equipping items, moving to bank, withdrawing to inventory) by accepting both item ID and item name seamlessly across remote endpoints.
- **Enhanced Vault (Bag & Bank)**:
  - Fixed incomplete item loading in Bag and Bank tabs.
  - Restored full category navigation tabs: All, Weapons, Classes, Armors, Helms, Capes, Pets, Misc, and AC-tagged items.
  - Restored a dedicated, persistent **OPEN BANK** button in the Vault header to easily open and refresh your bank in-game at any time.
- **Army Script Scheduler Upgrades**:
  - Multi-script queueing: Queue multiple scripts sequentially from the script repository without the dialog closing after each pick.
  - Dynamic queue reordering: Move queued scripts up (`▲`) or down (`▼`) to fine-tune the exact playlist execution order before starting.
- **Clean Account Launching**: Saved accounts launched from the Army tab now launch cleanly into tabbed windows without inherited or unwanted auto-loaded scripts.

**Full Changelog**: https://github.com/NinjaXz/VibeSkua/compare/v2.1.4...v2.1.5

# VibeSkua v2.1.4

# Fix ButlerV5 hang and Auto-Relogin issues
shoutout jonjon and lil peezer 🙏 rest in peace
## What's Changed
* fix relogin by @l0newolf12 in https://github.com/NinjaXz/VibeSkua/pull/63
* compatibvility update with new FlashUtil cahanges by @l0newolf12 in https://github.com/NinjaXz/VibeSkua/pull/64
* bump ver number by @l0newolf12 in https://github.com/NinjaXz/VibeSkua/pull/65


**Full Changelog**: https://github.com/NinjaXz/VibeSkua/compare/v2.1.3...v2.1.4

# VibeSkua: 2.1.3

## New
- **Calendar** (Tools & Helpers): daily and weekly reset countdowns plus the current AQW boosts
- **Notification Center**: a feed of game and script events with category filters, running all session
- **Advanced Stats**: live gold/XP/item/CP rates, level and rank progress bars (shows max level/rank), rate units, and a manual rate history table you can clear
- **Inventory**: item quantity goals with progress, highlights for equipped, stack and status items, and filters for them
- **ButlerV5**: copy master's walking, quest sync (incl. reward choices), Send All goto for outside players with target history, roster refresh, tidier roster window
- **Auto-jump** on map position changes (Application Options)
- **Grandpa Mode**: bigger text in logs and packet output
- **Hotkeys**: new Pause Script action
- **Army Control**: Show FPS option
- Search Scripts order cycles name/date
- Fast Travel defaults to private rooms
- New monocle icons (gold for Manager, platinum for client)
- Buttons shrink on press with a hover glow
- Scripts: favorite-aware inventory checks and a safe BankAll helper

## Fixes
- Quest Loader resumes after a disconnect or relogin and scans more reliably
- Relogin stops after an account lockout instead of retrying
- Capture proxy and packet logger start/stop more safely
- Packet Interceptor filters now match real packets
- Hotkeys fire once per key press
- Scripts start unpaused; start notice comes before the script runs
- Script options: removed enum choices reset to default instead of crashing
- CoreBots presets/class picks kept across tab switches
- Fast Travel editor dialog fixed
- Settings kept if a reload fails
- Dialogs opened from menus take the first click; tool windows stay on screen
- Bad saved map cache recovers instead of breaking
- Boost search in inventory/bank fixed
