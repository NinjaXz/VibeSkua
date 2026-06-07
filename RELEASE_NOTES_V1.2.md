# VibeSkua V1.2 Release Notes

Welcome to **VibeSkua V1.2**! This update focuses heavily on client stability, visual improvements, and stripping out unnecessary bloat for a cleaner, faster experience. 

## 🚀 Key Features & Improvements

### 1. True Borderless Scaling (No More Black Bars!)
- **Root Aspect-Ratio Fix:** The Flash player's internal ActionScript `StageScaleMode` lock has been successfully overridden. 
- **Dynamic Stretching:** The client now actively forces `exactFit` scaling on the WPF `SizeChanged` event. Whether you resize the window manually or snap it to full screen, the game will stretch perfectly to the edges without leaving black bars!
- **COM Initialization Fix:** Resolved an underlying bug where resizing or loading the game too early would throw an `InvalidActiveXStateException` and falsely trigger a "Clean Flash is missing" crash dialog.

### 2. Thread-Safe Script Pausing
- **Bulletproof Halts:** Replaced the legacy and dangerous `Thread.Suspend()` logic with a modern, thread-safe `ManualResetEventSlim` gate.
- **Combat Loop Integrity:** Scripts and combat loops now halt gracefully. Pausing your bot mid-combat or during heavy movement will no longer cause race conditions, crashes, or lock up the internal manager.

### 3. Lean UI & Skua Manager Cleanup
- **Bloat Removal:** Stripped out unused and unneeded tabs from the Skua Manager, including: *Goals*, *Updates*, *Change Logs*, and *About*.
- **Authentication Stripped:** Removed GitHub authentication logic completely to provide a faster, offline-friendly, and completely standalone launch experience.
- **Nag-Free Settings:** Removed annoying pop-ups and options regarding client updates, pre-releases, and deleting `.zip` files after downloads.

### 4. Vibe Coded Branding
- Standardized the global title across the Multi-Client Manager, the embedded client, and all sub-clients (Lite, Sync, Follower) to proudly display **VibeSkua V1.2**.

---
*Built entirely through AI-assisted development and pure momentum.*
