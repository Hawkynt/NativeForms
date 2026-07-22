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
| **MVVM** | `ObservableObject` view-models, `RelayCommand`/`RelayCommand<T>` (`ICommand`), and `PropertyBinding<T>` two-way binding between VM properties and control properties, `BindingExtensions.Bind` lambda sugar, converters, fallbacks, chained paths. |
| **MVC** | Controls raise events; a controller mediates model↔view. Provided by the plain event surface + one-way `PropertyBinding` from model to view. |
| **MVP** | Views expose interfaces (`interface IFooView`); a presenter drives them. NativeForms controls are interface-friendly (events + properties); `[ ]` ship a small `IView`/passive-view sample. |

- [x] `ObservableObject` (INotifyPropertyChanging/Changed, `SetProperty`)
- [x] `RelayCommand`, `RelayCommand<T>`
- [x] `PropertyBinding<T>` (OneWay / TwoWay / OneWayToSource / OneTime), reflection-free
- [x] `BindingList<T>` replacement: `ObservableList<T>` (IList<T> + granular `ListChanged`), reflection-free
- [x] Lambda binding sugar over `PropertyBinding<T>` (`BindingExtensions.Bind`, exact PRD shape,
      discard-safe lifetime rooted in the source's `PropertyChanged` list). The WinForms string
      API (`DataBindings.Add("Text", vm, "Name")`) stays a **non-goal** (reflection); plain
      delegates only, no `Expression<>` trees
- [x] `ICommand` wiring: `ToolStripItem.Command`, `SplitButton.Command` and `Button.Command`
      (+ `CommandParameter`) — `Enabled` follows `CanExecute`/`CanExecuteChanged`
- [~] List/selection binding: `ListBox.DataSource` + reflection-free `DisplaySelector`/`ImageSelector`
      done; `DataGridView.DataSource`/`Columns` + reflection-free `ValueSelector`/`ImageSelector`
      (cell-level, now with setter-based editing) done; `ComboBox.ValueSelector`/`SelectedValue`
      done; `ListView.SetDataSource` (snapshot + row factory) done

---

## 4. Performance & footprint budget

Targets (measured by the `NativeForms.Benchmarks` project; treat as CI-guarded goals):

- [x] `Control` instance overhead budget enforced by an allocation test (`AllocationBudgetTests`,
      `GC.GetAllocatedBytesForCurrentThread`), in three tiers: unrealized control **< 512 B**,
      owner-drawn single surface **< 768 B**, hosted-editor composite **< 1024 B**. The third tier is
      not a relaxation but a measurement: a shell that hosts a native `TextBox` pays for the editor
      (~296 B alone), the child collection holding it and the delegates wiring its text/key events
      back, so `SearchBox` (864 B), `NumericUpDown` (936 B), `DomainUpDown` (952 B), `FolderPicker`
      (936 B) and `FilePicker` (984 B) all sit above 768 B by construction. The blanket 768 B claim
      was never true for that family and never asserted for it; all five are now pinned. `FilePicker`
      is the family's ceiling with ~40 B of headroom — it subscribes to three editor events
      (text, key, focus-loss) where `SearchBox` needs two — so the next field added to a picker
      needs a re-measure, not a nudge to the limit.
- [x] **Trim/AOT can't regress**: per-platform NativeAOT publish in CI runs with
      `-p:TreatWarningsAsErrors=true`, so any IL2xxx/IL3xxx warning fails the build. Demo AOT binary is
      **~1.6 MB** self-contained (whole app + runtime); size reported in CI every run.
- [x] Append to a bound `ObservableList<T>` with no listener allocates **0 bytes** (null-conditional
      short-circuits the event args) — asserted.
- [x] Empty `Form` realized: **< 8 KB** managed allocation (measured ~920 B, asserted).
- [x] Zero per-frame managed allocation in steady state — asserted for EVERY owner-drawn
      control (31-control sweep incl. open menus); per-frame sort-map closure and per-cell
      display-text recomputation in the grid were found and removed (display text now cached
      per row, invalidated on data/selector changes).
- [x] No boxing on the event hot path; `EventHandler` slots are null until subscribed.
- [ ] Startup (cold) to first window shown: **< 50 ms** on a trimmed self-contained build.
- [x] Backend linking: `NativeForms.TrimProbe` (GTK-only) publishes without the Windows/macOS
      assemblies (asserted in nightly CI); AOT probe 1.4 MB vs 2.4 MB all-backends demo.
- [x] Data structures: Span at the pixel/text seams, LINQ eliminated from all paint/input/
      layout paths (one cold-path exception message kept), gesture-scoped transients audited —
      pooling rejected as unwarranted at current sizes.
- [x] `NativeForms.Benchmarks` (dependency-free Stopwatch harness: construction ns+bytes,
      realize, paints/s, 100k-row scroll) + nightly job with 2× regression thresholds and JSON
      artifact; trim job alongside.

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
- [x] Light/dark mode + high-contrast follow-the-OS: `IPlatformBackend.ThemeChanged`
      (WM_THEMECHANGED family / GtkSettings notify), theme-cache invalidation, realized
      owner-drawn controls repaint, `ITheme.IsHighContrast`.
- [~] DPI: `GetDpiScale` + `Control.LogicalToDevice` groundwork done; per-monitor
      rescale-on-move pending.
- [x] Double-buffered Win32 canvas (memory-DC blit; GTK cairo-buffered by design),
      invalidation regions honored end-to-end, `HitTest` helper; steady-state repaint allocates
      0 bytes (asserted) after de-allocating the GDI/Pango paint paths (cached brushes/pens/
      fonts/layouts, reused graphics + event args).
- [x] `DrawEllipse`/`FillEllipse` and `Draw`/`FillRoundedRectangle` (GDI `RoundRect`,
      Cairo arc paths) — ToggleSwitch pill is one rounded rect.
- [x] Native-style primitives drawn via theme (`GlyphRenderer`): push button face, check/radio,
      progress fill, sort arrow, row marker, combo arrow, header cell, focus ring, selection
      highlight — adopted across the owner-drawn controls; scrollbars via the shared renderers.
- [~] Shared icon+text content layout helper (`ContentLayout` + `TextImageRelation`): pure
      geometry, matrix-tested; adopted by CheckBox/RadioButton/GroupBox caption/PictureBox and
      the native Button/Label peers (platform limits documented: Win32 button/label and GTK label
      render image-only or text-only, not both); mnemonic-aware layout and item-cell adoption
      pending.

---

## 6. Data binding internals — `[ ]` planned beyond the primitive

- [x] `PropertyBinding<T>` primitive (delegates, no reflection).
- [x] **Lambdas everywhere**: every binding/configuration surface accepts plain `Func<>`/`Action<>`
      lambdas — `ValueSelector`, `ImageSelector`/`ImageIndexSelector`/`ImagesSelector`,
      `CellStyleSelector`, `ReadOnlyCellSelector`, `EnabledSelector`, `DisplaySelector`,
      `TooltipSelector` — never string member names, never `Expression<>` trees (none exist in Core;
      enforced by the no-reflection rule, exercised by the binding and column-type tests).
- [ ] Source-generated `[Bindable]`/property-accessor generator so `DataSource`+`DisplayMember`
      resolve member getters at **compile time** (keeps list binding reflection-free/AOT-safe).
- [~] `ObservableList<T>` with granular change events (add/remove/replace/reset) for virtualized
      list controls done; `Move` change type + `IReadOnlyObservableList<T>` pending.
- [x] Format/parse converters: `PropertyBinding<TSource, TTarget>` delegate pairs, two-way.
- [x] Binding fallbacks (`BindingFallback<T>`): default value when the source read throws,
      null-replacement when it yields `null` — source→target path, per binding, reflection-free.
- [~] Validation hooks: `ObservableObject.SetError`/`GetError`/`ErrorsChanged` + per-binding
      `onError` callback done; full `INotifyDataErrorInfo` + built-in control error visuals
      deliberately out (display is the app's callback)
- [x] Binding to nested paths via chained typed selectors (`BindingPath.Chain`, re-subscribes on middle-object swap; one-way).

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
- [x] `Component`/`IContainer` designer-free model (Timer/ToolTip/NotifyIcon/ContextMenuStrip are components; `Container` disposes in reverse order)
- [x] `Cursor`/`Cursors` + ambient `Control.Cursor` (WM_SETCURSOR / gdk named cursors; LinkLabel hand)
- [x] Focus model — `Focus()`/`Focused`/`CanFocus`, `TabIndex`/`TabStop` (defaults follow the
      control kind), WinForms event order (Enter→GotFocus / LostFocus→Leave with container-chain
      crossing), `Form.ActiveControl` + initial focus, Tab/Shift+Tab navigation through nested
      containers, `IsInputKey` claims, form-wide menu shortcuts + Alt-mnemonic bar activation
- [~] Keyboard — `KeyDown`/`KeyUp`/`KeyPress` on owner-drawn surfaces, mnemonics/accelerators
      via the form dialog-key chain done; native-widget key preview (peer key seam: Enter inside
      a native TextBox → AcceptButton, native Tab handling, button-mnemonic clicks) pending
- [~] Mouse events on `Control`: `MouseMove`/`MouseEnter`/`MouseLeave` ride the shared pointer
      channel for every control (native widgets and owner-drawn surfaces alike);
      `MouseDown`/`MouseUp`/`MouseWheel`/`MouseDoubleClick`/`DoubleClick` fire for owner-drawn controls
      (double-click detected in core from press timing + slop). Native widgets consume their own
      button/wheel events, so those do not surface for them — the same platform limit as native key
      preview. Event slots ride the lazy pointer relay, so an unhooked control keeps its footprint.
- [x] `Font`/`ForeColor`/`BackColor` (ambient chain, one lazy `AppearanceState`, peer forwarding
      + owner-drawn adoption), `Padding` (+`DisplayRectangle`), `Margin`, `Anchor`, `Dock`
      (flag-packed, zero-cost defaults)
- [~] Layout engine: anchoring (per-edge deltas against the container's display rectangle),
      docking (Controls-order edge claiming + Fill remainder), `Suspend`/`ResumeLayout`,
      free adoption by every plain container, TLP in-cell Dock/Anchor done; generalized
      `AutoSize` and flow-row cross-axis anchoring pending

### 7.2 Top-level & containers
- [x] **Nested child realization** — `IContainerPeer` (window + every canvas peer) hosts children;
      `Control.RealizeSelf` realizes recursively with parent-relative coordinates; late
      `Controls.Add` realizes immediately; `Remove`/`Clear` dispose the peer tree and the control
      re-realizes from buffered state
  - [x] `IContainerPeer.RemoveChild` — `Controls.Remove`/`Clear` tell the container peer to drop its
        bookkeeping entry (GTK canvas child list, Win32 id→peer map) before the child's peer tree is
        disposed, so no container re-realizes, routes input to, or re-adds a gone peer
- [~] `Form` (native) — title, client area, close event, Show *(realize/show done; below pending)*
  - [x] Realize + show + close event
  - [x] `StartPosition` (core-side against `GetScreenSize`/owner), `FormBorderStyle` (live
        Win32 style toggling, GTK resizable/decorated/type hints), `WindowState` with peer
        write-back sync, `MinimizeBox`/`MaximizeBox` (GTK advisory)
  - [x] `MinimumSize`/`MaximumSize` (WM_GETMINMAXINFO / geometry hints) + `Resize`/`SizeChanged`
        with echo-free peer write-back; `AcceptButton`/`CancelButton` Enter/Escape routing via the
        dialog-key chain (owner-drawn reach; native-widget preview tracked in §7.1)
  - [~] `ShowDialog()` modal + `DialogResult` (nested native loops, owner disable/transient,
        `AcceptButton`/`CancelButton` properties; Enter/Escape routing blocked on §7.1 focus model)
  - [x] `MdiParent`/MDI — documented non-goal in the `Form` remarks
  - [x] Icon (raw-ARGB `SetIcon`, decoder-free), `TopMost`, `Opacity` (compositor-dependent on Linux)
- [x] `Panel` (owner) — background, `BorderStyle` (None/FixedSingle/Fixed3D), real nested
      children, `AutoScroll` (see the dedicated box below)
- [~] `GroupBox` (owner) — themed frame + caption, caption image (icon before the text in the
      frame gap), real nested children done; child inset/layout convenience pending
- [~] `TabControl` / `TabPage` (owner, themed header strip; pages host real nested children)
  - [x] Tab headers with **icon + text** (`ImageList` + per-page `ImageIndex`), accent underline
        on the active tab, hover feedback
  - [x] `Alignment` Top/Bottom/Left/Right — horizontal strips (top/bottom) measure tab widths from
        the captions; vertical strips (left/right) stack tabs as themed rows with horizontal captions
        (no rotated-text primitive, a documented WinForms deviation). Overflow scroll arrows follow
        the flow axis on every edge
  - [x] `SelectedIndex`/`SelectedTab`, `SelectedIndexChanged`, keyboard nav (Ctrl+Tab wrap,
        arrows), content area auto-applied to pages on resize/switch
  - [x] Optional per-tab close button (`ShowCloseButtons`): each tab paints an × the caption makes
        room for; a click raises cancelable `TabClosing` then removes the page and raises `TabClosed`
- [x] `SplitContainer` (owner) — horizontal/vertical orientation, live splitter drag +
      `SplitterMoved`, min-size clamps, keyboard splitter movement
- [x] `DockPanel` / `DockContent` (owner) — Visual-Studio-style docking manager: a lazy layout tree of
      splitter regions whose leaves are tab groups, a central document well, panes docked to any edge
      (tabbed + splittered), `Floating` panes in real top-level windows (secondary windows no longer
      quit the loop — `IWindowPeer.SetQuitsOnClose`), `AutoHide` edge strips that fly out on hover,
      drag-to-redock with the diamond overlay guides + translucent landing preview (transient overlay
      surface, allocation-free at rest), `Ctrl+Tab` document switching, caption close/float/pin
      buttons, and reflection-free `SaveLayout`/`LoadLayout` round-trip. Empty manager ≈544 B, empty
      pane ≈368 B, populated repaint 0 B/frame
- [~] `FlowLayoutPanel` (all four `FlowDirection`s, `WrapContents`, `Control.Margin`,
      AutoScroll interplay) and `TableLayoutPanel` (absolute/percent/auto-size styles, spans,
      auto-placement, cell borders, in-cell Dock/Anchor for explicitly assigned children) done;
      grid auto-grow and invisible-child skip pending
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
  - [x] Mnemonic activation focuses the next control in tab order
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
  - [~] Owner-drawn grey placeholder for multiline: GTK paints the hint after the `GtkTextView`'s own
        draw while the buffer is empty (verified in pixels); the Win32 multiline `EDIT` half is not
        wired yet
  - [~] `ITextBoxPeer.KeyDown` seam exists (Win32 EDIT subclass, GTK pre-connected
        `key-press-event`, headless fake) — wired for `SearchBox` and now for
        `NumericUpDown`/`DomainUpDown` (Enter commits the pending edit, Up/Down step from inside the
        editor). `AcceptsReturn`/`AcceptsTab`, grid-editor Enter/Escape, word-wrap control and the
        undo API are still pending.
- [x] `MaskedTextBox` (core mask engine over the native TextBox: 0/9/L/?/A/a/&/C + literals +
      escapes, `PromptChar`, transactional whole-text validation with revert, `MaskCompleted`,
      `MaskedTextChanged`, raw-text extraction; whole-text transitions documented — no per-key
      caret steering yet)
- [~] `RichTextBox` (native: Win32 RICHEDIT50W via CHARFORMAT2/PARAFORMAT2/EM_STREAM, GTK
      GtkTextView named tags; core `RichDocument` + `RtfSerializer` RTF subset as the
      platform-neutral round-trip) — character styles, `SelectionColor`/size, paragraph
      alignment, bullets, auto-links + `LinkClicked` (`EN_LINK` via `WM_NOTIFY` routing),
      `Rtf` get/set, zoom done; `LoadFile`/`SaveFile`, `PlaceholderText`, paragraph indent
      pending (GTK: literal-text bullets and code-point offsets documented)

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
- [~] `ListView` (owner, native metrics) — Details/List/LargeIcon/SmallIcon/Tile views, columns,
      per-item icons (`Large`/`SmallImageList` + `ImageIndex`), sub-items, groups (flattened
      header rows), checkboxes (`ItemCheck` veto + corner overlay in icon views), MultiExtended
      selection (ListBox engine parity), in-place sorting (`ColumnClick`, `Sorting`,
      `ItemSorter`, stable `ObservableList.Sort`), label editing (hosted TextBox, F2), header
      sort arrows, virtualized paint in every view done; virtual-mode item API and
      `ColumnHeader` change-repaint wiring (`Changed` is only observed by TreeListView) pending
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
  - [x] Column types (single `DataGridViewColumn` + `Kind` enum + per-kind selectors, one
        allocation-free paint switch): [x] text, [x] image, [x] image+text, [x] check (toggle via
        setter, read-only-gated), [x] button (per-cell enabled à la
        `DataGridViewDisableButtonColumn`), [x] link, [x] multi-image (per-icon click index),
        [x] progress (shared `GlyphRenderer` fill), [x] combo (popup list),
        [x] numeric up-down (hosted editor), [x] date-time picker (CalendarCore popup),
        [x] time picker (hosted `TimePicker`, `TimeSelector`/`TimeSetter`, per-column window and
        layout, TSV paste conversion),
        [x] masked text (hosted MaskedTextBox + per-column `Mask`), [x] domain up-down,
        [x] color (swatch + native ColorDialog edit), [x] list box (taller scrollable popup list,
        single pick via `ValueSetter` or a whole set via `SelectionMode` +
        `CheckedItemsSelector`/`CheckedItemsSetter`), [x] checked list box (popup checked list,
        vetoable `CellItemCheck`, set commit, joined closed-cell summary); radio/rating/tree cells
        deliberately out
  - [x] Read-only story: grid `ReadOnly`, column `ReadOnly`, per-cell `ReadOnlyCellSelector`
        row predicate — any level wins, WinForms semantics
  - [~] Per-row/cell presentation via lambdas (the `System.Windows.Forms.Extensions` attribute
        goodies, reflection-free): [x] row back-color/height/hidden/selectable predicates,
        [x] cell style/display-text/tooltip selectors, [x] clickable cells
        (`CellClick`/`CellDoubleClick`/`CellContentClick` with model row indices — stable under
        sorting), [x] per-column `SortMode` + `SortComparison` over an index indirection
        (`Items` never mutated), [x] themed sort arrows, [x] full merged rows
        (`FullRowTextSelector`: one full-width cell, skipped by navigation/selection/editing)
  - [~] Virtualized rows (millions of rows, constant memory) [x] done — holds with all
        presentation selectors active (bounded-ops test at 100k); [x] column resize (header
        divider drag + `AllowUserToResizeColumns` + per-column `AutoSizeMode` over the visible
        window); [x] frozen columns (leading pinned run over a clipped scrolled run)
  - [~] [x] `DataSource`/`ObservableList<object?>` one-way binding via reflection-free `ValueSelector`,
        [x] cell editing (hosted editors, F2/double-click/typing, commit/cancel semantics incl.
        scroll-out commit), [x] validation (`CellValidating` veto) done; [ ] formatting beyond
        selectors pending
  - [~] [x] full-row selection, [x] keyboard nav (Up/Down/PageUp/PageDown/Home/End), [x] header rendering
        in native theme, [x] sorting, [x] `MultiSelect` (Ctrl/Shift, display-order ranges),
        [x] clipboard copy and [x] Excel-style TSV paste (`GetClipboardText`, per-cell setter
        conversion + validation veto, `PasteCompleted`) done
  - [~] [x] alternating row styles, [x] per-cell styles (`CellStyleSelector`, see the lambda
        presentation box) done; [ ] DPI + dark mode verification pending
  - [~] [x] Row headers (`ShowRowHeaders`/`RowHeaderWidth`, current-row marker), [x] column
        auto-size modes, [x] column drag-reorder (`DisplayIndex` indirection, model `Columns`
        untouched)
  - [x] Vertical virtualization (paints only the visible row range); [x] interactive vertical +
        horizontal scrollbars embedded via the shared renderer (structurally synced with
        `TopRow`/`HorizontalOffset`), auto-shown on overflow

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
  - [x] Title drill-down (month → months of the year → years of the decade → decades of the
        century and back), shared by the `DateTimePicker` drop-down: per-level paging arrows and
        wheel, `Min`/`MaxDate` greying/bouncing at every level, keyboard (Ctrl+Up/Ctrl+Down,
        arrows, Home/End, PageUp/PageDown, Enter), allocation-free drilled-out repaint
- [x] `CalendarView` (owner) — Outlook-style appointment scheduler, distinct from the `MonthCalendar`
      picker: `Day`/`WorkWeek`/`Week`/`Month` (`CalendarViewMode`) with a time grid (configurable
      `TimeScale`, shaded `WorkDayStart`/`WorkDayEnd`, "now" line) or a month day grid with chips +
      "+n more" overflow; reflection-free `SetAppointments<T>(…, Func<T,Appointment>)` binding into a
      start-sorted snapshot; side-by-side overlap column packing; click select (`SelectionChanged`),
      double-click/Enter `AppointmentActivate`, empty-time drag `TimeRangeSelected` (`DateRangeEventArgs`),
      drag-to-move/edge-resize of a movable appointment with a live snapped ghost — cancelable
      `AppointmentMoving` then `AppointmentMoved` (`AppointmentMoveEventArgs`), the app applies + re-binds,
      per-entry lockable via `Appointment.Movable` (locked entries show a padlock and refuse to drag),
      Escape cancels; keyboard/wheel navigation; virtualized (only visible days laid out, bounded for
      100k), cached layout so populated repaints allocate zero — including a live drag preview — (empty
      ≈ 624 B, `Appointment` ≈ 48 B)
- [~] `DateTimePicker` (owner field + popup calendar sharing `CalendarCore`) — Long/Short/Time/
      Custom invariant formats, `ShowCheckBox`/`Checked` greying, closed Up/Down day stepping,
      Alt+Down/F4, commit/cancel semantics, drop-down title drill-down done; `BoldedDates` and
      per-part focus on the closed field pending
- [x] `TimePicker` (owner field + shared `Drawing.SpinnerRenderer` column) — `TimeSpan` `Value`,
      `Min`/`MaxTime` window, `ShowSeconds`/`Use24HourClock` layouts, per-part caret (click,
      Left/Right) with the spinner buttons, Up/Down and the wheel stepping the part under it,
      wrap-without-carry, timer-driven autorepeat via the shared `AutoRepeat` engine
  - [x] Double-click opens the analog `ClockFace` in a light-dismiss popup (the `IPopupPeer`
        mechanism `DateTimePicker` uses): staged hour → minute → seconds dial, live preview into
        the field clamped to `Min`/`MaxTime`, OK/Enter commit, Escape/outside-click cancel-and-revert
- [x] `ClockFace` (owner dial, reusable stand-alone or popup-hosted like `CalendarCore`) — themed
      analog picker: 12-hour ring + AM/PM toggle or two-ring 00–23 dial, minute/seconds rings snapping
      to a single unit, click/drag/keyboard, stage machine with `Committed`/`Cancelled` callbacks,
      allocation-free repaint at every stage (cached strings, shared trig table, cached hand endpoint)
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
- [x] `ImageList` (icon storage shared by list/tree/combo/tab/toolbar) — pre-realization ARGB
      storage, lazy per-backend materialization with per-index caching (`ImageList.GetImage`),
      fixed `ImageSize`, dispose drops native bitmaps but keeps pixels, badge overlays
      (`AddBadged`, §7.9); wired into ListBox/ListView/TreeView/TreeListView/ComboBox/TabControl
      via `ImageIndex`-style members (`ImageKey` string lookup not offered — indices only)
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
- [x] `Accordion` / `AccordionPane` (owner) — Outlook-style stack of collapsible panes hosting real
      nested children; `Single`/`Multiple` `ExpandMode`, `SelectedIndex`/`SelectedPane` +
      `SelectedIndexChanged`, cancelable `PaneExpanding` plus `PaneExpanded`/`PaneCollapsed`,
      keyboard header navigation (Up/Down/Home/End, Enter/Space), open panes sharing the height the
      headers leave, and collapsed panes vetoed through the child-peer seam so reopening restores
      exactly the body that was there
- [x] `Ribbon` / `RibbonTab` / `RibbonGroup` / `RibbonItem` (owner) — Office-style tab strip over
      groups drawn as labelled boxes with a bottom caption strip; `Large` items (icon over caption,
      full group height) and `Small` items stacked three per column; `RibbonButton`,
      `RibbonToggleButton` and `RibbonHostItem` (hosts a real `Control`), all deriving from
      `ToolStripItem` so `ICommand` wiring and mnemonics come for free; group overflow collapsing
      right-to-left into a `MenuDropDown` button, keyboard tab switching, and per-ribbon font-keyed
      width caches
  - [x] `Minimized` collapses the ribbon onto its tab strip (the control shrinks its own `Height` and
        raises `PreferredHeightChanged` so a plain container re-flows the content below); double-click
        a tab toggles it; while minimized a tab click floats that tab's groups as a transient
        light-dismiss flyout (reusing the `IPopupPeer` engine) anchored under the strip
  - [x] `RibbonGridButton` + `GridPicker` (owner) — Office-style table-size chooser: a themed cell
        grid, an accent-highlighted hovered `Rows`×`Columns` block, a live "C × R Table" caption,
        `RangeSelected`, keyboard navigation (arrows/Enter/Escape), zero per-frame paint allocation;
        the button opens the picker in a popup under itself, and the picker is reusable standalone
        through one shared `GridPickerCore`
  - [ ] Two-line caption wrapping on large items; Quick Access Toolbar, contextual tab groups and
        KeyTips (a `MenuStrip` above the ribbon covers the application-menu case); the tab-click
        flyout shows only item glyphs, not re-parented hosted controls; the grid picker does not grow
        past its `MaxColumns`/`MaxRows`
- [~] `SearchBox` — hosted native TextBox + magnifier glyph + clear (×) with `SearchCleared`; in-editor Enter commit pending a peer key seam
- [x] Badge/overlay support on `ImageList` images (`AddBadged`: integer alpha-over composition, corner anchoring)
- [x] `FilePicker` / `FolderPicker` (owner-drawn shell + hosted native TextBox) — shared `PathPickerBase`
      over the existing common dialogs: `SelectedPath`, `PathChanged`, `ReadOnlyText`, `PlaceholderText`,
      `PerformBrowse()`, and a `PathExists` evaluated only at commit points (Enter via
      `ITextBoxPeer.KeyDown`, focus loss, dialog result, assignment) so the paint path never stats the
      filesystem; a broken path is framed in the warning colour. `FilePicker` adds `Mode` (Open/Save),
      WinForms `Filter`/`FilterIndex`, `Multiselect` + `SelectedPaths`, `InitialDirectory`, `Title` —
      asks for the *folder* rather than the file in Save mode, since naming a file that does not
      exist yet is the point of saving, and in Open mode refuses a typed directory outright (the
      committed value is a file, never a folder — the mirror of `FolderPicker`, which stands behind a
      directory)
- [x] `IconLabel` (owner) — image **and** text in one caption through the shared `ContentLayout`
      (`TextImageRelation`, `TextAlign`, `ImageAlign`, `AutoSize`, ambient font/fore colour, RTL
      mirroring). Exists because no platform static widget renders both: Win32 `SS_BITMAP` is
      image-only and GTK swaps in a `GtkImage`, so a captioned `Label` drops its image (§7.3)
- [x] `ProgressTile` (owner) — Explorer-style tile: icon, caption, optional `SecondaryText` and a
      usage bar reusing `GlyphRenderer.DrawProgressBar`, switching to `WarningColor` past
      `WarningThreshold`; `Clickable` gates focus/hover/`Click`, `Selected` paints the selection face.
      Named for its shape, not for drives: Core is platform-agnostic and the paint path may not touch
      the filesystem, so both captions are caller-supplied strings and nothing is `DriveInfo`-bound

---

## 8. Cross-cutting features
- [~] DPI awareness & scaling — `GetDpiScale` + `Control.LogicalToDevice` groundwork done (§5);
      per-monitor v2 rescale-on-move (Windows), GDK scale and macOS backing-scale pending
- [x] Dark mode / high contrast with live theme-change notifications — see §5
- [ ] Accessibility (UIA on Windows, ATK/AT-SPI on GTK, NSAccessibility on macOS)
- [x] Right-to-left: ambient `Control.RightToLeft`, mirrored owner-drawn painting, and container
      layout mirroring — a right-to-left container flips where its children's peers sit across the
      client width while their logical `Bounds` stay left-to-right (verified in pixels)
- [x] Localization: `NativeForms.Strings` providers cover every built-in string (OS dialogs localize themselves)
- [~] Drag & drop: in-process `DoDragDrop`/`AllowDrop`/`Drag*` events (mouse-capture session,
      all backends incl. headless) done; OS-level OLE/GTK DnD pending (COM vtables excluded by
      the interop rules). Clipboard: text set/get seams done (DGV copy/paste)
- [x] `ImageDecoder`: pure-managed PNG subset (8-bit, all filters, non-interlaced) + ICO
      (PNG/32-bit/24-bit+mask entries) with `ImageList.AddPng`/`AddIco` (nearest-neighbor resample);
      encoder lives only in the test project
- [x] **Uniform image API across all controls**: a direct `Image` property (Button, Label, CheckBox,
      RadioButton, GroupBox, PictureBox) or `ImageList` + `ImageIndex`/`ImageKey` — the latter now on
      every item class (TabPage, ToolStripItem, ListViewItem, TreeNode+`SelectedImageKey`, RibbonGroup,
      AccordionPane, DockContent), resolved through the shared `ImageList.ResolveIndex`/`IndexOfKey`
      (index wins, key falls back, case-insensitive). Arbitrary-object lists (ComboBox, ListBox) reach
      the same `ImageList` through their `ImageIndexSelector`/`ImageSelector` lambdas. Same rendering
      path everywhere
- [x] Threading: loop-thread affinity, `Control.Invoke`/`BeginInvoke` (PostMessage dispatcher /
      `g_idle_add`), `NativeFormsSynchronizationContext` installed by `Run`

---

## 9. Quality gates
- [x] Unit tests (NUnit 4) for platform-agnostic behavior — model, realization, registry, binding,
      focus/keyboard, layout, appearance, threading, decoding, and every control's paint/input
      (1302 tests); grows with each control.
- [x] Headless backend for tests (`HeadlessBackend` + recording `ICanvasPeer`/`RecordingGraphics`) so
      control paint and input are testable without a display.
- [x] Trim + AOT publish of the demo in CI on each OS with trim warnings as errors (headline goal).
- [x] Footprint regression thresholds via `AllocationBudgetTests` + `PaintAllocationTests`
      (per-instance budgets, empty-Form budget, zero-allocation steady-state repaint for every
      owner-drawn control) — every CI run, all OSes; benchmark + trim jobs nightly.
- [x] **WinForms conformance audit**: every control family reviewed against `System.Windows.Forms`
      semantics (names, defaults, event order, behavioral contracts). Deviations were either fixed
      (dock order, form lifecycle, input gates, member parity, event pipelines) or documented as
      deliberate in the control's "Differences from WinForms" section.
- [x] **Demo autopilot**: `dotnet run --project NativeForms.Demo -- --autopilot` drives the whole
      gallery with injected input, asserts real control state, runs a layout audit (out-of-frame,
      truncated captions, overlaps) per page and writes in-process PNG captures
      (`gtk_widget_draw` → cairo → `cairo_surface_write_to_png`, no external tool). 90 checks;
      the pass/fail gate for "the demo works and looks right".
- [~] **Real-GTK test tier**: fixtures that drive the actual backend (in-process `GdkEvent`
      injection via `gtk_main_do_event`/`gdk_display_put_event`, `gtk_test_widget_wait_for_draw`
      before capture) and self-skip without a `DISPLAY`, so CI stays headless-green while a
      developer with a display gets real coverage — `GtkNativeSizingTests` is the first.
      XTEST is unusable here (`:1` is Xwayland `-rootless`; the compositor swallows injected
      pointer events), which is why injection happens in-process.
- [x] A focused owner-drawn control whose page is then hidden no longer **strands the GTK toplevel's
      focus widget**: `Form.ReconcileActiveControlVisibility` surrenders focus to the next focusable
      tab stop after any visibility push (all backends, headless-tested), and the GTK peer clears the
      toplevel focus when it unmaps the widget that held it so the move lands reliably. The Pickers
      walkthrough workaround was removed and the text-entry sweep passes across repeated runs.
- [ ] Per-platform smoke tests / screenshots for owner-drawn controls.
- [x] `TableLayoutPanel` now sizes and positions its tracks from `DisplayRectangle`, so cells honor
      `Padding` and never sit under a visible `AutoScroll` scrollbar — the same class of defect
      `Panel` was fixed for.
- [ ] The Win32 halves of the native-tooltip support (child window subclassing, `TOOLTIPS_CLASS`)
      are compile-verified only; they have never executed, since the sweep ran on GTK.
- [ ] **Interactive GUI verification in CI**: the headless fakes cannot see event routing,
      clipping or coordinate mapping — those bugs shipped green. A GTK harness driving real
      input (`gdk_test_simulate_*` / `gtk_main_do_event`) exists for local runs; wiring it into
      CI needs a real X server (XTEST does not land under Xwayland).
- [x] `GtkPopupPeer.IsOutside` maps the press through `XRoot`/`YRoot` and the popup's own origin,
      so a grab-redirected click no longer reads as "inside" (was latent until drop-downs began
      staying open, which made it reachable).
- [x] A drop-down now tells a grab-shadow focus-out from a genuine window-manager focus change by
      the grab itself: `GtkPopupPeer` listens for `grab-broken-event`, which fires only when an
      external grab (Alt-Tab to another application, another app grabbing) takes the seat grab away,
      and dismisses there — the way WinForms closes a drop-down when its owner deactivates. The
      spurious owner focus-out is still ignored. The no-spurious-dismiss path is verified on real GTK
      (the gallery's drop-down captures stay open); the Alt-Tab positive path needs a full window
      manager, which the rootless Xwayland test display cannot provide (same limit as interactive
      GUI in CI).
- [x] **Demo gallery**: `NativeForms.Demo` is a tabbed showcase of every shipped control with
      representative property settings; every new control lands with a gallery section
      (coverage tracked in §11).
- [x] **Reference documentation**: every shipped control/subsystem has a page under `docs/`
      (usage example + API tables + notes + WinForms deltas); both READMEs link into the docs and
      carry the control index (coverage tracked in §11).

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
This matrix tracks the rest of "done": a section in the `NativeForms.Demo` tabbed gallery and a
reference page under `docs/`. Every change that ships a control/feature extends this table in the
same commit. `—` = not applicable.

| Feature | Tests | Demo | Docs |
|---|---|---|---|
| Architecture (core/peer/realization/containers/popups/modal) | ✔ | — | [architecture.md](architecture.md) |
| `Application` + `BackendRegistry` | ✔ | ✔ | [controls/application.md](controls/application.md) |
| `Control` base (incl. `Margin`, `PointToScreen`, `ContextMenuStrip`) | ✔ | ✔ | [controls/control.md](controls/control.md) |
| `Form` (modal, window management, icon) | ✔ | ✔ | [controls/form.md](controls/form.md) |
| `Timer` | ✔ | ✔ | [controls/timer.md](controls/timer.md) |
| `ImageList` (+ badges) | ✔ | ✔ | [controls/imagelist.md](controls/imagelist.md) |
| `Button` (image, `DialogResult`) | ✔ | ✔ | [controls/button.md](controls/button.md) |
| `Label` (AutoSize/TextAlign/mnemonics/image) | ✔ | ✔ | [controls/label.md](controls/label.md) |
| `LinkLabel` | ✔ | ✔ | [controls/linklabel.md](controls/linklabel.md) |
| `TextBox` | ✔ | ✔ | [controls/textbox.md](controls/textbox.md) |
| `MaskedTextBox` | ✔ | ✔ | [controls/maskedtextbox.md](controls/maskedtextbox.md) |
| `RichTextBox` (+ RTF subset) | ✔ | ✔ | [controls/richtextbox.md](controls/richtextbox.md) |
| `SearchBox` | ✔ | ✔ | [controls/searchbox.md](controls/searchbox.md) |
| `FilePicker` / `FolderPicker` | ✔ | ✔ | [controls/filepicker.md](controls/filepicker.md) · [folderpicker.md](controls/folderpicker.md) |
| `IconLabel` (image **and** text) | ✔ | ✔ | [controls/iconlabel.md](controls/iconlabel.md) |
| `CheckBox` / `RadioButton` (images) | ✔ | ✔ | [controls/checkbox.md](controls/checkbox.md) · [radiobutton.md](controls/radiobutton.md) |
| `ToggleSwitch` | ✔ | ✔ | [controls/toggleswitch.md](controls/toggleswitch.md) |
| `SplitButton` / `DropDownButton` | ✔ | ✔ | [controls/splitbutton.md](controls/splitbutton.md) |
| `NumericUpDown` / `DomainUpDown` | ✔ | ✔ | [controls/numericupdown.md](controls/numericupdown.md) · [domainupdown.md](controls/domainupdown.md) |
| `TrackBar` | ✔ | ✔ | [controls/trackbar.md](controls/trackbar.md) |
| `HScrollBar` / `VScrollBar` | ✔ | ✔ | [controls/scrollbar.md](controls/scrollbar.md) |
| `ProgressBar` (incl. marquee) | ✔ | ✔ | [controls/progressbar.md](controls/progressbar.md) |
| `ProgressTile` (Explorer-style drive tile) | ✔ | ✔ | [controls/progresstile.md](controls/progresstile.md) |
| `DateTimePicker` | ✔ | ✔ | [controls/datetimepicker.md](controls/datetimepicker.md) |
| `MonthCalendar` (title drill-down) | ✔ | ✔ | [controls/monthcalendar.md](controls/monthcalendar.md) |
| `CalendarView` (Day/WorkWeek/Week/Month scheduler) | ✔ | ✔ | [controls/calendarview.md](controls/calendarview.md) |
| `TimePicker` (double-click analog clock) | ✔ | ✔ | [controls/timepicker.md](controls/timepicker.md) |
| `ClockFace` (analog dial, stand-alone or popup) | ✔ | ✔ | [controls/clockface.md](controls/clockface.md) |
| `PictureBox` | ✔ | ✔ | [controls/picturebox.md](controls/picturebox.md) |
| `Panel` (AutoScroll) | ✔ | ✔ | [controls/panel.md](controls/panel.md) |
| `GroupBox` (caption image, nesting) | ✔ | ✔ | [controls/groupbox.md](controls/groupbox.md) |
| `TabControl` / `TabPage` | ✔ | ✔ | [controls/tabcontrol.md](controls/tabcontrol.md) |
| `SplitContainer` | ✔ | ✔ | [controls/splitcontainer.md](controls/splitcontainer.md) |
| `Expander` | ✔ | ✔ | [controls/expander.md](controls/expander.md) |
| `Accordion` / `AccordionPane` | ✔ | ✔ | [controls/accordion.md](controls/accordion.md) |
| `Ribbon` (tabs, groups, item model, overflow, minimize-to-strip + tab flyout) | ✔ | ✔ | [controls/ribbon.md](controls/ribbon.md) |
| `GridPicker` / `RibbonGridButton` (Office table-size chooser) | ✔ | ✔ | [controls/gridpicker.md](controls/gridpicker.md) |
| `DockPanel` / `DockContent` (dock, float, tab, split, auto-hide, persistence) | ✔ | ✔ | [controls/dockpanel.md](controls/dockpanel.md) |
| `FlowLayoutPanel` | ✔ | ✔ | [controls/flowlayoutpanel.md](controls/flowlayoutpanel.md) |
| `TableLayoutPanel` | ✔ | ✔ | [controls/tablelayoutpanel.md](controls/tablelayoutpanel.md) |
| `ListBox` (selection modes, icons) | ✔ | ✔ | [controls/listbox.md](controls/listbox.md) |
| `CheckedListBox` | ✔ | ✔ | [controls/checkedlistbox.md](controls/checkedlistbox.md) |
| `ComboBox` | ✔ | ✔ | [controls/combobox.md](controls/combobox.md) |
| `ListView` (5 views, groups, checks, sort, label edit) | ✔ | ✔ | [controls/listview.md](controls/listview.md) |
| `TreeView` | ✔ | ✔ | [controls/treeview.md](controls/treeview.md) |
| `TreeListView` | ✔ | ✔ | [controls/treelistview.md](controls/treelistview.md) |
| `DataGridView` (kinds, editing, frozen, reorder, clipboard) | ✔ | ✔ | [controls/datagridview.md](controls/datagridview.md) |
| `MenuStrip` + item model | ✔ | ✔ | [controls/menustrip.md](controls/menustrip.md) |
| `ContextMenuStrip` | ✔ | ✔ | [controls/contextmenustrip.md](controls/contextmenustrip.md) |
| `ToolStrip` | ✔ | ✔ | [controls/toolstrip.md](controls/toolstrip.md) |
| `StatusStrip` | ✔ | ✔ | [controls/statusstrip.md](controls/statusstrip.md) |
| `ToolTip` | ✔ | ✔ | [controls/tooltip.md](controls/tooltip.md) |
| `NotifyIcon` | ✔ | — | [controls/notifyicon.md](controls/notifyicon.md) |
| Modal forms + `MessageBox` + common dialogs | ✔ | ✔ | [controls/dialogs.md](controls/dialogs.md) |
| MVVM primitives + binding + `ICommand` wiring | ✔ | ✔ | [mvvm.md](mvvm.md) |
| Owner-draw engine (`IGraphics`/`ITheme`/canvas/shared primitives) | ✔ | ✔ | [custom-controls.md](custom-controls.md) |

`NotifyIcon` has no gallery section (a tray icon in a demo is intrusive; Win32-only today).
Colour and font dialogs are demoed indirectly through the modal `MessageBox` round-trip. The file
dialog is no longer indirect: the `FilePicker`'s browse button opens the platform's real chooser and
the autopilot drives it — posting the click rather than awaiting it, then dismissing with Escape,
exactly as the `MessageBox` check does. That check runs **last**, with the modal one: a native
chooser is a toplevel that takes the keyboard focus with it and does not reliably hand it back, so
placed mid-script it strands every later typing check.
