# NativeForms — Product Requirements & Implementation Checklist

> A fast, tiny, trim/AOT-compatible UI toolkit with a Windows Forms-shaped API that renders through
> platform-native widgets (Win32, GTK, Cocoa) via P/Invoke — and paints the controls no platform
> offers natively in that platform's own visual style.

This document is the **authoritative, living checklist**. Every control and feature is tracked here
with `[ ]` / `[x]` boxes. When code and this document disagree, this document wins unless it is being
revised in the same change. Keep boxes honest: a box is `[x]` only when implemented **and** covered
by a test (and, for visuals, verified on the target platform).

Status legend: `[ ]` not started · `[~]` partial · `[x]` done & tested · `native` = wraps a native
widget · `owner` = we draw it ourselves in the native theme.

---

## 1. Vision & goals

1. **Source-level WinForms familiarity.** Public API mirrors `System.Windows.Forms` in a distinct
   namespace (`Hawkynt.NativeForms`) — porting is largely a namespace swap. We match names, members
   and semantics where they make sense; we do **not** aim for 100% binary compatibility.
2. **Native first, owner-drawn to match.** If the platform has the widget, we wrap it (it looks and
   behaves exactly like the user's desktop). If it doesn't (DataGridView, rich ListView, icon
   ComboBox drop-downs, …), we draw it ourselves using the platform's **theme colors, metrics and
   fonts** so it still looks native.
3. **Trim & NativeAOT compatible.** No reflection-based serialization, no `TypeDescriptor`
   data-binding, no runtime code-gen. `IsAotCompatible=true` on every library; the analyzers must
   stay green.
4. **Bytes, not megabytes.** Aggressive memory discipline (see §4). A small form should cost
   kilobytes of managed state, not megabytes.
5. **Pattern-agnostic.** First-class support for **MVVM, MVC and MVP** and every data-binding flavor
   (one-way, two-way, to-source, one-time; scalar and list binding).
6. **One binary, every platform** — or trim to a single platform for the smallest footprint. The
   app chooses which backends it registers.

### Non-goals
- Binary/ABI compatibility with real WinForms (drop-in DLL replacement).
- The full `System.Drawing.Common` GDI+ surface. We define our own minimal drawing abstraction.
- WinForms Designer (`.resx`/`.Designer.cs` codegen) — may come later, not a v1 concern.
- Web/mobile targets.

---

## 2. Architecture layers

```
Hawkynt.NativeForms                (Core: controls, layout, events, App)   [platform-agnostic]
 ├─ .ComponentModel                (ObservableObject, RelayCommand, bindings)
 ├─ .Drawing                       (owner-draw abstraction: IGraphics, ITheme, geometry)   [planned]
 └─ .Backends                      (IPlatformBackend + peer interfaces)
Hawkynt.NativeForms.Backends.Windows   (Win32/user32/comctl32/uxtheme via [LibraryImport])
Hawkynt.NativeForms.Backends.Gtk       (GTK 3 via [LibraryImport])
Hawkynt.NativeForms.Backends.MacOS     (Cocoa/AppKit via objc_msgSend — placeholder)
```

- **Core** never calls a native API. It creates **peers** through `IPlatformBackend` and drives the
  message loop through `IPlatformBackend.Run`.
- **Peers** are the native side of a control (`IControlPeer`, `IWindowPeer`, `IButtonPeer`, …). They
  buffer state before realization and flush it when the native widget is created.
- **Owner-drawn controls** (planned `.Drawing`) render onto an `IGraphics` surface the backend
  exposes for a "canvas" peer, using an `ITheme` that reports the OS accent color, control
  background, selection color, font, and standard metrics so custom controls look native.

### AOT/interop rules (enforced, not aspirational)
- `[LibraryImport]` source-generated P/Invoke only — never `[DllImport]`.
- Native callbacks (WndProc, GTK signal handlers) are `[UnmanagedCallersOnly]` static methods passed
  as function pointers; managed state is recovered via static maps or `GCHandle`, never captured
  closures or marshalled delegates.
- No `System.Reflection`, `TypeDescriptor`, `Activator.CreateInstance(Type)`, or dynamic in Core or
  backends. Binding uses compiled delegates (see §6).

---

## 3. Patterns: MVVM / MVC / MVP

| Pattern | How NativeForms supports it |
|---|---|
| **MVVM** | `ObservableObject` view-models, `RelayCommand`/`RelayCommand<T>` (`ICommand`), and `PropertyBinding<T>` two-way binding between VM properties and control properties. `[ ]` `Control.DataBindings` sugar layer. |
| **MVC** | Controls raise events; a controller mediates model↔view. Provided by the plain event surface + one-way `PropertyBinding` from model to view. |
| **MVP** | Views expose interfaces (`interface IFooView`); a presenter drives them. NativeForms controls are interface-friendly (events + properties); `[ ]` ship a small `IView`/passive-view sample. |

- [x] `ObservableObject` (INotifyPropertyChanging/Changed, `SetProperty`)
- [x] `RelayCommand`, `RelayCommand<T>`
- [x] `PropertyBinding<T>` (OneWay / TwoWay / OneWayToSource / OneTime), reflection-free
- [ ] `BindingList<T>` replacement: `ObservableList<T>` (IList<T> + `ListChanged`), reflection-free
- [ ] `Control.DataBindings.Add("Text", vm, nameof(vm.Name), TwoWay)` convenience over `PropertyBinding`
- [ ] `ICommand` wiring on `Button`/`ToolStripButton`/menu items (auto enable/disable via `CanExecute`)
- [ ] List/selection binding for `ListBox`/`ComboBox`/`ListView`/`DataGridView` (`DataSource`, `DisplayMember`, `ValueMember`, `SelectedValue`) — reflection-free member access via source-generated accessors or caller-supplied selectors

---

## 4. Performance & footprint budget

Targets (measured by the `NativeForms.Benchmarks` project; treat as CI-guarded goals):

- [ ] Empty `Form` realized: **< 8 KB** managed allocation beyond the native window.
- [ ] `Control` instance overhead: **≤ 64 bytes** managed for an unrealized control (excluding text).
- [ ] Zero per-frame managed allocation for owner-drawn controls in steady state (no GC in scroll).
- [ ] No boxing on the event hot path; `EventHandler` slots are null until subscribed.
- [ ] Startup (cold) to first window shown: **< 50 ms** on a trimmed self-contained build.
- [ ] Backend assemblies: only the registered backend is linked (verified by trim size test).
- [ ] Data structures: prefer `struct`, `Span<T>`, pooled buffers; avoid LINQ on hot paths.
- [ ] `[ ]` Benchmark harness + regression thresholds wired into `nightly.yml`.

Design rules that serve the budget: buffered-then-flushed peer state (no shadow trees), lazy child
realization, `Rectangle`/`Point`/`Size` value types for geometry, and no reflection metadata cache.

---

## 5. Owner-draw & theming (the "looks native even when we draw it" layer) — `[ ]` planned

- [ ] `IGraphics` surface: lines, rects, rounded rects, text (with native font), images/icons, clip,
      DPI-aware transforms. Backed by GDI (Win32), Cairo/GDK (GTK), CoreGraphics (Cocoa).
- [ ] `ITheme`: accent color, window/control/field background, text/disabled/selection colors,
      default font & sizes, focus-ring style, standard paddings/metrics — queried from the OS
      (uxtheme/`GetSysColor`, GTK `GtkStyleContext`, `NSColor`/`NSAppearance`).
- [ ] Light/dark mode + high-contrast follow-the-OS, with change notifications.
- [ ] Per-monitor DPI awareness; logical↔device pixel mapping.
- [ ] Double-buffered canvas peer; invalidation regions; hit-testing helpers.
- [ ] Native-style primitives drawn via theme: push button, check, radio, combo arrow, header cell,
      grid line, scrollbar (or reuse native scrollbars where possible), selection highlight.

---

## 6. Data binding internals — `[ ]` planned beyond the primitive

- [x] `PropertyBinding<T>` primitive (delegates, no reflection).
- [ ] Source-generated `[Bindable]`/property-accessor generator so `DataSource`+`DisplayMember`
      resolve member getters at **compile time** (keeps list binding reflection-free/AOT-safe).
- [ ] `ObservableList<T>` + `IReadOnlyObservableList<T>` with granular change events (add/remove/
      move/replace/reset) for virtualized list controls.
- [ ] Format/parse converters (`IValueConverter`-style) for two-way text↔value.
- [ ] Validation hooks (`INotifyDataErrorInfo`-style), error surfacing on controls.
- [ ] Binding to nested paths (`a.b.c`) via chained typed selectors.

---

## 7. Control inventory & checklist

Per control, the sub-boxes are the acceptance criteria. `native`/`owner` marks the intended
strategy (may differ per platform; note exceptions inline).

### 7.1 Foundation
- [x] `Application` (`Run`, `Exit`, backend selection)
- [x] `Control` base (Bounds/Location/Size/Left/Top/Width/Height, Visible, Enabled, Text, Parent,
      Controls, Click/TextChanged, realize/peer lifecycle)
- [x] `ControlCollection`
- [x] Backend abstraction (`IPlatformBackend`, peer interfaces, `BackendRegistry`)
- [ ] `Component`/`IContainer` + designer-free component model
- [ ] `Cursor`, `Cursors`; `Control.Cursor`
- [ ] Focus model (`Focus()`, `TabIndex`, `TabStop`, `Enter`/`Leave`/`GotFocus`/`LostFocus`)
- [ ] Keyboard (`KeyDown`/`KeyUp`/`KeyPress`, mnemonics/accelerators)
- [ ] Mouse (`MouseDown`/`Up`/`Move`/`Enter`/`Leave`/`Wheel`, `DoubleClick`)
- [ ] `Font`, `ForeColor`, `BackColor`, `Padding`, `Margin`, `Anchor`, `Dock`
- [ ] Layout engine: anchoring, docking, `AutoSize`, `TableLayoutPanel`/`FlowLayoutPanel` semantics

### 7.2 Top-level & containers
- [~] `Form` (native) — title, client area, close event, Show *(realize/show done; below pending)*
  - [x] Realize + show + close event
  - [ ] `StartPosition`, `FormBorderStyle`, `WindowState` (min/max/normal), `MinimizeBox`/`MaximizeBox`
  - [ ] `MinimumSize`/`MaximumSize`, resize events, `AcceptButton`/`CancelButton`
  - [ ] `ShowDialog()` modal + `DialogResult`
  - [ ] `MdiParent`/MDI (or documented non-goal)
  - [ ] Icon, `TopMost`, `Opacity`
- [ ] `Panel` (native container / owner) — borders, `AutoScroll`
- [ ] `GroupBox` (native/owner)
- [ ] `TabControl` / `TabPage` (native where available, else owner)
- [ ] `SplitContainer` / `Splitter`
- [ ] `FlowLayoutPanel`, `TableLayoutPanel` (owner/layout)
- [ ] `ToolStripContainer`, `Panel` scrolling

### 7.3 Buttons & simple inputs
- [~] `Button` (native) — click, text *(done: click/text/bounds/enable/visible)*
  - [ ] `DialogResult`, default/accept styling, image + text, `FlatStyle`
- [ ] `CheckBox` (native) — `Checked`, `CheckState`, tri-state, `CheckedChanged`
- [ ] `RadioButton` (native) — grouping by container, `CheckedChanged`
- [ ] `Label` (native) *(basic exists; add `AutoSize`, `TextAlign`, mnemonics)*
  - [x] Text
  - [ ] `AutoSize`, `TextAlign`, `BorderStyle`, mnemonic → focuses next control
- [ ] `LinkLabel` (owner)
- [ ] `TextBox` (native) — `Multiline`, `PasswordChar`, `ReadOnly`, `MaxLength`, selection, `TextChanged`
- [ ] `MaskedTextBox` (owner over native TextBox)
- [ ] `RichTextBox` (native where available / owner)
- [ ] `NumericUpDown` (native/owner)
- [ ] `DomainUpDown`

### 7.4 Lists & selection
- [ ] `ListBox` (native) — items, `SelectionMode`, data binding, owner-draw items
- [ ] `CheckedListBox` (native/owner)
- [ ] `ComboBox` (native) — `DropDown`/`DropDownList`/`Simple`, **items with icons** (owner-drawn
      drop-down in native style), `DataSource`/`DisplayMember`/`ValueMember`, autocomplete
- [ ] `ListView` (owner, native metrics) — Details/LargeIcon/SmallIcon/List/Tile views, columns,
      `ImageList` icons, checkboxes, sorting, groups, virtual mode, editing labels
- [ ] `TreeView` (owner/native) — nodes, `ImageList`, checkboxes, expand/collapse, editing, virtual
- [ ] `DataGridView` (owner) — **flagship owner-drawn control**:
  - [ ] Column types: text, check, button, combo, image, link
  - [ ] Virtualized rows (millions of rows, constant memory), row/column resize, frozen columns
  - [ ] Cell editing, validation, formatting, `DataSource` binding (`ObservableList<T>`)
  - [ ] Sorting, selection modes, clipboard copy/paste, keyboard nav, header rendering in native theme
  - [ ] Alternating row styles, per-cell styles, DPI + dark mode

### 7.5 Range & date
- [ ] `TrackBar` (native/owner)
- [ ] `ProgressBar` (native) — determinate/marquee
- [ ] `ScrollBar` (`HScrollBar`/`VScrollBar`) (native)
- [ ] `DateTimePicker` (native/owner)
- [ ] `MonthCalendar` (owner)

### 7.6 Menus, toolbars, status
- [ ] `MenuStrip` / `ToolStripMenuItem` (native menu bar where available, else owner)
- [ ] `ContextMenuStrip`
- [ ] `ToolStrip` / `ToolStripButton` / separators / dropdowns (owner in native theme)
- [ ] `StatusStrip` / `ToolStripStatusLabel`
- [ ] `ToolTip`

### 7.7 Media & misc
- [ ] `PictureBox` (owner) — `SizeMode`, image formats
- [ ] `ImageList` (icon storage shared by list/tree/combo/toolbar)
- [ ] `NotifyIcon` (tray)
- [ ] `WebBrowser/WebView` (native host) — likely later / optional
- [ ] `PropertyGrid` (owner) — later

### 7.8 Dialogs (native common dialogs)
- [ ] `MessageBox.Show`
- [ ] `OpenFileDialog` / `SaveFileDialog`
- [ ] `FolderBrowserDialog`
- [ ] `ColorDialog`, `FontDialog`
- [ ] `PrintDialog`/printing (later / optional)

---

## 8. Cross-cutting features
- [ ] DPI awareness & scaling (per-monitor v2 on Windows, GDK scale, backing-scale on macOS)
- [ ] Dark mode / high contrast, live theme-change notifications
- [ ] Accessibility (UIA on Windows, ATK/AT-SPI on GTK, NSAccessibility on macOS)
- [ ] Right-to-left layout
- [ ] Localization of built-in strings (dialog buttons etc.)
- [ ] Drag & drop, clipboard
- [ ] `ImageList`/icon decoding without heavy image libs (small PNG/ICO decoder or native)
- [ ] Threading: UI-thread affinity, `Control.Invoke`/`BeginInvoke`, `SynchronizationContext`

---

## 9. Quality gates
- [ ] Unit tests (NUnit 4) for every platform-agnostic behavior (layout math, binding, model).
- [ ] Headless backend for tests (a `FakeBackend`/`HeadlessBackend` recording peer calls) so control
      logic is testable without a display. `[ ]`
- [ ] Per-platform smoke tests / screenshots for owner-drawn controls.
- [ ] Trim + AOT publish of the demo in CI on each OS (proves the headline goal).
- [ ] Footprint/benchmark regression thresholds (nightly).

---

## 10. Milestones

- **M0 — Foundation (current).** Core control model, backend abstraction, native Win32 + GTK
  Button/Label/Form, macOS placeholder, MVVM primitives + binding, demo, tests, CI. `[~]`
- **M1 — Input & layout.** Focus/keyboard/mouse, Font/colors, anchor/dock + TableLayout/FlowLayout,
  TextBox, CheckBox, RadioButton, Panel/GroupBox. `[ ]`
- **M2 — Owner-draw & theming.** `IGraphics`/`ITheme`, headless backend, LinkLabel/PictureBox, dark
  mode. `[ ]`
- **M3 — Lists.** ListBox/ComboBox(+icons)/CheckedListBox, list binding + `ObservableList<T>`. `[ ]`
- **M4 — DataGridView + ListView + TreeView** (virtualized, bound, editable). `[ ]`
- **M5 — Menus/toolbars/status/dialogs.** `[ ]`
- **M6 — Accessibility, DPI polish, macOS (Cocoa) backend.** `[ ]`

Each milestone: tests first (TDD, per house rule), green `dotnet build`/`dotnet test -c Release`
before commit, semantic single-concern commits with the `+ - * # !` prefix, no AI traces anywhere.
