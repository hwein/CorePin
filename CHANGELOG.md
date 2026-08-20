# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
- On unrecognized CPUs the map shows generic group names with a "Help us name them" button that copies the topology.
- "+ From running…" opens a searchable picker of running programs; Enter or double-click creates the rule.
- "+ Add app" creates a rule from an .exe picked in a file dialog.
- Rule rows show the program's real icon, extracted in the background; a neutral placeholder stands in when there is none.

### Changed

- The log uses the standard .NET level names; the old values `info` and `warn` are still accepted.

### Fixed

- An unreadable config that cannot be renamed aside is reported as such instead of claiming a rename.
- A stale `config.skipped.json` no longer survives a load that ends with an unreadable config.
- A rule without an `exeName` is skipped with its own reason instead of `malformed entry`.
- The very first start no longer logs a false "logicalProcessors changed" warning.
