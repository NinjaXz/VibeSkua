# VibeSkua

A modern, multi-account AQW client and automation toolkit built on the Skua framework. VibeSkua combines a tabbed Avalonia desktop client, army management, scripting tools, diagnostics, plugins, and optional remote control.

> **Development note:** VibeSkua is developed with AI-assisted workflows alongside manual engineering and testing.

VibeSkua is based on [auqw/skua](https://github.com/auqw/skua) (v1.4.3.0) and builds on the early Avalonia port by [ArrowDev](https://github.com/ArrowDev123/Skua). This fork extends that foundation with multi-client orchestration, client-side tooling, and ongoing framework and stability improvements.

---

## Highlights

### Multi-client desktop workspace

- **Tabbed client host:** Run and manage multiple game instances from one desktop window, with per-account tabs and shared workspace controls.
- **Resizable workspace:** The host window and embedded game views resize together.
- **Army controls:** Start and stop scripts across accounts, load scripts in batches, jump accounts to maps or players, and coordinate common client settings.
- **Grid view:** Monitor multiple active accounts in a single overview.
- **Army Scheduler and Queue:** Coordinate script schedules and account groups without managing every client separately.
- **Headless mode:** Reduce rendering overhead for unattended multi-account sessions.

### OMNISCOPE

OMNISCOPE is the live game-intelligence and inspection workspace, available through the Plugins area.

It brings together live game and player information with inspection tools for exploring the current game environment. Its tooling includes:

- Player, map, room, entity, target, health, class, aura, skill, quest, inventory, and event information.
- Live telemetry refresh and event history.
- Map and game-data inspection, including map and shop indexing.
- SWF source indexing and game-client resolution helpers.
- Map SWF download and inspection utilities.

### Remote Control and Android Companion

VibeSkua includes an embedded HTTP remote-control service that can be used by the Android companion application or another compatible client on the network.

The companion provides remote access to supported client and account information, including:

- Connection management and account switching.
- Account and army status dashboards.
- Script controls and multi-account actions.
- Army scheduling and broadcast controls.
- Drops, inventory, quests, temporary items, and chat information.
- Connection recovery and configurable notifications.

The remote-control service uses port **8088** by default. Configure the host address and port in the companion to match the computer running VibeSkua. Keep remote access on trusted networks and only expose it beyond your local network if you understand and have configured the associated network security.

### Automation and scripting

- **Script Watchdog:** Tracks script progress and network activity, detects prolonged stalls, and provides diagnostic feedback. Optional Discord notifications are supported.
- **Resilient quest loading:** Improved handling of disconnects, relogins, failed batches, and account state transitions.
- **Script Scheduler:** Queue scripts with independent option profiles, custom display names, and saved playlists.
- **Script repository:** Search, filter, sort, and manage community scripts, alongside local scripts and custom script directories.
- **Pause and resume:** Pause supported script execution without discarding its current position.
- **Custom hotkeys:** Configure shortcuts for client and multi-account actions.
- **Gameplay API additions:** Includes additional script controls such as monster selection without automatically approaching or attacking.

### Manager and plugins

- **Server health indicators:** The Manager server list measures latency and displays connection-health states. The high-latency threshold and approaching-threshold percentage can be configured in Manager options. This is a Manager feature and does not add latency hooks to the game client.
- **ButlerV5:** Multi-account coordination tools, including roster management, follower workflows, and batch actions.
- **Cosmetics:** Cosmetic and equipment-related tools.
- **Daily Tracker:** Daily and weekly progress and lockout tracking.
- **Diagnostics:** Client logs, console and diagnostic utilities for troubleshooting.

### Client architecture and reliability

VibeSkua uses **Avalonia 12** for its desktop UI and targets **.NET 10**. The client has continued to evolve beyond the original Avalonia port with updates to window management, dialogs, tab interactions, plugin lifecycle handling, settings, and background task coordination.

Other improvements include script lifecycle handling, login and relogin coordination, memory and resource management, and more robust multi-account operation.

---

## Installation

Pre-built Windows releases are published on the [VibeSkua Releases page](https://github.com/NinjaXz/VibeSkua/releases).

| Package | Description |
| --- | --- |
| **Setup installer** | Recommended for most users. Installs VibeSkua and supports the Velopack update workflow. |
| **Portable ZIP** | Extract and run without a traditional installation. |

The Manager is the primary entry point for launching and managing the client. The portable package also includes the client and its required runtime files.

### Android Companion

The Android companion is built from the `Skua.App.Android` project. The repository includes the current `skua-remote.apk` artifact; check the Releases page for the version intended to match your desktop build.

To build the Android application yourself, see [Build Tools](BuildTools/README.md).

---

## Building from source

### Requirements

- Windows x64 for the desktop client and its embedded Flash/COM integration.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Git and access to the repository's configured remotes.
- A Java 8 JDK on PATH for first-time Apache Flex setup, unless a compatible Flex SDK is already installed and configured.
- Network access for package restore and any build-time dependencies that need downloading.

The repository includes the ActionScript 3 sources and compiler setup. Packaging automatically configures Apache Flex when it cannot find an existing SDK; the first-time download requires Java 8. The finished package includes FFDec and a private Java 21 runtime, so end users do not need to install either separately.

### Fast local build

Run from the repository root:

```cmd
BuildProject.bat
```

This restores and builds the desktop projects and plugins, compiles the ActionScript client during packaging, assembles the local output under `Build\`, and launches the built client.

**Important:** When run on the `main` branch, `BuildProject.bat` also pulls `origin/main`, merges `upstream/main`, and pushes the synchronized result back to `origin/main`. Review your Git remotes and working tree before running it on `main`. On other branches, it builds the current checkout without performing that sync.

### Release packaging

Run:

```cmd
Publish-Release.bat
```

This invokes the main build workflow and then creates Velopack release assets under `Build\Releases\`. The script uses the version defined in `Directory.Build.props` unless a version is supplied as its first argument.

### Manual .NET build

To restore and build the solution without running the packaging workflow:

```cmd
dotnet restore Skua.sln
dotnet build Skua.sln -c Release --no-restore
```

A manual solution build does not run the complete AS3 compilation and packaging steps.

### Build documentation

Build and packaging scripts are organized under [`BuildTools/`](BuildTools/README.md). That guide covers the Windows build, release packaging, Android companion build, and internal helper scripts.

---

## Project layout

| Directory | Purpose |
| --- | --- |
| `Skua.App.Avalonia/` | Main desktop client. |
| `Skua.Manager.Avalonia/` | Account and client Manager. |
| `Skua.Core/`, `Skua.Core.Interfaces/`, `Skua.Core.Models/`, `Skua.Core.Utils/` | Core services, APIs, models, and shared utilities. |
| `Skua.Shared.Avalonia/` | Shared Avalonia UI components and assets. |
| `Skua.Plugin.*` | Optional client plugins, including OMNISCOPE, ButlerV5, Cosmetics, and Daily Tracker. |
| `Skua.App.Android/` | Android remote-control companion. |
| `Skua.AS3/` | ActionScript 3 game-client sources and compiler setup. |
| `BuildTools/` | Build, packaging, and runtime-staging helpers. |
| `FFDec/` | Bundled FFDec tooling used by source-inspection features. |

---

## Credits

- [auqw/skua](https://github.com/auqw/skua) for the original Skua framework and core botting API.
- [ArrowDev](https://github.com/ArrowDev123/Skua) for the early Avalonia UI port foundation.
- [NinjaXz](https://github.com/NinjaXz) and the VibeSkua contributors for the upstream project and ongoing development.

---

## License and disclaimer

VibeSkua is provided under the MIT License. It is a derivative project built on the Skua framework. Original game assets, data, and intellectual property remain the property of their respective owners.

**Educational and personal use only.** Use of this software may violate the associated game's Terms of Service. You are responsible for understanding and accepting the risks of using it, including possible account actions. The project is provided "as is", without warranty.
