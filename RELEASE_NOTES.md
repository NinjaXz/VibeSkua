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
