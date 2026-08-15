# CorePin

**Pin apps to the cores that matter.**

CorePin binds selected programs to selected CPU cores — and remembers it across
restarts.

Windows can already do this. Task Manager offers it under *Set affinity* — but
awkwardly, hidden away, with no indication of which core belongs to which CCD, and
above all **it does not stick**: the next time the application starts, the setting is
gone. CorePin extends that in exactly three places:

1. **Make CCDs and core types visible** — a map of the processor instead of a list of
   `CPU 0 … CPU 31`, showing which cores belong together and which carry the V-Cache.
2. **Make setup quick** — pick a program from a list, click a preset, done.
3. **Remember the setting per executable** — set it once instead of after every launch.

Nothing more. But those three then have to be genuinely good.

## Status

Early development. Not yet released, not yet usable.

## Requirements

- Windows 11 x64 (build 22000+). Windows 10 21H2+ should work but is not tested.
- Systems with 64 or fewer logical processors — one Windows processor group. This
  covers every consumer CPU. Larger systems are detected and declined honestly rather
  than handled incorrectly.

## Building

Requires the .NET 10 SDK. No other dependencies — CorePin uses **zero NuGet packages**
by design, so the repository can be read and built by anyone without a package feed.

```
dotnet build
```

## License

MIT — see [LICENSE](LICENSE).
