# Agent guide — NativeForms

Working agreement for **all** coding agents (Claude Code, Codex, Copilot, …) and human contributors
working in this repository. These rules are not optional. The full house spec lives in the
`Hawkynt/project-template` repo (`STANDARD.md`); this file is the per-repo distillation.

## What this is

A cross-platform **C# (.NET 10) UI toolkit** with a Windows Forms-shaped API. The window and the
text-bearing primitives (`Form`, `Button`, `Label`, `TextBox`, `RichTextBox`) are real platform
widgets driven via `[LibraryImport]` P/Invoke; controls without a faithful platform counterpart are
owner-drawn in that platform's own theme. **Windows (Win32) and Linux (GTK 3) are the mature shipping
backends; macOS (Cocoa/AppKit) is implemented and exercised by native CI probes/screenshots, but still
has platform-specific gaps documented in `docs/backends.md`.** It must stay **fast**, **tiny** (bytes,
not megabytes) and **trim/NativeAOT compatible**.

Solution layout — project folders at the repo root:
- `NativeForms.Core` — platform-agnostic controls, layout, events, data-binding (`Hawkynt.NativeForms`).
- `NativeForms.Backends.Windows` / `.Gtk` — the mature Win32 and GTK 3 native backends.
- `NativeForms.Backends.MacOS` — the Cocoa/AppKit backend with native peers, dialogs, menus and CI smoke/screenshot coverage; see `docs/backends.md` for known differences.
- `NativeForms.Generators` — Roslyn source generator (`[GridEditable]` → `PopulateGrid`), packed as an
  analyzer asset inside the Core NuGet package.
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

## Sourcing an implementation

Never write a format, codec, cipher or compression scheme out of your own understanding when
somebody has already got it right. Work **down** this ladder, stop at the first rung that applies,
and say in the commit body which rung you used and why the ones above it did not.

**1 — Licence-compatible source you can take.** MIT, BSD, Apache-2.0, LGPL, public domain: anything
this repository's LGPL-3.0-or-later can absorb. Search for it before writing anything. There are two
ways to take it and the choice is not cosmetic:

- **Vendor it** — a verbatim subtree under `Vendored/<Library>/` next to its own `LICENSE.txt`, kept
  in the upstream's own formatting. Do *not* restyle it: the whole point is that the next upstream
  version still applies cleanly, and a reformatted copy conflicts on every update. Keep it out of
  the published API surface with the `exclude-namespace` input of the `package-readme` action rather
  than by editing the source.
- **Convert it** — carry the algorithm across into this codebase properly. Converted code is *our*
  code, so every rule this guide sets for our own code applies to it, including the current C#
  language version (C# 14) wherever that says the same thing more plainly. Do not restate those
  rules here or anywhere else: one stale copy of them is how this guide spent years asking for a
  brace style the code had never used. A conversion that still reads like C, or like a decompiler's
  output, is not finished.

Either way, record where it came from — a `THIRD_PARTY_NOTICES.md` in the package, or a
`THIRD-PARTY-NOTICE.<Name>.txt` beside the code. Attribution is a licence term, not a courtesy.

**2 — Licence-incompatible source: use it, but not its code.** GPL where we ship LGPL, anything
proprietary, anything with no licence at all. Read it and *build material from it*: a written
specification, a set of test cases, and a third-party oracle you can run to produce expected output.
Then implement from that derived material. Do not paste it, do not transliterate it line by line,
and do not carry its file layout or its identifier names across — that is still the same copy.

**Constants are not expression.** Tables, S-boxes, magic numbers, CRC polynomials, Huffman code
tables, quantisation matrices, window and filter coefficients: copy them exactly, from whichever
source is authoritative, on every rung of this ladder. A re-derived S-box is simply a wrong S-box,
and a table somebody worked out for themselves is the defect that nothing catches until real files
arrive. Where a value is arbitrary-but-agreed, matching it *is* the specification.

**3 — Original reference material.** The specification, the standard (RFC, ITU-T, ISO, ECMA), the
academic paper, the vendor's own documentation, the format author's write-up. Prefer the normative
text over anybody's description of it; where the two disagree, the normative text wins and the
disagreement is worth a comment.

**4 — Other trusted sources.** Reverse-engineering write-ups, articles and blog posts by named
people with a track record, and long-lived project wikis that cite their evidence.

**5 — Untrusted material, by agreement only.** Forum answers, unattributed gists, wiki edits with no
provenance. Only when nothing above exists, and only where several *independent* sources agree —
majority vote, discounting the ones that plainly copied each other. Treat the result as a hypothesis
and mark it as one in the code.

Whatever rung you land on, the finished implementation is judged the same way: it must agree with an
oracle or with real files, not merely compile and look plausible. When a licence-incompatible
implementation was your oracle, keep the comparison as a test wherever it can run, and where it
cannot, commit the captured expected output with a note saying what produced it.

## README & repo conventions

- Standard frame: title → badges → one-line `>` blockquote; body; `## ❤️ Support` and `## 📜 License`
  close the file. License is LGPL-3.0-or-later; the `## ❤️ Support` section and `.github/FUNDING.yml`
  stay intact. No per-file license headers in `.cs` files.
