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
