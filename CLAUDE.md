# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

`AGENTS.md` is the binding working agreement for this repo — read it. `docs/PRD.md` is the
authoritative, living checklist of every control and feature; when code and PRD disagree, the PRD
wins unless it is being revised in the same change. Tick a PRD box only when the work is implemented
**and** tested.

## Commands

```sh
dotnet build NativeForms.sln -c Release        # build (CI runs exactly this)
dotnet test  NativeForms.sln -c Release        # all tests (NUnit 4, headless — no display needed)
dotnet test  NativeForms.sln -c Release --filter "FullyQualifiedName~DataGridViewTests"   # one fixture
dotnet run --project NativeForms.Demo          # run the demo (GTK 3 on Linux, native on Windows)

# NativeAOT publish — the headline goal; CI fails on any IL2xxx/IL3xxx warning
dotnet publish NativeForms.Demo/NativeForms.Demo.csproj -c Release -r linux-x64 \
  --self-contained -p:PublishAot=true -p:TreatWarningsAsErrors=true
```

The loop: build + test green → commit → push → wait for CI (ubuntu/windows/macos + per-platform AOT
publish). New behavior is TDD-driven by the PRD's acceptance boxes: test first. Stable releases are
manual (`gh workflow run release.yml`) — never cut one unless explicitly asked.

## What this is

A cross-platform C# (.NET 10) UI toolkit with a Windows Forms-shaped API (`Form`, `Button`,
`Controls`, `Click`) that renders through real platform-native widgets, and owner-draws the controls
no platform offers natively — in that platform's own theme. Must stay fast, tiny (kilobytes), and
trim/NativeAOT compatible. Namespace root is `Hawkynt.NativeForms`.

## Architecture

**Core / peer split.** `NativeForms.Core` is strictly platform-agnostic — it never touches a native
API. Each `Control` drives an `IControlPeer` (the native side: an HWND, a GtkWidget*, …) created via
`IPlatformBackend` (`NativeForms.Core/Backends/`). Backends live only in
`NativeForms.Backends.Windows` (Win32), `.Gtk` (GTK 3), `.MacOS` (placeholder). Apps register
backends through `BackendRegistry`; `IsSupported` picks the right one at runtime.

**Buffered-then-flushed realization.** A `Control` holds its state in managed fields until it is
*realized* (`Control.RealizeSelf`), at which point the peer is created and buffered state is flushed
into the native widget; later property writes forward immediately via `_peer?.Set…`. There is no
shadow tree, and child realization is lazy, driven by the owning `Form`. This is the core footprint
mechanism — don't add per-control caches or eager native objects.

**Owner-draw layer.** Controls without a native widget (DataGridView, ListView, CheckBox, …) derive
from `OwnerDrawnControl`, which realizes onto a single per-backend `ICanvasPeer` and exposes
WinForms-style `OnPaint`/`OnMouse…`/`OnKey…` overrides. Painting goes through `IGraphics` (GDI on
Win32, Cairo/Pango on GTK) using `ITheme` — OS-queried colors/font/metrics — so custom controls
still look native. Write such a control once in Core; it runs on every backend.

**MVVM without reflection.** `Hawkynt.NativeForms.ComponentModel` provides `ObservableObject`,
`RelayCommand`/`RelayCommand<T>`, `ObservableList<T>`, and `PropertyBinding<T>` — all
delegate/selector based, never reflection based.

**Tests are headless.** `NativeForms.Tests/Fakes/HeadlessBackend.cs` implements the full backend
contract with a recording canvas/graphics, so control logic, paint output, and input handling are
asserted without a display. `AllocationBudgetTests` enforces per-instance allocation budgets
(unrealized control < 512 B, owner-drawn < 768 B) — new fields on `Control` cost budget.

## Hard rules (from AGENTS.md — enforced, not aspirational)

- **AOT/trim safety:** `[LibraryImport]` only, never `[DllImport]`. Native callbacks are
  `[UnmanagedCallersOnly]` statics passed as function pointers; managed state comes back via static
  maps/`GCHandle`, never captured closures or marshalled delegates. No `System.Reflection`,
  `TypeDescriptor`, `Activator.CreateInstance(Type)`, or dynamic — anywhere.
- **Footprint:** value-type geometry, null event slots until subscribed, no per-frame allocation on
  the paint path, no LINQ on hot paths. Budget details in `docs/PRD.md` §4.
- **Commits:** one control/feature/concern per commit, referencing the relevant PRD box. Subject
  lines start with `+` added · `-` removed · `*` changed · `#` bug fixed · `!` critical todo — never
  "fix"/"changed"/"modified". No AI attribution anywhere: no `Co-Authored-By` AI lines, no
  "Generated with" footers, no agent mentions in messages, comments, or docs.
- **README frame:** title → badges → `>` blockquote; `## ❤️ Support` and `## 📜 License` close the
  file and stay intact. LGPL-3.0-or-later; no per-file license headers in `.cs` files.
