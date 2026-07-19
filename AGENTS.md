# Agent guide — NativeForms

Working agreement for **all** coding agents (Claude Code, Codex, Copilot, …) and human contributors
working in this repository. These rules are not optional. The full house spec lives in the
`Hawkynt/project-template` repo (`STANDARD.md`); this file is the per-repo distillation.

## What this is

A cross-platform **C# (.NET 8) UI toolkit** with a Windows Forms-shaped API that renders through
platform-native widgets (Win32, GTK, Cocoa) via `[LibraryImport]` P/Invoke, and owner-draws the
controls no platform offers natively — in that platform's own theme. It must stay **fast**, **tiny**
(bytes, not megabytes) and **trim/NativeAOT compatible**.

Solution layout — project folders at the repo root:
- `NativeForms.Core` — platform-agnostic controls, layout, events, data-binding (`Hawkynt.NativeForms`).
- `NativeForms.Backends.Windows` / `.Gtk` / `.MacOS` — one native backend each (macOS is a placeholder).
- `NativeForms.Demo` — a runnable sample (`WinExe`) and the AOT publish target.
- `NativeForms.Tests` — NUnit 4 tests; a headless backend makes control logic testable without a display.

**`docs/PRD.md` is the authoritative, living checklist** of every control and feature. When code and
the PRD disagree, the PRD wins unless it is being revised in the same change. Tick a box only when the
work is implemented **and** tested (and, for visuals, verified on the target platform).

## Design rules (first-class, not aspirational)

- **AOT/trim safety:** `[LibraryImport]` only (never `[DllImport]`); native callbacks are
  `[UnmanagedCallersOnly]` statics passed as function pointers, managed state recovered via static
  maps / `GCHandle`, never captured closures or marshalled delegates. No `System.Reflection`,
  `TypeDescriptor`, `Activator.CreateInstance(Type)`, or dynamic. `IsAotCompatible=true` stays green.
- **Native-first, owner-draw to match:** wrap the native widget when it exists; otherwise draw it
  ourselves using the OS theme colors/metrics/fonts so it still looks native.
- **Footprint discipline:** buffered-then-flushed peer state (no shadow trees), lazy child
  realization, value-type geometry, null event slots until subscribed, no per-frame allocation on the
  paint path. See the budget in `docs/PRD.md` §4.
- **Core stays platform-agnostic** — it only ever talks to `IPlatformBackend`/peers. Native code
  lives exclusively in the backend projects.

## Commits

- **Group changes semantically/logically** — one control/feature/concern per commit; reference the
  relevant `docs/PRD.md` box in the body.
- **Every subject line starts with a prefix**: `+` added · `-` removed · `*` changed · `#` bug fixed ·
  `!` critical todo. Never start a subject with "fix"/"bugfix"/"changed"/"modified".
- **No AI traces anywhere**: no `Co-Authored-By` AI lines, no "Generated with" footers, no agent
  mentions in messages, comments, or authorship.

## The loop (always, in this order)

1. **Before committing**: `dotnet build NativeForms.sln -c Release` and
   `dotnet test NativeForms.sln -c Release` until green (CI runs the same on ubuntu + windows +
   macOS). New behavior is driven by the PRD's acceptance boxes — add the test first (TDD).
2. **Commit** (rules above) and **push**.
3. **Wait for CI**; on `main` a green CI triggers the nightly (prerelease + GFS prune, same-day
   replace). Fix and loop until everything is green.

Stable releases are **manual** (`gh workflow run release.yml`) — never cut one unless explicitly asked.

## README & repo conventions

- Standard frame: title → badges → one-line `>` blockquote; body; `## ❤️ Support` and `## 📜 License`
  close the file. License is LGPL-3.0-or-later; the `## ❤️ Support` section and `.github/FUNDING.yml`
  stay intact. No per-file license headers in `.cs` files.
