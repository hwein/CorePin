# Privacy

CorePin runs entirely on your PC. It contains no network code: no telemetry,
no crash reporting, no update checks, no accounts.

## What CorePin stores on your PC

Everything lives in `%LOCALAPPDATA%\CorePin\` — except the autostart entry, see below.
Delete that folder and everything in it is gone.

- `config.json` — your rules (program file name, last known full path, selected
  threads, enabled flag, rule id), settings (poll interval, log level, window
  position and size, and the start-with-Windows state Windows reports — `off`,
  `normal`, or `admin`; Windows stays the authority and the value is never acted
  on), and your CPU model name with its logical processor count. A full path may
  contain your Windows user name if the program lives in your profile folder.
- `config.skipped.json` — rules that could not be read, same fields. An unreadable
  `config.json` is renamed aside, never deleted.
- `logs\` — one file per session: CPU topology (model, cores, caches), file names
  and process IDs of matched programs, affinity masks, Windows error codes, and
  CorePin's own events. Default level `info`; `debug` adds per-tick detail.
  Capped at 10 MB per file, 5 files, 50 MB in total. No user name, machine name,
  or Windows version is written.
- **The autostart entry**, outside `%LOCALAPPDATA%`, created only after you check
  *Start with Windows* in the tray menu: from a normal instance, the Run value
  `CorePin` in the registry, holding the full path to `CorePin.exe`; from an
  elevated instance, the task `CorePin Autostart` in Task Scheduler, holding that
  path plus your account's SID, your account name, and the author `CorePin`.
  Unchecking it removes the entry again, from the instance type that created it.

## What CorePin reads while running (memory only)

- The list of running processes — file name, path, process ID — to match rules and
  to fill the "+ From running…" picker.
- Icons from the executables of your rules.
- CPU identity and topology, elevation status, Windows theme settings.
- The autostart state from the registry (the `CorePin` Run value and its
  `StartupApproved` entry) and from Task Scheduler (the `CorePin Autostart` task),
  read at startup, whenever the tray menu opens, and after each change.

Nothing else from other programs: no window contents, no keystrokes, no files.

## What leaves your PC

Nothing — unless you trigger it:

- **Copy topology** puts the topology JSON (vendor, CPU model, core and cache
  layout, CorePin version) on the clipboard.
- **Help us name them** copies that JSON and opens `github.com/hwein/CorePin/issues/new`
  in your browser, with the CPU model name in the issue title. The JSON reaches
  GitHub only if you paste and submit the issue; GitHub's privacy policy applies there.

The topology JSON contains no serial numbers, user names, or paths.
