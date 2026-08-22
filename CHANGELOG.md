# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.0.3] - 2026-08-22

### Changed

- The release build is framework-dependent: it needs the .NET 10 Desktop Runtime (x64), and the download is under 1 MB instead of 120 MB.

### Fixed

- The released `CorePin.exe` starts again; the 0.0.1 and 0.0.2 downloads crashed on launch.

## [0.0.2] - 2026-08-22

### Added

- Start with Windows from the tray menu; keeps the rights CorePin was started with.

### Fixed

- The version reported in the log and in the file properties now matches the release version.

## [0.0.1] - 2026-08-21

### Added

- Application window that follows the Windows light/dark mode, including the title bar.
- Session log file under `%LOCALAPPDATA%\CorePin\logs\`, with `--log-level debug|info|warn`.
- `--dump-topology` writes the detected CPU topology as JSON to standard output.
- The log records the detected processor clusters, their labels and cache sizes at startup.
- Startup is refused with an explanatory message on systems with more than one processor group.
- Rules and settings are read from `%LOCALAPPDATA%\CorePin\config.json` at startup.
- A malformed rule is skipped and listed in `config.skipped.json`; an unreadable config is renamed aside and startup continues.
- Rules are now applied: programs listed in `config.json` are pinned to their cores and re-pinned if their affinity changes.
- Tray icon with a context menu: Open CorePin, Copy topology, Open log folder, Exit.
- Closing the window (X or Esc) hides it to the tray; the process keeps running until Exit.
- Launching CorePin while it is already running brings the existing window to the front.
- `--tray` starts CorePin hidden to the tray, without opening the window.
- The tray icon follows the taskbar theme (light/dark variant).
- Main window with the rule list, per-rule status, status line, and config notices.
- Rules can be disabled (Space) and deleted (Del with inline confirmation, or the row's context menu).
- Every change is saved automatically; window position and size survive a restart.
- The window opens centered on the monitor with the mouse pointer on first start.
- The tray tooltip shows the current rule counts.
- A shield marker in the status line shows when CorePin runs elevated.
- Interactive CPU map: one cell per core with clickable SMT halves, cluster headers with facts, and hover/keyboard support.
- Presets generated from the detected topology (All plus one per cluster) and an SMT threads toggle.
- Map footer with live thread count and a click-to-copy affinity mask.
- Changing a rule's cores applies immediately to running processes and is saved automatically.
- On unrecognized CPUs the map shows generic group names with a "Help us name them" button that copies the topology and opens a prefilled GitHub issue.
- "+ From running…" opens a searchable picker of running programs; Enter or double-click creates the rule.
- "+ Add app" creates a rule from an .exe picked in a file dialog.
- Rule rows show the program's real icon, extracted in the background; a neutral placeholder stands in when there is none.
- Real application and tray icons (the "one core lit" grid) replace the placeholder art, with light and dark tray variants.

### Changed

- The log uses the standard .NET level names; the old values `info` and `warn` are still accepted.
- The window can no longer be maximized; it stays freely resizable.
- On first start the window opens above the system tray instead of screen-center.
- A brief "saved" hint appears in the status line after changes are written.
- Cluster frames always span the full card width; on unrecognized CPUs the hint and its button share one row.

### Fixed

- An unreadable config that cannot be renamed aside is reported as such instead of claiming a rename.
- A stale `config.skipped.json` no longer survives a load that ends with an unreadable config.
- A rule without an `exeName` is skipped with its own reason instead of `malformed entry`.
- The very first start no longer logs a false "logicalProcessors changed" warning.
- Rule list and picker rows use theme colors for titles and selection instead of the classic grey chrome.
- A freshly started CorePin comes to the front even while other windows are maximized.
