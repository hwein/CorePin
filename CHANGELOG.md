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

### Changed

- The log uses the standard .NET level names; the old values `info` and `warn` are still accepted.
