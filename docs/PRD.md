# NativeForms — Product Requirements & Implementation Checklist

> A fast, tiny, trim/AOT-compatible UI toolkit with a Windows Forms-shaped API that renders through
> platform-native widgets (Win32, GTK, Cocoa) via P/Invoke — and paints the controls no platform
> offers natively in that platform's own visual style.

This document is the **authoritative, living checklist**. Every control and feature is tracked here
with `[ ]` / `[x]` boxes. When code and this document disagree, this document wins unless it is being
revised in the same change. Keep boxes honest: a box is `[x]` only when implemented **and** covered
by a test (and, for visuals, verified on the target platform). Beyond the box, a feature counts as
**finished** only when it is also shown in the demo gallery and documented under `docs/` — §11
tracks that coverage per feature.

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
   (one-way, two-way, to-source, one-time; scalar and list binding; converters, default values, null-replace values).
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
 ├─ .Drawing                       (owner-draw abstraction: IGraphics, ITheme, geometry)
 └─ .Backends                      (IPlatformBackend + peer interfaces)
Hawkynt.NativeForms.Backends.Windows   (Win32/user32/comctl32/uxtheme via [LibraryImport])
Hawkynt.NativeForms.Backends.Gtk       (GTK 3 via [LibraryImport])
Hawkynt.NativeForms.Backends.MacOS     (Cocoa/AppKit via objc_msgSend — placeholder)
```

- **Core** never calls a native API. It creates **peers** through `IPlatformBackend` and drives the
  message loop through `IPlatformBackend.Run`.
- **Peers** are the native side of a control (`IControlPeer`, `IWindowPeer`, `IButtonPeer`, …). They
  buffer state before realization and flush it when the native widget is created.
- **Owner-drawn controls** (`.Drawing`) render onto an `IGraphics` surface the backend exposes for
  a "canvas" peer, using an `ITheme` that reports the OS accent color, control background,
  selection color, font, and standard metrics so custom controls look native.

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
| **MVVM** | `ObservableObject` view-models, `RelayCommand`/`RelayCommand<T>` (`ICommand`), and `PropertyBinding<T>` two-way binding between VM properties and control properties. `[ ]` lambda binding sugar layer. |
| **MVC** | Controls raise events; a controller mediates model↔view. Provided by the plain event surface + one-way `PropertyBinding` from model to view. |
| **MVP** | Views expose interfaces (`interface IFooView`); a presenter drives them. NativeForms controls are interface-friendly (events + properties); `[ ]` ship a small `IView`/passive-view sample. |

- [x] `ObservableObject` (INotifyPropertyChanging/Changed, `SetProperty`)
- [x] `RelayCommand`, `RelayCommand<T>`
- [x] `PropertyBinding<T>` (OneWay / TwoWay / OneWayToSource / OneTime), reflection-free
- [x] `BindingList<T>` replacement: `ObservableList<T>` (IList<T> + granular `ListChanged`), reflection-free
- [ ] Lambda binding sugar over `PropertyBinding<T>` — string-free, reflection-free:
      `label.Bind(vm, nameof(vm.Count), v => v.Display, (c, text) => c.Text = text)` plus two-way
      overloads. The WinForms string API (`DataBindings.Add("Text", vm, "Name")`) is a **non-goal**:
      it needs reflection. Plain `Func<,>`/`Action<,>` delegates only — no `Expression<>` trees
      (interpreted under NativeAOT)
- [ ] `ICommand` wiring on `Button`/`ToolStripButton`/menu items (auto enable/disable via `CanExecute`)
- [~] List/selection binding: `ListBox.DataSource` + reflection-free `DisplaySelector`/`ImageSelector`
      done; `DataGridView.DataSource`/`Columns` + reflection-free `ValueSelector`/`ImageSelector`
      (one-way, cell-level) done; `ComboBox`/`ListView` + `ValueMember`/`SelectedValue` pending

---

## 4. Performance & footprint budget

Targets (measured by the `NativeForms.Benchmarks` project; treat as CI-guarded goals):

- [x] `Control` instance overhead budget enforced by an allocation test (`AllocationBudgetTests`,
      `GC.GetAllocatedBytesForCurrentThread`): unrealized control **< 512 B**, owner-drawn **< 768 B**.
- [x] **Trim/AOT can't regress**: per-platform NativeAOT publish in CI runs with
      `-p:TreatWarningsAsErrors=true`, so any IL2xxx/IL3xxx warning fails the build. Demo AOT binary is
      **~1.6 MB** self-contained (whole app + runtime); size reported in CI every run.
- [x] Append to a bound `ObservableList<T>` with no listener allocates **0 bytes** (null-conditional
      short-circuits the event args) — asserted.
- [ ] Empty `Form` realized: **< 8 KB** managed allocation beyond the native window.
- [ ] Zero per-frame managed allocation for owner-drawn controls in steady state (no GC in scroll).
- [x] No boxing on the event hot path; `EventHandler` slots are null until subscribed.
- [ ] Startup (cold) to first window shown: **< 50 ms** on a trimmed self-contained build.
- [ ] Backend assemblies: only the registered backend is linked (verified by trim size test).
- [ ] Data structures: prefer `struct`, `Span<T>`, pooled buffers; avoid LINQ on hot paths.
- [ ] Benchmark harness + regression thresholds wired into `nightly.yml`.

Design rules that serve the budget: buffered-then-flushed peer state (no shadow trees), lazy child
realization, `Rectangle`/`Point`/`Size` value types for geometry, and no reflection metadata cache.

---

## 5. Owner-draw & theming (the "looks native even when we draw it" layer)

- [x] `IGraphics` surface: lines, rects, text (native font, aligned), images/icons, clip stack.
      Backed by **GDI** (Win32) and **Cairo/Pango** (GTK); CoreGraphics (Cocoa) pending.
- [x] `ITheme`: accent, window/control/field background, text/disabled/selection colors, default font,
      row height, scrollbar size — queried from the OS (`GetSysColor`/`SPI_GETNONCLIENTMETRICS` on
      Win32; `GtkStyleContext`/`gtk-font-name` on GTK); `DefaultTheme` fallback for headless/tests.
- [x] `ICanvasPeer` + `OwnerDrawnControl`: one paintable/focusable native surface per backend, so
      every custom control is written once and runs on any backend. Mouse/key/focus + paint plumbed.
- [x] Decoder-free `IImage` (32-bit ARGB) so controls show icons without an image library.
- [ ] Light/dark mode + high-contrast follow-the-OS, with change notifications.
- [ ] Per-monitor DPI awareness; logical↔device pixel mapping.
- [ ] Double-buffered canvas peer; invalidation regions; hit-testing helpers.
- [x] `DrawEllipse`/`FillEllipse` (GDI `Ellipse`, Cairo unit-circle) — [ ] rounded rects/pills still pending.
- [ ] Native-style primitives drawn via theme: push button, radio, combo arrow, header cell,
      grid line, scrollbar (or reuse native scrollbars where possible), selection highlight.
- [~] Shared icon+text content layout helper (`ContentLayout` + `TextImageRelation`): pure
      geometry, matrix-tested; adopted by CheckBox/RadioButton/GroupBox caption/PictureBox and
      the native Button/Label peers (platform limits documented: Win32 button/label and GTK label
      render image-only or text-only, not both); mnemonic-aware layout and item-cell adoption
      pending.

---

## 6. Data binding internals — `[ ]` planned beyond the primitive

- [x] `PropertyBinding<T>` primitive (delegates, no reflection).
- [ ] **Lambdas everywhere**: every binding/configuration surface (bindings, column value/image/
      style selectors, read-only predicates, display-text/tooltip providers) accepts plain
      `Func<>`/`Action<>` lambdas — never string member names, never `Expression<>` trees.
- [ ] Source-generated `[Bindable]`/property-accessor generator so `DataSource`+`DisplayMember`
      resolve member getters at **compile time** (keeps list binding reflection-free/AOT-safe).
- [~] `ObservableList<T>` with granular change events (add/remove/replace/reset) for virtualized
      list controls done; `Move` change type + `IReadOnlyObservableList<T>` pending.
- [ ] Format/parse converters (`IValueConverter`-style, as delegate pairs) for two-way text↔value.
- [ ] Binding fallbacks: default value when the source is unset, null-replace value when the
      source yields `null` (per binding, reflection-free).
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
- [x] `Timer` (WinForms-shaped, `ITimerPeer`: WM_TIMER on Win32, `g_timeout` on GTK, fireable
      headless fake; deferred arm until a backend runs) — drives marquee/caret/tooltip/autorepeat
- [x] Popup surface (`IPopupPeer : ICanvasPeer`: WS_POPUP + capture light-dismiss on Win32,
      `GTK_WINDOW_POPUP` + seat/GTK grabs; `ShowAt`/`Hide`/`Dismissed`; `Control.PointToScreen`)
      — hosts ComboBox drop-downs, menus, tooltips, calendar fly-outs
- [ ] `Component`/`IContainer` + designer-free component model
- [ ] `Cursor`, `Cursors`; `Control.Cursor`
- [ ] Focus model (`Focus()`, `TabIndex`, `TabStop`, `Enter`/`Leave`/`GotFocus`/`LostFocus`)
- [ ] Keyboard (`KeyDown`/`KeyUp`/`KeyPress`, mnemonics/accelerators)
- [ ] Mouse (`MouseDown`/`Up`/`Move`/`Enter`/`Leave`/`Wheel`, `DoubleClick`)
- [ ] `Font`, `ForeColor`, `BackColor`, `Padding`, `Margin`, `Anchor`, `Dock`
- [ ] Layout engine: anchoring, docking, `AutoSize`, `TableLayoutPanel`/`FlowLayoutPanel` semantics

### 7.2 Top-level & containers
- [x] **Nested child realization** — `IContainerPeer` (window + every canvas peer) hosts children;
      `Control.RealizeSelf` realizes recursively with parent-relative coordinates; late
      `Controls.Add` realizes immediately; `Remove`/`Clear` dispose the peer tree and the control
      re-realizes from buffered state
  - [ ] `IContainerPeer.RemoveChild` so Win32/GTK containers drop their bookkeeping entry for a
        removed child before the container itself dies (today the stale entry is harmless)
- [~] `Form` (native) — title, client area, close event, Show *(realize/show done; below pending)*
  - [x] Realize + show + close event
  - [x] `StartPosition` (core-side against `GetScreenSize`/owner), `FormBorderStyle` (live
        Win32 style toggling, GTK resizable/decorated/type hints), `WindowState` with peer
        write-back sync, `MinimizeBox`/`MaximizeBox` (GTK advisory)
  - [~] `MinimumSize`/`MaximumSize` (WM_GETMINMAXINFO / geometry hints) + `Resize`/`SizeChanged`
        with echo-free peer write-back done; `AcceptButton`/`CancelButton` Enter/Escape routing
        blocked on the §7.1 focus model
  - [~] `ShowDialog()` modal + `DialogResult` (nested native loops, owner disable/transient,
        `AcceptButton`/`CancelButton` properties; Enter/Escape routing blocked on §7.1 focus model)
  - [x] `MdiParent`/MDI — documented non-goal in the `Form` remarks
  - [x] Icon (raw-ARGB `SetIcon`, decoder-free), `TopMost`, `Opacity` (compositor-dependent on Linux)
- [~] `Panel` (owner) — background + `BorderStyle` (None/FixedSingle/Fixed3D) done; `AutoScroll` pending
- [~] `GroupBox` (owner) — themed frame + caption, caption image (icon before the text in the
      frame gap), real nested children done; child inset/layout convenience pending
- [~] `TabControl` / `TabPage` (owner, themed header strip; pages host real nested children)
  - [x] Tab headers with **icon + text** (`ImageList` + per-page `ImageIndex`), accent underline
        on the active tab, hover feedback
  - [~] Header overflow scroll arrows done; `Alignment` bottom/left/right pending
  - [x] `SelectedIndex`/`SelectedTab`, `SelectedIndexChanged`, keyboard nav (Ctrl+Tab wrap,
        arrows), content area auto-applied to pages on resize/switch
  - [ ] Optional per-tab close button (modern tabbed-UI affordance)
- [x] `SplitContainer` (owner) — horizontal/vertical orientation, live splitter drag +
      `SplitterMoved`, min-size clamps, keyboard splitter movement
- [~] `FlowLayoutPanel` (all four `FlowDirection`s, `WrapContents`, `Control.Margin`,
      AutoScroll interplay) and `TableLayoutPanel` (absolute/percent/auto-size styles, spans,
      auto-placement, cell borders) done; grid auto-grow, invisible-child skip, Anchor/Dock
      interplay pending
- [x] `Panel.AutoScroll` — themed scrollbars (shared `ScrollBarRenderer`), wheel + thumb drag,
      children scrolled via the logical→peer bounds mapping seam (`AutoScrollPosition`)

### 7.3 Buttons & simple inputs
- [~] `Button` (native) — click, text *(done: click/text/bounds/enable/visible)*
  - [~] [x] `DialogResult` (click walks to the owning Form, closes modal); [ ] default/accept
        styling, [ ] `FlatStyle` pending
  - [~] Image (`Image`/`ImageAlign`/`TextImageRelation` peer surface): GTK full image+text
        (`gtk_button_set_image` + position); Win32 `BM_SETIMAGE`/`BS_BITMAP` image-only (classic
        BUTTON cannot render both — documented); owner-drawn image+text fallback pending
- [~] `CheckBox` (owner) — `Checked` + `CheckedChanged`, click/Space toggle, themed checkmark done;
      image + text via `ContentLayout` done; tri-state `CheckState` pending
- [~] `RadioButton` (owner) — themed ring + accent dot, grouping by container, click/Space,
      `CheckedChanged`, image + text via `ContentLayout` done
- [~] `Label` (native) — polish done, images pending
  - [x] Text
  - [x] `AutoSize` (canvas-free `IPlatformBackend.MeasureText`), `TextAlign`, `BorderStyle`
        (Win32 style bit; GTK documented no-op), mnemonic rendering (`&x` underline, GTK `_x`
        translation)
  - [ ] Mnemonic activation focuses the next control (blocked on the §7.1 focus model)
  - [~] `Image` + `ImageAlign`: peer surface done — Win32 `SS_BITMAP` and GTK widget-swap render
        image-only when the caption is empty (image+text is platform-limited, documented)
- [~] `LinkLabel` (owner) — whole-text link: accent color + underline, hover + `Visited` states,
      click/Space → `LinkClicked`; per-character `LinkArea` ranges pending
- [~] `TextBox` (native: Win32 EDIT / GTK GtkEntry + GtkTextView-in-ScrolledWindow)
  - [x] Single-line editing, `TextChanged` (echo-guarded two-way sync, once per user edit)
  - [x] `Multiline` (vertical scrollbar; live flip recreates the widget and re-flushes buffered
        text/selection, same control id so `WM_COMMAND` routing survives)
  - [x] `PlaceholderText` (single-line cue banner: `EM_SETCUEBANNER` /
        `gtk_entry_set_placeholder_text`)
  - [x] `PasswordChar`/`UseSystemPasswordChar`, `ReadOnly`, `MaxLength` (GTK: entry only —
        GtkTextView has no native limit, documented), `CharacterCasing` (core-side, all backends)
  - [x] Selection API (`SelectionStart`/`SelectionLength`/`SelectedText`), buffered → live
  - [ ] Owner-drawn grey placeholder for multiline (no native support in EDIT or GtkTextView)
  - [ ] `AcceptsReturn`/`AcceptsTab` key behavior (`WM_GETDLGCODE`), word-wrap control, undo API
- [x] `MaskedTextBox` (core mask engine over the native TextBox: 0/9/L/?/A/a/&/C + literals +
      escapes, `PromptChar`, transactional whole-text validation with revert, `MaskCompleted`,
      `MaskedTextChanged`, raw-text extraction; whole-text transitions documented — no per-key
      caret steering yet)
- [ ] `RichTextBox` (native where available / owner) — character styles (bold/italic/underline/
      strikeout, `SelectionColor`/`SelectionFont`), paragraph alignment + indent, bullet lists,
      auto-detected links + `LinkClicked`, `PlaceholderText`, RTF subset load/save (`Rtf`,
      `LoadFile`/`SaveFile`), zoom

### 7.4 Lists & selection
- [x] `ListBox` (owner) — items, per-item icons, wheel/keyboard scroll, `DataSource` binding,
      `SelectionMode` (None/One/MultiSimple/MultiExtended: Ctrl/Shift click + keyboard, sorted
      `SelectedIndices`, anchor ranges, caret via `FocusedIndex`)
- [x] `CheckedListBox` (owner) — per-item check state over the ListBox engine (`ItemCheck`
      veto-able before the flip, `CheckOnClick`, Space toggles selection, shared `CheckGlyph`
      with CheckBox, check states survive item mutation)
- [~] `ComboBox` (owner field + popup drop-down in native theme) — `DropDownList` and `DropDown`
      (hosted native TextBox editor), **items with icons** (shared ListBox row painter, pixel-
      identical rows), `PlaceholderText`, full keyboard model (Alt+Down/F4, closed-arrow
      selection, prefix cycling open and closed), light-dismiss popup sized by
      `MaxDropDownItems`, `DataSource` + `DisplaySelector`/`ValueSelector`/`SelectedValue`
      (lambda-shaped DisplayMember/ValueMember) done; `Simple` style and autocomplete pending
      (autocomplete needs key events on `ITextBoxPeer`)
- [~] `ListView` (owner, native metrics) — Details + List views, columns (`Width`/`TextAlign`),
      per-item icons, sub-items, single selection, header row, wheel/keyboard scroll, virtualized
      paint done; LargeIcon/SmallIcon/Tile views, groups, checkboxes, virtual-mode item API, label
      editing, sorting and multi-selection pending
- [~] `TreeView` (owner) — nodes with expand/collapse (themed +/− glyphs, cancelable
      Before/After pipeline), per-node icons (`ImageIndex`/`SelectedImageIndex` via `ImageList`,
      lazily materialized), checkboxes (`AfterCheck`, shared `CheckGlyph`), full keyboard nav
      (arrows walk rows, Right into child, Left to parent, +/−/*/Space), `EnsureVisible`,
      virtualized paint over the lazily re-flattened visible-node list (100k nodes bounded) done;
      label editing (TextBox overlay) and state images pending
- [~] `TreeListView` (owner) — TreeView × ListView-Details hybrid done: hierarchy in the first
      column + selector-driven sub-item columns (`TreeListViewColumn`), shared engine pieces
      factored (`ITreeNodeHost`, `TreeRowList`, `TreeNavigation`, `HeaderRowPainter`,
      `ExpandGlyph` — glyphs pixel-identical to TreeView), per-node icons, full keyboard parity,
      virtualized at 100k nodes, `SetDataSource` with reflection-free children selector +
      cycle-bounding depth guard; column sorting, interactive column resize, label editing pending
- [~] `DataGridView` (owner) — **flagship owner-drawn control**:
  - [~] Column types (single `DataGridViewColumn` + `Kind` enum + per-kind selectors, one
        allocation-free paint switch): [x] text, [x] image, [x] image+text, [x] check (toggle via
        setter, read-only-gated), [x] button (per-cell enabled à la
        `DataGridViewDisableButtonColumn`), [x] link, [x] multi-image (per-icon click index),
        [x] progress (shared `GlyphRenderer` fill); pending: [ ] combo, [ ] numeric up-down,
        [ ] date-time picker (all three need the cell-editing overlay)
  - [x] Read-only story: grid `ReadOnly`, column `ReadOnly`, per-cell `ReadOnlyCellSelector`
        row predicate — any level wins, WinForms semantics
  - [~] Per-row/cell presentation via lambdas (the `System.Windows.Forms.Extensions` attribute
        goodies, reflection-free): [x] row back-color/height/hidden/selectable predicates,
        [x] cell style/display-text/tooltip selectors, [x] clickable cells
        (`CellClick`/`CellDoubleClick`/`CellContentClick` with model row indices — stable under
        sorting), [x] per-column `SortMode` + `SortComparison` over an index indirection
        (`Items` never mutated), [x] themed sort arrows; [ ] full merged rows pending
  - [~] Virtualized rows (millions of rows, constant memory) [x] done — holds with all
        presentation selectors active (bounded-ops test at 100k); [x] column resize (header
        divider drag + `AllowUserToResizeColumns` + per-column `AutoSizeMode` over the visible
        window); [ ] frozen columns pending
  - [~] [x] `DataSource`/`ObservableList<object?>` one-way binding via reflection-free `ValueSelector` done;
        [ ] cell editing, [ ] validation, [ ] formatting pending
  - [~] [x] full-row selection, [x] keyboard nav (Up/Down/PageUp/PageDown/Home/End), [x] header rendering
        in native theme done; [ ] sorting, [ ] extra selection modes, [ ] clipboard copy/paste pending
  - [~] [x] alternating row styles done; [ ] per-cell styles, [ ] DPI + dark mode pending
  - [~] [x] Row headers (`ShowRowHeaders`/`RowHeaderWidth`, current-row marker), [x] column
        auto-size modes; [ ] column drag-reorder pending
  - [x] Vertical virtualization (paints only the visible row range); [ ] horizontal scrollbar
        (`HorizontalOffset` shift exists; interactive scrollbar) pending

### 7.5 Range & date
- [x] `TrackBar` (owner) — Min/Max/Value, `TickFrequency` ticks, horizontal/vertical, themed
      groove + accent fill + thumb, track paging + thumb scrub, Win32 key directions
- [x] `NumericUpDown` / `DomainUpDown` (owner spinner + hosted native TextBox editor) — decimal
      clamping/`Increment`/`DecimalPlaces`, domain matching + `Wrap`, themed spin buttons with
      timer-driven autorepeat (shared `AutoRepeat` engine); commit points documented (no focus
      model yet)
- [x] `HScrollBar`/`VScrollBar` (owner) — proportional thumb, channel paging, arrow autorepeat,
      Win32 `Maximum − LargeChange + 1` semantics, `Scroll` vs `ValueChanged` split
  - [ ] Unify the two internal scrollbar renderers (`Drawing.ScrollBarRenderer` used by
        `Panel.AutoScroll` vs the `ScrollBar` control's own) into one implementation
- [x] `MonthCalendar` (owner) — `CalendarCore` engine (title + nav arrows, `FirstDayOfWeek`
      header, 6×7 grid with leading/trailing greying, today accent, single + Shift/drag range
      selection capped by `MaxSelectionCount`, `Min`/`MaxDate` clamps, full keyboard set incl.
      Ctrl+Page year paging, wheel month paging)
- [~] `DateTimePicker` (owner field + popup calendar sharing `CalendarCore`) — Long/Short/Time/
      Custom invariant formats, `ShowCheckBox`/`Checked` greying, closed Up/Down day stepping,
      Alt+Down/F4, commit/cancel semantics done; `BoldedDates`, per-part focus, time spinner
      editing pending
- [x] `ProgressBar` (owner) — determinate (Min/Max/Value, accent fill), `Style.Marquee`
      (timer-driven sweep, allocation-free per tick), `Step`/`PerformStep`, vertical orientation

### 7.6 Menus, toolbars, status
- [~] `MenuStrip` / `ToolStripMenuItem` — owner-drawn via popup peers on all backends (one
      `MenuDropDown` engine drives menus, context menus, drop-down buttons, overflow); the
      native menu bar mapping (Win32 `HMENU`, `GtkMenuBar`, NSMenu) is a tracked follow-up
  - [x] Items with **icon + text**, separators, nested cascading submenus
  - [x] Checked and radio-checked items (`CheckOnClick`, `CheckedGroup`)
  - [~] Keyboard: mnemonics rendered + activated while the bar/menu has focus; shortcut keys
        (`Keys` chords) displayed and dispatched via `ProcessShortcut`; form-wide routing and
        Alt bar activation blocked on the §7.1 focus model
  - [x] `ICommand` wiring (auto enable/disable via `CanExecute`)
- [~] `ContextMenuStrip` — `Control.ContextMenuStrip` + right-click at the cursor on
      owner-drawn controls; native-widget controls pending (peer right-click events)
- [x] `ToolStrip` (owner) — icon+text buttons, toggles, separators,
      `ToolStripDropDownButton`/`ToolStripSplitButton`, overflow chevron popup, `ImageList`
- [x] `StatusStrip` — `ToolStripStatusLabel` (incl. `Spring`), embedded progress item (shared
      renderer), size grip
- [~] `ToolTip` — owner-drawn controls with Initial/AutoPop delays via `Timer` + popup done;
      native-widget controls and per-item tips in lists/trees/grids pending

### 7.7 Media & misc
- [x] `PictureBox` (owner) — `Image`, `SizeMode` (Normal/Stretch/Center/Zoom letterbox), `BorderStyle`
- [~] `ImageList` (icon storage shared by list/tree/combo/toolbar) — pre-realization storage done:
      holds ARGB pixel data with no backend present, materializes `IImage`s lazily per backend and
      caches per index (`ImageList.GetImage`), fixed `ImageSize`, dispose drops native bitmaps but
      keeps pixels; pending: wiring into controls (`ImageIndex`/`ImageKey`), badge overlays (§7.9)
- [~] `NotifyIcon` (tray) — Win32 `Shell_NotifyIconW` with message-only callback window done;
      GTK throws (GtkStatusIcon deprecated; StatusNotifier/D-Bus is the tracked follow-up)
- [ ] `WebBrowser/WebView` (native host) — likely later / optional
- [ ] `PropertyGrid` (owner) — later

### 7.8 Dialogs (native common dialogs)
- [x] `MessageBox.Show` (buttons/icons mapped; `MessageBoxW` / `GtkMessageDialog`)
- [~] `OpenFileDialog` / `SaveFileDialog` — WinForms filter syntax, `FilterIndex`, `Multiselect`
      (`GetOpenFileNameW` family / `GtkFileChooserDialog`); `FilterIndex` write-back pending
- [~] `FolderBrowserDialog` (`SHBrowseForFolderW` / GTK select-folder); Win32 initial-directory
      hook pending
- [x] `ColorDialog`, `FontDialog` (`ChooseColorW`/`ChooseFontW` / GTK choosers with Pango
      round-trip)
- [ ] `PrintDialog`/printing (later / optional)

### 7.9 Modern extras (controls users expect today; owner-drawn, native-themed)
- [x] `ToggleSwitch` (owner) — themed pill + thumb, accent when on, optional caption, click/Space (snap, no animation yet)
- [x] `SplitButton` / `DropDownButton` (owner) — shared `DropDownButtonBase` over the MenuDropDown engine; SplitButton gates its main action through `ICommand`
- [x] `Expander` (owner) — collapsible header (themed triangle + caption, click/Space) +
      content whose child peers hide while collapsed; height restores on expand
- [~] `SearchBox` — hosted native TextBox + magnifier glyph + clear (×) with `SearchCleared`; in-editor Enter commit pending a peer key seam
- [x] Badge/overlay support on `ImageList` images (`AddBadged`: integer alpha-over composition, corner anchoring)

---

## 8. Cross-cutting features
- [ ] DPI awareness & scaling (per-monitor v2 on Windows, GDK scale, backing-scale on macOS)
- [ ] Dark mode / high contrast, live theme-change notifications
- [ ] Accessibility (UIA on Windows, ATK/AT-SPI on GTK, NSAccessibility on macOS)
- [ ] Right-to-left layout
- [ ] Localization of built-in strings (dialog buttons etc.)
- [ ] Drag & drop, clipboard
- [ ] `ImageList`/icon decoding without heavy image libs (small PNG/ICO decoder or native)
- [ ] **Uniform image API across all controls**: either a direct `Image` property (Button, Label,
      CheckBox, RadioButton, GroupBox, PictureBox) or `ImageList` + `ImageIndex`/`ImageKey`
      (ComboBox, ListBox, ListView, TreeView, TreeListView, TabControl, toolbars) — same pattern,
      same rendering path (§5 shared icon+text layout), everywhere
- [ ] Threading: UI-thread affinity, `Control.Invoke`/`BeginInvoke`, `SynchronizationContext`

---

## 9. Quality gates
- [~] Unit tests (NUnit 4) for platform-agnostic behavior — model, realization, registry, binding,
      owner-drawn control paint/input (53 tests); grows with each control.
- [x] Headless backend for tests (`HeadlessBackend` + recording `ICanvasPeer`/`RecordingGraphics`) so
      control paint and input are testable without a display.
- [x] Trim + AOT publish of the demo in CI on each OS with trim warnings as errors (headline goal).
- [x] Footprint regression thresholds via `AllocationBudgetTests` (runs every CI, all OSes).
- [ ] Per-platform smoke tests / screenshots for owner-drawn controls.
- [~] **Demo gallery**: `NativeForms.Demo` shows every shipped control with representative property
      settings; every new control lands with a gallery section (coverage tracked in §11).
- [~] **Reference documentation**: every shipped control/subsystem has a page under `docs/`
      (usage example + API tables + notes); the README links into the docs and carries the control
      index (coverage tracked in §11).

---

## 10. Milestones (the completion roadmap)

Every §7 box belongs to a milestone below, except items marked "later / optional" inline
(WebBrowser/WebView, PropertyGrid, printing, MDI) — those are decided when their milestone
neighborhood ships.

- **M0 — Foundation.** Core control model, backend abstraction, native Win32 + GTK
  Button/Label/Form, macOS placeholder, MVVM primitives + binding, demo, tests, CI. `[~]`
- **M1 — Input & layout.** Focus/keyboard/mouse plumbing on every control, Font/colors/Padding,
  `Cursor`/`Cursors`, `Component`/`IContainer`, anchor/dock + `TableLayoutPanel`/
  `FlowLayoutPanel`, Label polish (AutoSize/TextAlign/mnemonics), TextBox (single-line →
  multiline → `PlaceholderText` hint). `[~]` (CheckBox/RadioButton/Panel/GroupBox base done)
- **M2 — Owner-draw & theming.** `IGraphics`/`ITheme`/`ICanvasPeer`/`OwnerDrawnControl`, GDI + Cairo
  canvas peers, native themes, decoder-free icons, headless canvas + allocation budgets. `[~]`
  (remaining: shared icon+text layout helper (§5), rounded rects, double buffering, dark-mode
  notifications, LinkLabel)
- **M3 — Lists & selection.** ListBox multi-selection, ComboBox (icons in drop-down, placeholder,
  popup, autocomplete, value binding), CheckedListBox. `[~]`
- **M4 — Grids & trees.** DataGridView (cell editing, sorting, resize, more column types), ListView
  (icon/tile views, groups, checkboxes, sorting), **TreeView**, **TreeListView**. `[~]`
- **M5 — Containers & tabs.** TabControl/TabPage (incl. icon tab headers), SplitContainer,
  `Panel.AutoScroll`, Expander. `[ ]`
- **M6 — Images everywhere.** `ImageList` + `ImageIndex`/`ImageKey` pattern, image + text on
  Button/Label/CheckBox/RadioButton/GroupBox caption/tab headers (§8 uniform image API),
  PictureBox, small PNG/ICO decoding. `[ ]`
- **M7 — Text & value editors.** RichTextBox, MaskedTextBox, NumericUpDown/DomainUpDown,
  TrackBar, ScrollBars, ProgressBar marquee, DateTimePicker, MonthCalendar. `[ ]`
- **M8 — Chrome & dialogs.** MenuStrip/ContextMenuStrip, ToolStrip (incl. SplitButton/
  DropDownButton), StatusStrip, ToolTip, NotifyIcon, MessageBox + file/folder/color/font dialogs,
  ToggleSwitch/SearchBox extras. `[ ]`
- **M9 — Platform polish.** Accessibility, per-monitor DPI, live dark-mode/high-contrast, RTL,
  drag & drop/clipboard, threading (`Control.Invoke`), macOS (Cocoa) backend. `[ ]`

Each milestone: tests first (TDD, per house rule), green `dotnet build`/`dotnet test -c Release`
before commit, semantic single-concern commits with the `+ - * # !` prefix, no AI traces anywhere.

---

## 11. Coverage matrix — tested · demo-ed · documented

A §7 box may be `[x]` (implemented + unit-tested) while the feature is still invisible to users.
This matrix tracks the rest of "done": a section in the `NativeForms.Demo` gallery and a reference
page under `docs/`. Every change that ships a control/feature extends this table in the same commit.
`—` = not applicable.

| Feature | Tests | Demo | Docs |
|---|---|---|---|
| Architecture (core/peer/realization) | ✔ | — | [architecture.md](architecture.md) |
| `Application` + `BackendRegistry` | ✔ | ✔ | [controls/application.md](controls/application.md) |
| `Control` base | ✔ | ✔ | [controls/control.md](controls/control.md) |
| `Form` | ✔ | ✔ | [controls/form.md](controls/form.md) |
| `Button` | ✔ | ✔ | [controls/button.md](controls/button.md) |
| `Label` | ✔ | ✔ | [controls/label.md](controls/label.md) |
| `Panel` | ✔ | ✔ | [controls/panel.md](controls/panel.md) |
| `GroupBox` | ✔ | ✔ | [controls/groupbox.md](controls/groupbox.md) |
| `CheckBox` | ✔ | ✔ | [controls/checkbox.md](controls/checkbox.md) |
| `RadioButton` | ✔ | ✔ | [controls/radiobutton.md](controls/radiobutton.md) |
| `ProgressBar` | ✔ | ✔ | [controls/progressbar.md](controls/progressbar.md) |
| `ListBox` | ✔ | ✔ | [controls/listbox.md](controls/listbox.md) |
| `ListView` | ✔ | ✔ | [controls/listview.md](controls/listview.md) |
| `DataGridView` | ✔ | ✔ | [controls/datagridview.md](controls/datagridview.md) |
| MVVM primitives + binding | ✔ | ✔ | [mvvm.md](mvvm.md) |
| Owner-draw engine (`IGraphics`/`ITheme`/canvas) | ✔ | ✔ | [custom-controls.md](custom-controls.md) |

Known gallery gaps (limited by pending §7 boxes, not by the gallery): no icons anywhere
(`IImage` requires a resolved backend — see the `ImageList` box in §7.7), containers shown as
empty frames (nested child realization, §7.2), single radio group (grouping is per-container and
the gallery is flat).
