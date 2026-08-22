# CorePin

**Pin apps to the cores that matter.**

CorePin binds selected programs to selected CPU cores and remembers it across restarts.
It lives in the tray: the window shows the processor as a map grouped by CCD or P/E
cluster, you pick a running program and a preset, and the rule is applied whenever that
program runs. Only documented Win32 APIs — no driver, no injection, no hooks.

Two limits of the approach: CorePin pins *after* start, not *at* start, so a program that
sizes its thread pool at launch still creates one thread per core; and child processes
inherit the mask, so pinning a launcher pins everything it starts. Programs running
elevated need CorePin to run elevated too; where Windows refuses a change, the rule says
so, with the reason.

## Download

Prebuilt binaries are on the [Releases page](https://github.com/hwein/CorePin/releases):
one executable, no installer. It needs the
[.NET 10 Desktop Runtime (x64)](https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe);
if it is missing, the first start shows a dialog with the download link. Current releases
are unsigned; code signing through [SignPath Foundation](https://signpath.org) is pending.

## Requirements

- Windows 11 x64 (build 22000 or newer). Windows 10 21H2+ should work but is not tested.
- .NET 10 Desktop Runtime (x64).
- 64 or fewer logical processors — one processor group. Larger systems are declined with
  a message.

## Start with Windows, removing CorePin

*Start with Windows* in the tray menu registers a Run value (from a normal instance) or a
scheduled task (from an elevated instance); names and contents are in
[PRIVACY.md](PRIVACY.md). Uncheck it before deleting `CorePin.exe`, then delete
`%LOCALAPPDATA%\CorePin\`, which holds the configuration and logs.

## Privacy

CorePin has no network code and sends nothing anywhere; what it stores locally, and what
leaves your PC only when you ask for it, is listed in [PRIVACY.md](PRIVACY.md).

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), nothing
else — CorePin has zero NuGet dependencies.

```
dotnet build
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports are welcome; feature proposals are
measured against the scope above, and the bar is deliberately high.

## License

MIT — see [LICENSE](LICENSE).
