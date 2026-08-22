# CorePin

**Pin apps to the cores that matter.**

CorePin binds selected programs to selected CPU cores — and remembers it across
restarts.

---

## Why this exists

Modern desktop and laptop CPUs are no longer uniform. A Ryzen 9 7950X3D has one CCD
with 3D V-Cache — excellent for games — and one without, which clocks higher. A Core
Ultra 9 has P-cores, E-cores and LP-E-cores. The Windows scheduler usually guesses
right, but for games, emulators, audio workstations and per-core-licensed software it
regularly guesses wrong, and then it costs double-digit percentages of performance.

Windows can already fix this. Task Manager offers it under *Set affinity* — but
awkwardly, buried three clicks deep, with no indication of which core belongs to which
CCD or which ones carry the V-Cache. And above all **it does not stick**: the next time
the application starts, the setting is gone.

CorePin extends that in exactly three places:

1. **It makes the processor visible.** A map grouped by CCD or P/E cluster instead of a
   flat list of `CPU 0 … CPU 31`, showing which cores belong together, how much L3 they
   share, and which ones carry the V-Cache.
2. **It makes setup quick.** Pick a program from a list of what is running, click a
   preset, done.
3. **It remembers the setting per executable.** Set it once instead of after every
   launch.

Nothing more. Those three then have to be genuinely good.

## What it is not

CorePin is a small tool with a deliberately narrow job. It does not do priority classes,
I/O priority or power profiles. It does not monitor, graph or log utilization. It does
not overclock, undervolt or touch PBO. It uses no kernel driver, no process injection
and no hooks — only documented Win32 APIs. And it never guesses on your behalf: there is
no "intelligent" auto-optimization, because you know your workload and it does not.

If you want any of that, [Process Lasso](https://bitsum.com/) does it well. CorePin is
for the case where you want one thing and want it to stay.

## How it works

CorePin lives in the tray. A watcher checks once a second whether a process matching one
of your rules is running, and applies the affinity mask you picked. Changes you make in
the window take effect immediately — you never restart the game.

Two things are worth knowing up front, because they are properties of the approach
rather than bugs:

- **CorePin pins *after* start, not *at* start.** Many applications size their thread
  pool at launch from `GetActiveProcessorCount`. They create threads for every core and
  are then crowded onto the allowed ones. The pinning still works, but it is not the
  same as launching with fewer cores.
- **Child processes inherit the mask.** Pinning `steam.exe` pins every game started from
  it. Rules match on the executable's file name, so a rule for a generic name such as
  `javaw.exe` or `start_protected_game.exe` will catch more than you meant.

Some programs cannot be pinned at all. Anything running elevated needs CorePin to run
elevated too, and Store or Game Pass titles often live in a job object that forbids the
change. In both cases CorePin says so, on the rule, with the reason — it never pretends
to have succeeded. A release can fail the same way: if Windows refuses to restore the
original mask when a rule stops applying — disabled, deleted, or set back to all cores —
the process keeps its pinned cores until it exits. A rule that still exists shows
*Blocked by Windows*; after a delete, only the log records the failure.

## Start with Windows

The tray menu has a checkbox, *Start with Windows*, off by default. CorePin never asks
for UAC and never elevates itself — the user decides at launch: a plain double-click
starts it normally, *Run as administrator* starts it elevated.

Checking the box means Windows starts CorePin at the next sign-in, with the same rights
as the instance the box was checked in:

- **From a normal instance**, it writes a value named `CorePin` under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. It shows up in Task Manager,
  under *Startup apps*. Turning it off there is respected — the checkbox then shows
  off, and checking it again turns it back on.
- **From an elevated instance**, it creates a task named `CorePin Autostart` in Task
  Scheduler's root folder, running with the highest available rights of your own
  account. It shows up only in Task Scheduler, not in Task Manager's *Startup apps*.

Both mechanisms start `CorePin.exe --tray`.

The change takes effect at the next sign-in; the running instance is left as it is. To
switch which one is used, turn the checkbox off, then back on from the instance you
want it to run as.

If the box was checked from an elevated instance and you uncheck it from a normal one,
nothing changes and this appears instead:

> Start with Windows was turned on from an instance running as administrator. Start CorePin as administrator to turn it off.

Start CorePin as administrator, then uncheck the box there.

Moving or renaming the executable: the Run value repairs itself the next time CorePin
starts, once the old file is gone. The task repairs itself the same way, but only at
an elevated start — a normal start after that only records the stale path in the log
and changes nothing. A second copy of CorePin in another folder, with the old file
still in place, changes nothing either way.

Windows usually shows a notification when the Run value is added, saying an autostart
app was added — that is Windows, not CorePin.

The task launches whatever sits at its recorded path, elevated — put CorePin in a
folder ordinary programs cannot write to (for example under `Program Files`) before
checking the box from an elevated instance.

*Run as administrator* opens a UAC dialog. Confirming it with the credentials of a
different account runs the whole CorePin process under that account — a different
Windows profile, a different `config.json` and different logs, and a checkbox that
controls that account's autostart, not yours.

### Removing CorePin

Turn *Start with Windows* off first (an elevated instance can remove both kinds of
entry, a normal one only the Run value), then delete `CorePin.exe`. Otherwise the Run
value `CorePin` or the task `CorePin Autostart` is left behind, without effect but
still there: remove it in Registry Editor, under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (Task Manager can only disable
it, not delete it), or in Task Scheduler. Also delete `%LOCALAPPDATA%\CorePin\`, which
holds the configuration and logs.

## Download

Prebuilt binaries are published on the
[Releases page](https://github.com/hwein/CorePin/releases). CorePin is a single
self-contained executable — no installer, no runtime to install.

Future releases may be code-signed through [SignPath Foundation](https://signpath.org)
(free code signing by [SignPath.io](https://signpath.io)); the application is pending,
current releases are unsigned.

## Privacy

CorePin has no network code and sends nothing anywhere; what it stores locally, and what
leaves your PC only when you ask for it, is listed in [PRIVACY.md](PRIVACY.md).

## Requirements

- Windows 11 x64 (build 22000 or newer). Windows 10 21H2+ should work but is not tested.
- 64 or fewer logical processors — one Windows processor group. This covers every
  consumer CPU, up to and including a 32-core Threadripper. Larger systems are detected
  and declined honestly rather than handled incorrectly.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Nothing
else — CorePin has **zero NuGet dependencies** by design, so the repository can be read,
audited and built by anyone without a package feed.

```
dotnet build
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports are welcome; feature proposals are
measured against the scope above, and the bar is deliberately high.

## License

MIT — see [LICENSE](LICENSE).
