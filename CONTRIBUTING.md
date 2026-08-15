# Contributing to CorePin

Short and direct on purpose. This file is an example of the standard it describes.

## 1. Scope

CorePin binds selected programs to selected CPU cores and remembers it. That is the
whole product. Every contribution is measured against three questions — if any answer is
*no*, it does not go in:

1. Does it serve "pin a program to cores, permanently" directly?
2. Does a user need it in *typical* use — or only in a corner case?
3. Does it cost less complexity than it delivers value?

Explicitly out of scope, and not up for discussion without a deliberate change to the
project's direction: priority classes and power profiles · live monitoring, graphs or
history · overclocking and undervolting · kernel drivers, injection or hooks ·
"intelligent" auto-optimization · rules for Windows services · telemetry, cloud sync or
accounts · localization (the UI is English).

Making the case for a feature is the proposer's job, not the maintainer's.

## 2. The most useful contribution: CPU topology

CorePin contains **no table of CPU models**. Every cluster and label is derived from the
topology Windows reports, so the tool works on processors that did not exist when it was
written. Where the rules cannot make a safe statement, you get generic group names and
the full function — never an invented label.

That design has one consequence: it needs data from hardware the maintainer does not
own. If CorePin shows `Group 0` / `Group 1` instead of proper names on your machine,
there is a button under the CPU map — **Help us name them** — that copies a topology
dump and opens a prefilled issue. Paste, submit, done. That is the single most valuable
thing you can send, and it takes fifteen seconds.

Dumps become frozen regression tests, so a fix for your CPU cannot be undone later.

## 3. Ways to contribute

- **Topology dumps** — as above, or via `CorePin.exe --dump-topology`.
- **Bug reports** — open an issue with your CorePin version, CPU model, Windows build,
  reproduction steps, expected vs. observed behavior, and the relevant part of
  `%LOCALAPPDATA%\CorePin\logs\`. Set `"logLevel": "debug"` in
  `%LOCALAPPDATA%\CorePin\config.json` first if the problem is reproducible.
- **Feature contributions** — open a PR and argue §1 in writing.
- **Documentation fixes** — open a PR.

Before filing, please check the two documented limits in the README (pinning happens
after start; child processes inherit the mask). Both are properties of the approach, not
defects.

The maintainer does not hold design discussions. The written case belongs in the PR
description or a linked issue. It gets read and decided, not debated.

## 4. Binding rules for code

- **PRs target `next`, never `main`.** `main` only receives release merges.
- **Zero NuGet dependencies.** This is not a preference, it is the reason the repository
  can be audited and built by anyone. It holds for tests and debug builds too. What ships
  with the runtime is fine.
- **Only documented Win32 APIs.** No kernel driver, no injection, no hooks, no
  undocumented calls.
- **Never guess on the user's behalf.** If the topology does not allow a safe statement,
  fall back to generic names and keep the function. If an operation fails, say so on the
  affected rule with the reason and the next step — no silent failures, no reassuring
  status text.
- **KISS.** The simplest solution that meets the requirement. No abstraction for a
  hypothetical future, no refactoring that was not asked for, match the surrounding
  style.
- **Comments are short and only for what the code cannot show** — a constraint, a Win32
  pitfall, a *why*. Never a retelling of the code. If you change a line, check the
  comments around it and fix or delete the stale ones.
- Nullable warnings are errors. No `!` without a comment justifying the assertion.
- New behavior comes with tests. The test runner is hand-written (`dotnet run --project
  tests/CorePin.Tests`); there is no `dotnet test`, because a test framework would be a
  NuGet dependency.
- `dotnet build` and the test run must both be green.
- Commit messages follow [Conventional Commits
  1.0.0](https://www.conventionalcommits.org/en/v1.0.0/): imperative, subject line 72
  characters or less. A body only if it explains something the diff does not.
  **No tool or AI attribution trailers** — no `Co-Authored-By` bot lines. Reference
  issues with `Fixes #N`, not a narrative.
- **Do not edit `CHANGELOG.md` or version numbers.** Both are mechanical. Describe
  user-visible changes in the PR description so they can be recorded accurately.
- The repository's language is English — code, comments, docs and commit messages.

## 5. What happens to your PR

CI must be green; then the maintainer reads it and decides.

This is a spare-time project. There are no response-time guarantees, no obligation to
justify a decision, and no guaranteed review rounds. PRs and issues that miss the scope
or the rules can be closed without comment.

An accepted PR is merged into `next` and ships with the next release.

## 6. AI-assisted contributions

Explicitly fine. The same rules apply without exception, and you are responsible for what
you submit, however it was produced. Generated bloat is treated exactly like hand-written
bloat: rejected.

## 7. Security

CorePin changes process affinity through documented Win32 calls and needs no elevation
for normal use. If you find a security-relevant issue anyway, do not open a public issue
— write to the address in the maintainer's GitHub profile.
