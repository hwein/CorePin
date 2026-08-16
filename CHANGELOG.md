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
- Startup is refused with an explanatory message on systems with more than one processor
  group.
- Rules and settings are read from `%LOCALAPPDATA%\CorePin\config.json` at startup. The
  file may be edited by hand and tolerates comments and trailing commas.
- A single malformed rule is skipped and listed in `config.skipped.json` instead of
  discarding the whole file; an unreadable file is renamed aside and startup continues.
- Rules are now applied: a program listed in `config.json` is pinned to its cores within a
  second, whether it starts later or is already running, and is pinned again if something
  else changes its affinity.

### Changed

- The log uses the standard .NET levels `trace`, `debug`, `information`, `warning`, `error`
  and `critical`. A real failure is now separated from a handled oddity: a watcher that
  gives up is `critical`, a release or a config write that fails is `error`. The old
  `settings.logLevel` values `info` and `warn` keep working.
