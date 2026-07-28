# NativeForms — Product Requirements & Implementation Checklist

> A fast, tiny, trim/AOT-compatible UI toolkit with a Windows Forms-shaped API. Windows, buttons,
> labels and text boxes are real platform widgets (Win32, GTK) driven via P/Invoke; every other
> control is owner-drawn in the host platform's own visual style. Shipping platforms are Windows and
> Linux — macOS is a stated future direction (§10 M9), not a shipped feature.

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
2. **Native where it pays, owner-drawn to match.** *Today* the native-peer set is deliberately
   narrow — the window plus the text-bearing primitives (`Form`, `Button`, `Label`, `TextBox`,
   `RichTextBox`), where the OS owns caret, IME, selection and accessibility. Everything else is
   drawn by Core against the platform's **theme colors, metrics and fonts**, which is what lets one
   implementation of a `DataGridView` or `CalendarView` behave identically on every backend. Widening
   that native set for controls that have a faithful platform counterpart is a tracked goal, not a
   claim — see §12.
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
Hawkynt.NativeForms.Backends.Windows   (Win32/user32/comctl32/uxtheme via [LibraryImport])  SHIPPING
Hawkynt.NativeForms.Backends.Gtk       (GTK 3 via [LibraryImport])                          SHIPPING
Hawkynt.NativeForms.Backends.MacOS     (Cocoa/AppKit via objc_msgSend)                      STUB — throws
Hawkynt.NativeForms.Generators         (Roslyn generator, packed as an analyzer in Core)     SHIPPING
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

| Pattern  | How NativeForms supports it                                                                                                                                                                                                                        |
| -------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **MVVM** | `ObservableObject` view-models, `RelayCommand`/`RelayCommand<T>` (`ICommand`), and `PropertyBinding<T>` two-way binding between VM properties and control properties, `BindingExtensions.Bind` lambda sugar, converters, fallbacks, chained paths. |
| **MVC**  | Controls raise events; a controller mediates model↔view. Provided by the plain event surface + one-way `PropertyBinding` from model to view.                                                                                                       |
| **MVP**  | Views expose interfaces (`interface IFooView`); a presenter drives them. NativeForms controls are interface-friendly (events + properties); `[ ]` ship a small `IView`/passive-view sample.                                                        |

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
- [~] Startup (cold) to first window shown, measured on the Linux/GTK AOT self-contained build via
      the demo's `--measure-startup`: **~90 ms** floor, of which the toolkit itself is negligible —
      backend registration ~0 ms, a bare form constructs in ~0.3 ms, and the whole gallery (hundreds
      of controls across nine tabs) adds only ~40 ms. The floor is **`gtk_init`** (the X connection,
      theme CSS and fontconfig on the first GTK call), a GTK cost the toolkit cannot shrink; the
      **< 50 ms** target is a Win32-class figure, unreachable under `gtk_init` on GTK. Construction
      and realize allocate the budgeted kilobytes; no toolkit hotspot remains to remove.
- [x] Backend linking: `NativeForms.TrimProbe` (GTK-only) publishes without the Windows/macOS
      assemblies (asserted in nightly CI); AOT probe 1.4 MB vs 2.4 MB all-backends demo.
- [x] Data structures: Span at the pixel/text seams, LINQ eliminated from all paint/input/
      layout paths (one cold-path exception message kept), gesture-scoped transients audited —
      pooling rejected as unwarranted at current sizes.
- [x] `NativeForms.Benchmarks` (dependency-free Stopwatch harness: construction ns+bytes,
      realize, paints/s, 100k-row scroll) + nightly job with 2× regression thresholds and JSON
      artifact; trim job alongside.
- [x] **Scale is linear**, benchmarked and gated: building a form of 2000 controls ~0.5 ms (was
      quadratic — every add re-laid the whole form; now the base lays out only for a docked child),
      scrolling an AutoScroll panel of 2000 children ~0.3 ms/notch (was quadratic — each child
      re-derived the content extent; now one viewport per pass), binding a 50k-row `DataGridView`
      ~6 ms (virtualized), realizing 2000 controls ~1 ms. Linear-scale gates fail the nightly job if
      the build or scroll paths regress to quadratic.

Design rules that serve the budget: buffered-then-flushed peer state (no shadow trees), lazy child
realization, `Rectangle`/`Point`/`Size` value types for geometry, and no reflection metadata cache.

---

## 5. Owner-draw & theming (the "looks native even when we draw it" layer)

- [x] `IGraphics` surface: lines, rects, text (native font, aligned), images/icons, clip stack.
      Backed by **GDI** (Win32) and **Cairo/Pango** (GTK); CoreGraphics (Cocoa) pending.
- [x] Colour emoji in any control's `Text`, on both rendering paths and both platforms. GTK gets it for
      free (`pango_cairo_show_layout` is colour-glyph-capable and picks up the system emoji font). On Win32
      the native widgets get it from the OS, and the owner-drawn path diverts the strings that need it to
      Direct2D, since GDI cannot emit COLR/CPAL layers — §13. Strings without a colour glyph are untouched:
      the scan that decides costs one pass and no allocation.
- [x] `ITheme`: accent, window/control/field background, text/disabled/selection colors, default font,
      row height, scrollbar size — queried from the OS (`GetSysColor`/`SPI_GETNONCLIENTMETRICS` on
      Win32; `GtkStyleContext`/`gtk-font-name` on GTK); `DefaultTheme` fallback for headless/tests.
- [x] `ICanvasPeer` + `OwnerDrawnControl`: one paintable/focusable native surface per backend, so
      every custom control is written once and runs on any backend. Mouse/key/focus + paint plumbed.
- [x] Decoder-free `IImage` (32-bit ARGB) so controls show icons without an image library.
- [x] Light/dark mode + high-contrast follow-the-OS: `IPlatformBackend.ThemeChanged`
      (WM_THEMECHANGED family / GtkSettings notify), theme-cache invalidation, realized
      owner-drawn controls repaint, `ITheme.IsHighContrast`.
- [~] DPI: `GetDpiScale` + `Control.LogicalToDevice` groundwork done; GTK pins native
      widget text to the owner-drawn 96-DPI font-map baseline so labels match native
      controls on HiDPI screens; per-monitor rescale-on-move pending.
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
- [x] Source-generated `[Bindable]`/property-accessor generator so `DataSource`+`DisplayMember`
      resolve member getters at **compile time** (keeps list binding reflection-free/AOT-safe). The
      generated `GetMemberAccessor` is a `switch` from name to a static lambda reading that property, and
      the control surface is `ComboBox.SetDataSource<T>(items, displayMember, valueMember)` /
      `ListBox.SetDataSource<T>(items, displayMember)` constrained to `T : IBindableMembers` — a static
      abstract interface member, so the lookup is reached from the type argument without ever naming a
      `Type`. A name the model does not have throws at the call that named it, rather than binding blank
      the way the reflection-based libraries this shape is borrowed from do. Diagnostic `NFG004` covers a
      `[Bindable]` class that is not a non-nested partial.
- [x] `ObservableList<T>` with granular change events — add/remove/replace/reset, plus `Move`
      (`ListChangeType.Moved` carrying `OldIndex` + `Index`) and the read-only `IReadOnlyObservableList<T>`
      view for consumers that observe but do not mutate.
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
- [x] `Cursor`/`Cursors` + ambient `Control.Cursor` (WM_SETCURSOR / gdk named cursors; LinkLabel hand);
      `Cursor.FromBytes` builds a custom bitmap pointer from a `.cur` (hotspot honoured), an `.ani`
      (first frame) or any still image, realized natively (`gdk_cursor_new_from_pixbuf` / `CreateIconIndirect`)
- [x] Focus model — `Focus()`/`Focused`/`CanFocus`, `TabIndex`/`TabStop` (defaults follow the
      control kind), WinForms event order (Enter→GotFocus / LostFocus→Leave with container-chain
      crossing), `Form.ActiveControl` + initial focus, Tab/Shift+Tab navigation through nested
      containers, `IsInputKey` claims, form-wide menu shortcuts + Alt-mnemonic bar activation
- [~] Keyboard — `KeyDown`/`KeyUp`/`KeyPress` on owner-drawn surfaces, mnemonics/accelerators via the
      form dialog-key chain done; a native `TextBox` now previews its keys through that chain over the
      peer key seam (Enter → `AcceptButton`, Escape → `CancelButton`, Tab/Shift+Tab navigation, menu
      shortcuts), with `AcceptsReturn`/`AcceptsTab` keeping the key for a multiline editor. Native
      button-mnemonic clicks (an owner-drawn concern already, via the mnemonic chain) are the remaining
      native-preview gap.
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
- [~] `GroupBox` (native frame, or owner-drawn — §12) — themed frame + caption, caption image (icon before the text in the
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
      buttons, a `DocumentTabStripEdge` (`TabAlignment`) placing the tab strip on any edge — horizontal
      rows on top/bottom, vertical strips on left/right — and reflection-free `SaveLayout`/`LoadLayout`
      round-trip. Empty manager ≈544 B, empty
      pane ≈368 B, populated repaint 0 B/frame
- [~] `FlowLayoutPanel` (all four `FlowDirection`s, `WrapContents`, `Control.Margin`,
      AutoScroll interplay) and `TableLayoutPanel` (absolute/percent/auto-size styles, spans,
      auto-placement, cell borders, in-cell Dock/Anchor for explicitly assigned children) done;
      grid auto-grow and invisible-child skip pending
- [x] `Panel.AutoScroll` — themed scrollbars (shared `ScrollBarRenderer`), wheel + thumb drag,
      children scrolled via the logical→peer bounds mapping seam (`AutoScrollPosition`)

### 7.3 Buttons & simple inputs
- [~] `Button` (native) — click, text *(done: click/text/bounds/enable/visible)*
  - [~] [x] `DialogResult` (click walks to the owning Form, closes modal); [x] default/accept styling
        — `Form.AcceptButton` marks the button on its peer (`IButtonPeer.SetDefault`), painted by the
        platform (Win32 `BS_DEFPUSHBUTTON`, GTK `gtk_widget_grab_default` when the window chain is
        ready — theme-dependent emphasis); [ ] `FlatStyle` pending
  - [~] Image (`Image`/`ImageAlign`/`TextImageRelation` peer surface): GTK full image+text
        (`gtk_button_set_image` + position); Win32 `BM_SETIMAGE`/`BS_BITMAP` image-only (classic
        BUTTON cannot render both — documented); owner-drawn image+text fallback pending
- [~] `CheckBox` (native, or owner-drawn — §12) — `Checked` + `CheckedChanged`, click/Space toggle, themed checkmark done;
      image + text via `ContentLayout` done; tri-state `CheckState` pending
- [~] `RadioButton` (native, or owner-drawn — §12) — themed ring + accent dot, grouping by container, click/Space,
      `CheckedChanged`, image + text via `ContentLayout` done
- [~] `Label` (native) — polish done, images pending
  - [x] Text
  - [x] `AutoSize` (canvas-free `IPlatformBackend.MeasureText`), `TextAlign`, `BorderStyle`
        (Win32 style bit; GTK documented no-op), mnemonic rendering (`&x` underline, GTK `_x`
        translation)
  - [x] Mnemonic activation focuses the next control in tab order
  - [~] `Image` + `ImageAlign`: peer surface done — Win32 `SS_BITMAP` and GTK widget-swap render
        image-only when the caption is empty (image+text is platform-limited, documented)
- [~] `LinkLabel` (native `SysLink`/`GtkLinkButton`, or owner-drawn — §12) — whole-text link: accent color + underline, hover + `Visited` states,
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
        editor). Grid-editor Enter/Escape, word-wrap control and the undo API are still pending
        (`AcceptsReturn`/`AcceptsTab` are done — see §7.1).
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
- [x] `ListBox` (native, or owner-drawn — §12) — items, per-item icons, wheel/keyboard scroll, `DataSource` binding,
      `SelectionMode` (None/One/MultiSimple/MultiExtended: Ctrl/Shift click + keyboard, sorted
      `SelectedIndices`, anchor ranges, caret via `FocusedIndex`)
- [x] `CheckedListBox` (owner) — per-item check state over the ListBox engine (`ItemCheck`
      veto-able before the flip, `CheckOnClick`, Space toggles selection, shared `CheckGlyph`
      with CheckBox, check states survive item mutation)
- [~] `ComboBox` (native drop-down list, or owner field + popup in native theme — §12) — `DropDownList` and `DropDown`
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
      virtualized paint over the lazily re-flattened visible-node list (100k nodes bounded), drag
      reorder/reparent (`AllowReorder`, `ItemDrag`/`NodeDragOver`/`NodeDrop`, above/onto/below
      insertion marker, translucent drag image `ShowDragImage`, own-subtree guard, hover auto-expand),
      lazy child population on first expand via a per-node delegate (`SetChildLoader`, virtual trees) done;
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
- [x] `TrackBar` (native, or owner-drawn — §12) — Min/Max/Value, `TickFrequency` ticks, horizontal/vertical, themed
      groove + accent fill + thumb, track paging + thumb scrub, Win32 key directions
- [x] `NumericUpDown` / `DomainUpDown` (owner spinner + hosted native TextBox editor) — decimal
      clamping/`Increment`/`DecimalPlaces`, domain matching + `Wrap`, themed spin buttons with
      timer-driven autorepeat (shared `AutoRepeat` engine); commit points documented (no focus
      model yet)
- [x] `HScrollBar`/`VScrollBar` (native, or owner-drawn — §12) — proportional thumb, channel paging, arrow autorepeat,
      Win32 `Maximum − LargeChange + 1` semantics, `Scroll` vs `ValueChanged` split
  - [x] Unify the two internal scrollbar renderers (`Drawing.ScrollBarRenderer` used by
        `Panel.AutoScroll` vs the `ScrollBar` control's own) into one implementation. They were the same
        arithmetic under two names: a container's extent/viewport/offset triple converts exactly into the
        Windows Forms quartet (`minimum = 0`, `maximum = extent - 1`, `largeChange = viewport`), so the
        thumb length and the position-to-pixel mapping now live in one place and the container-shaped
        calls convert and delegate. What legitimately differed — minimum thumb length, thumb inset,
        whether there are stepper buttons — is a `ScrollBarMetrics` profile, so neither caller's rendering
        changed: the pinned paint output is byte-identical.
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
      drag-to-move/edge-resize of a movable appointment with a live snapped ghost and a north-south
      resize cursor — cancelable `AppointmentMoving` then `AppointmentMoved` (`AppointmentMoveEventArgs`),
      the app applies + re-binds, per-entry lockable via `Appointment.Movable` (locked entries show a
      padlock and refuse to drag), Escape cancels; an edge drag follows the pointer's day column so a
      start/end can be dragged onto another day; multi-day timed spans clamped per overlapping day
      with continuation chevrons — only a real (non-clamped) edge resizes, an off-view edge stays put;
      keyboard/wheel navigation; virtualized (only visible days laid out, bounded for
      100k), cached layout so populated repaints allocate zero — including a live drag preview — (empty
      ≈ 624 B, `Appointment` ≈ 48 B)
- [x] `ColorPicker` (owner) — a colour swatch that drops down a 40-colour standard palette
      (light-dismiss `IPopupPeer`); `SelectedColor`/`SelectedColorChanged`, keyboard-reachable, greys
      when disabled, and embeddable in a `RibbonHostItem`/`ToolStripControlHost`
- [x] Calendar per-day delegates on `MonthCalendar`/`DateTimePicker`/`CalendarCore`:
      `DayBackgroundProvider` (shade holidays), `DateSelectable` (predicate blocking picks), and
      `DayTooltipProvider` (per-day hover tooltip, shown by `MonthCalendar`)
- [~] `DateTimePicker` (owner field + popup calendar sharing `CalendarCore`) — Long/Short/Time/
      Custom invariant formats, `ShowCheckBox`/`Checked` greying, closed Up/Down day stepping,
      Alt+Down/F4, commit/cancel semantics, drop-down title drill-down done; `BoldedDates` and
      per-part focus on the closed field pending
- [x] `TimePicker` (owner field + shared `Drawing.SpinnerRenderer` column) — `TimeSpan` `Value`,
      `Min`/`MaxTime` window, `ShowMinutes` (hours-only)/`ShowSeconds`/`Use24HourClock` layouts,
      per-part caret (click, Left/Right) with the spinner buttons, Up/Down and the wheel stepping the
      part under it, wrap-without-carry, timer-driven autorepeat via the shared `AutoRepeat` engine
  - [x] Double-click opens the analog `ClockFace` in a light-dismiss popup (the `IPopupPeer`
        mechanism `DateTimePicker` uses): staged hour → minute → seconds dial matching the field's
        precision, live preview into the field clamped to `Min`/`MaxTime`, picking the final part
        commits (OK/Enter too), Escape/outside-click cancel-and-revert
- [x] `ClockFace` (owner dial, reusable stand-alone or popup-hosted like `CalendarCore`) — themed
      analog picker: 12-hour ring + AM/PM toggle or two-ring 00–23 dial, minute/seconds rings snapping
      to a single unit, `Precision` (Hours/Minutes/Seconds) with picking the final part committing,
      click/drag/keyboard, stage machine with `Committed`/`Cancelled` callbacks,
      allocation-free repaint at every stage (cached strings, shared trig table, cached hand endpoint)
- [x] `ProgressBar` (native when horizontal, or owner-drawn — §12) — determinate (Min/Max/Value, accent fill), `Style.Marquee`
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
- [x] `ContextMenuStrip` — `Control.ContextMenuStrip` + right-click at the cursor on both
      owner-drawn and native-widget controls (peer `ContextMenuRequested` from WM_CONTEXTMENU /
      a GTK button-3 press, plus the Menu / Shift+F10 keyboard request)
- [x] `ToolStrip` (owner) — icon+text buttons, toggles (`CheckOnClick`), separators,
      `ToolStripDropDownButton`/`ToolStripSplitButton` (checkboxes + icons in their drop-downs via
      `ToolStripMenuItem`), `ToolStripControlHost` hosting a real `Control` (combo/date/time/colour
      pickers), overflow chevron popup, `ImageList`
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
- [x] `PropertyGrid` (owner) — category grouping, per-type editors reusing the real pickers, and the
      source-generated `PopulateGrid` of §15; see [controls/propertygrid.md](controls/propertygrid.md)

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
  - [x] Quick Access Toolbar (`QuickAccessItems`): icon-only `RibbonButton` commands painted at the
        right of the tab strip, hover-highlit, hit-tested ahead of the tabs, reachable from any tab
  - [x] Contextual tab groups (`ContextualTabGroups`): a colour-coded family (`Text` + `Color`) whose
        tabs show only while the group's `Visible` is set — filtered out of the strip paint, hit-test
        and keyboard nav otherwise, and marked in the group colour while shown; hiding the group that
        holds the selection hands it to the nearest shown tab. (The Office-style banner spanning the
        group's tabs is a future refinement; the colour marker stands in for it.)
  - [x] The grid picker never grows past its `MaxColumns`/`MaxRows` — the cell hit-test rejects
        out-of-grid points, the arrow keys `Math.Min`-cap, and `SetSelection` clamps
  - [x] Two-line caption wrapping on large items: a caption past `_MaxLargeCaptionWidth` wraps at the
        space split that makes the wider line narrowest, keeping the column compact; the two lines are
        cached (one lazy object per wrapping item, off the per-instance budget) so the paint path stays
        allocation-free
  - [x] The tab-click flyout shows only item glyphs — a hosted control never re-parents into the
        popup; its slot paints a recessed placeholder box instead, while the live control stays put
        under the expanded ribbon
  - [ ] KeyTips (deferred — a `MenuStrip` above the ribbon already covers the application-menu case)
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
      `WarningThreshold`; `Clickable` gates focus/hover/`Click`, `Selected` paints the selection face;
      `Compact` gives a short one-row layout (icon left, caption over the bar down the content height).
      Named for its shape, not for drives: Core is platform-agnostic and the paint path may not touch
      the filesystem, so both captions are caller-supplied strings and nothing is `DriveInfo`-bound
- [x] `Breadcrumb` (owner) — Explorer navigation bar: `BreadcrumbItem` segments (`Text`, `Tag`,
      `ImageIndex`/`ImageKey`) laid left to right, chevron separators, hover-highlit and clickable.
      `TrimOnClick` trims the path to a clicked segment (navigate-up) before `ItemClicked`; when the
      segments outgrow the width the leading ones fold behind a "…" chip, the last segment always kept.
      A `SubItemsProvider` delegate (folder walk) drops down a segment's children on its chevron —
      virtual namespaces (archives, remote trees) supported — with a configurable `PathSeparator`.
      `Editable` turns an empty-space click into a hosted `TextBox` path field: Enter reparses through a
      `PathParser` delegate and raises `PathEntered`, Escape reverts, and an `AutoCompleteSource`
      delegate drops down a suggestion list that filters as you type (arrow/Enter/click to pick,
      Tab to complete-and-stay, a second Tab to leave the field)

### 7.10 App-shell & advanced controls (shipped — the pieces a file explorer / image editor / media player / coding IDE / browser shell / settings app needed)
- [x] `PropertyGrid` (owner) — two-column name/value editor grouped by category, with per-row typed
      inline editors (text, checkbox/bool, numeric, dropdown/enum, colour, browse); reflection-free
      (delegate/selector-driven rows), expandable categories, a description strip, `SelectedObject` via
      a supplied row model, and `PropertyValueChanged`. Used by settings, the IDE and file properties
- [x] `CodeTextBox` / syntax-highlighting editor (owner) — a multiline text surface with a line-number
      gutter, current-line highlight, tab/indent handling, and a pluggable delegate tokenizer for
      colouring (keyword/string/comment spans); optional autocomplete popup reusing the light-dismiss
      `IPopupPeer`. The IDE's centrepiece; `RichTextBox` only covers basic RTF
- [x] `ZoomPanel` / zoomable-pannable canvas (owner) — a scrollable viewport that scales and pans its
      content (mouse-wheel zoom, drag-pan, fit/actual-size), optional rulers; the image editor's working
      surface and a document/media viewport. `PictureBox` only displays; `Panel.AutoScroll` only scrolls
- [x] Virtual list mode — `VirtualMode` on `ListView` (and later `DataGridView`/`TreeView`): rows served
      by a `RetrieveVirtualItem`-style delegate over a `VirtualListSize`, so a huge folder or search
      result never materialises every item. Currently every row is a live object
- [x] `TreeView` inline label editing — finish the existing `BeginEdit` TODO: `LabelEdit`, an overlaid
      hosted `TextBox`, `BeforeLabelEdit`/`AfterLabelEdit` (F2/click-to-rename), mirroring `ListView`
- [x] `RangeSlider` (owner) — a two-thumb `TrackBar`: a lower and upper value over a min/max, the span
      between them filled, keyboard + drag per thumb, `RangeChanged`. Media trim, level/curve endpoints,
      numeric filter ranges
- [x] `TokenBox` / tag-and-chip input (owner) — a text field that turns committed entries into removable
      chips (× to delete, Backspace deletes the last), a delegate `AutoCompleteSource`, `Tokens` +
      `TokensChanged`. Tags, recipients, search scopes
- [x] `NavigationView` / side-bar shell (owner) — a collapsible left navigation pane of icon+caption
      items (and sub-items), a selected-item accent, an optional hamburger collapse to icons-only, and a
      content region; the modern settings/browser app frame. `Accordion` is close but is not a nav shell
- [x] `InfoBar` / banner + in-app `Toast` (owner) — an inline dismissible message strip (info/success/
      warning/error severity with an icon, title, message and optional action button) and a transient
      corner toast over the form. `NotifyIcon` is only the OS tray; there is no in-window messaging surface
- [x] `SegmentedControl` (owner) — a horizontal group of mutually-exclusive toggle segments (the
      button-styled radio group / iOS-style picker), `SelectedIndex` + `SelectedIndexChanged`. Composable
      from `RadioButton` today but wanted as one control for toolbars and settings

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
- [x] `ImageDecoder`: pure-managed multi-format decode into a frame model (`DecodedImage`/`ImageFrame`,
      ARGB + per-frame delay + loop count) via a magic-byte `Decode` dispatcher — PNG (8-bit, all
      filters, non-interlaced), BMP (8/24/32-bit BI_RGB), PCX (8-bit palette / 24-bit planes),
      ICO/CUR (PNG/32-bit/24-bit+mask entries), animated GIF (LZW, disposal/transparency, NETSCAPE
      loop) and ANI (RIFF/ACON icon frames + rate/seq) — with `ImageList.AddPng`/`AddIco`
      (nearest-neighbor resample); the encoders live only in the test project
- [x] `AnimatedImage` + `AnimationClock`: an animated image picks its frame as a pure function of
      elapsed time (loop none/N/forever, modulo the loop), so it stays in sync whether or not it has
      been on screen; one shared `Timer` repaints only the visible subscribers, a hidden one shows the
      exact frame it would have when revealed. `PictureBox.AnimatedImage` hosts still or animated images;
      a disabled box freezes the animation (resuming exactly where it stopped when re-enabled) and paints
      the frame grayscale. `AnimatedImage` implements `IImage`, so it can be assigned to any control's
      plain `Image` property — the same interface a still image uses — and animates there: owner-drawn
      consumers (CheckBox, RadioButton, IconLabel, GroupBox, Expander, DropDownButton/SplitButton,
      ProgressTile) resolve the current frame each repaint through `OwnerDrawnControl`, and native-widget
      Button/Label re-push the current frame to their peer as the shared clock advances; all freeze and
      grey while disabled
- [x] **Uniform image API across all controls**: a direct `Image` property (Button, Label, CheckBox,
      RadioButton, GroupBox, PictureBox) or `ImageList` + `ImageIndex`/`ImageKey` — the latter now on
      every item class (TabPage, ToolStripItem, ListViewItem, TreeNode+`SelectedImageKey`, RibbonGroup,
      AccordionPane, DockContent), resolved through the shared `ImageList.ResolveIndex`/`IndexOfKey`
      (index wins, key falls back, case-insensitive). Arbitrary-object lists (ComboBox, ListBox) reach
      the same `ImageList` through their `ImageIndexSelector`/`ImageSelector` lambdas. Same rendering
      path everywhere. An `ImageList` entry may itself be an `AnimatedImage` (`ImageList.Add(AnimatedImage)`):
      `GetImage` resolves it to the current frame and the list raises `FrameChanged` as it advances, so
      every icon-based control (TreeView, TreeListView, ListView, TabControl, Accordion, Breadcrumb,
      DockPanel, Ribbon, ComboBox) animates the icon by repainting on that event
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
- [~] Per-platform smoke tests / screenshots for owner-drawn controls. Linux is covered by the autopilot's
      in-process captures; Windows now has a manual route but no automation — the gallery comes up under
      wine (with the one `Rtf` swapped for plain text, see below) and a window is captured by its own
      drawable, because `:1` is a rootless Xwayland whose root grabs black and ImageMagick 7 here has no
      X11 delegate:

      ```py
      from Xlib import display, X            # python-xlib; walk the tree for the window by WM name,
      raw = win.get_image(0, 0, w, h, X.ZPixmap, 0xffffffff)   # then BGRX -> a binary PPM that
      # ...                                  # `magick out.ppm out.png` converts without a delegate.
      ```

      Automating it needs the autopilot's injection and capture behind a backend seam — see the
      interactive-verification entry below, which is the same gap.
- [ ] **Win32 rendering findings from the wine run**, none of them promotion-related, all reproducible by
      bringing the gallery up as described above:
      - Controls hosted in a `ToolStrip` through `ToolStripControlHost` paint **blank** — the date picker
        and the zoom combo are empty boxes, while the colour swatch beside them draws. Owner-drawn controls
        everywhere else on the page paint fine, so this is specific to the strip's hosting, and it predates
        the promotions (the date picker was never promotable). It became more visible once
        `ToolStripControlHost` started pinning what it hosts to the painter, which is the right call for
        GTK — a toolbar row is shorter than a platform combo will draw in.
      - A plain `→` falls back to a replacement box even though it is not an emoji, so the font-fallback
        chain for the owner-drawn text path still deserves a look. (The emoji themselves now take the
        Direct2D path of §13.)
      - The `RichTextBox` cannot be exercised at all under wine (see below), so its Win32 paint path
        remains unobserved.
- [x] `TableLayoutPanel` now sizes and positions its tracks from `DisplayRectangle`, so cells honor
      `Padding` and never sit under a visible `AutoScroll` scrollbar — the same class of defect
      `Panel` was fixed for.
- [x] The Win32 halves of the native-tooltip support (child window subclassing, `TOOLTIPS_CLASS`) have
      now executed: `Win32NativePromotionTests` raises a tip on a native button and asserts a real
      `tooltips_class32` window appears where there was none before — the half a headless fake cannot see.
      Verified under wine as well as on the CI runner.
- [x] **The Win32 native-peer promotions have executed**, under wine on the Linux dev box: a probe
      publishes `win-x64` self-contained, registers `Win32Backend`, and asserts on the live desktop that
      each of the nine promotions really reached a widget, that each gated control stayed on the painter,
      that driving the widget round-trips (`Checked`, radio grouping *including clearing the group*,
      `LinkVisited`, `Value`, `SelectedIndex`, items added after realization, `TopIndex`,
      `IndexFromPoint`, a group box child's bounds), and that a mid-use property change swaps the peer
      with the state intact. `Win32NativePromotionTests` holds that sweep, self-skipping off Windows like
      the real-GTK tier does, so CI's `windows-latest` job runs it against a real desktop on every push.
      It also runs **under wine on the Linux dev box**, which is what turned "compile-verified" into
      "verified" for §12 — wine reports itself as Windows, so the fixture executes rather than skipping.
      The whole suite passes there: 1989 tests, 1954 passed, 0 failed, 35 skipped, and those 35 are the
      GTK fixtures correctly standing down. Recipe, since `dotnet test` cannot cross-run:

      ```sh
      # a scratch NUnitLite host for the test assembly — the test project publishes no launcher
      dotnet new console -o /tmp/nunitrun && cd /tmp/nunitrun
      dotnet add package NUnitLite && dotnet add reference <repo>/NativeForms.Tests/NativeForms.Tests.csproj
      # Program.cs: return new NUnitLite.AutoRun(typeof(Hawkynt.NativeForms.Tests.Win32NativePromotionTests).Assembly).Execute(args);
      dotnet publish -c Release -r win-x64 --self-contained -o /tmp/nunitout
      WINEPREFIX=/tmp/wp WINEDEBUG=-all DISPLAY=:1 wine /tmp/nunitout/nunitrun.exe \
        --where "class =~ Win32NativePromotionTests" --noresult
      ```
- [x] **The GTK backdrop assertions need the display to themselves.**
      `GtkPopupPlacementTests.Opening_a_…_does_not_push_its_window_into_the_backdrop_state` asserts the
      toplevel has *not* entered GTK's `:backdrop` state, which is precisely "another window took focus" —
      so anything else opening a window on the same `DISPLAY` during the run fails it. Measured: 2 failures
      in 40 full-suite runs while wine apps were being launched on `:1`, and 0 in 20 with the display quiet.
      Not a defect; do not chase it. Run the suite without competing windows, or accept the retry.
- [ ] **The demo cannot run end-to-end under wine**: `EM_STREAMIN` faults inside wine's
      `riched20`/`msftedit`, so `RichTextBox.Rtf` takes the process down during realization. Established
      by bisection — plain `Text` is fine, only the stream-in path faults, and it still faults with *all*
      of our subclassing removed, so nothing of ours is on the hook; the `EDITSTREAM` layout and the
      `EDITSTREAMCALLBACK` signature both match the SDK. Until wine fixes it, Win32 runtime coverage comes
      from the test tier above rather than from the gallery walkthrough. Do not re-diagnose this as a
      toolkit bug. Swapping that one `Rtf` for plain text locally does let the whole gallery come up under
      wine, which is how the Win32 rendering was eyeballed and how the `SysLink` fallback below was found —
      worth repeating after Win32 work, since the autopilot itself cannot help there: its injection and
      capture are GTK calls, so `--autopilot` aborts on `libgtk-3.so.0` the moment it tries to settle.
- [ ] **Autopilot capture must not touch the widget tree.** A capture is an *observation*; anything that
      mutates GTK state from inside it corrupts the very walkthrough it is documenting. Measured: adding
      a per-layer background fill that read `BackendRegistry.Resolve().Theme` made the TimePicker
      double-click check fail **4 runs in 5**, against a baseline of **2 in 8** once reverted — because
      `GtkTheme`'s constructor creates and destroys a `GtkLabel` to sample the style context, and pumping
      the main loop from a capture settles pending relayouts, which moved a later check's press onto the
      container (`presses landed on: GtkFixed`). Any future capture work must resolve colours and settle
      *before* the walkthrough starts, never during it. The residual `gtk_widget_draw: alloc_needed`
      warning on a freshly mapped dialog is accepted as cosmetic until then.
- [x] **The TimePicker double-click check is deterministic.** It failed ~2 runs in 8 on X11 with
      `the clock never opened`. Established along the way: `GtkCanvasPeer` *is* a `gtk_fixed_new()`, so the
      `presses landed on: GtkFixed` line means the gesture **did** reach the control — it was a
      double-click *recognition* miss, not a targeting one. Recognition compares the wall-clock gap between
      the two presses against the desktop's `DoubleClickTime` (400 ms here), and injected input shares the
      machine with everything else. Refuted by measurement, so do not retry: making the two presses atomic
      in one pump; dismissing a stray popup first; making the geometry read atomic; dropping the settles
      between the presses (each still ~2 in 6). Fixed by retrying the gesture once from a lapsed click
      state — **0 failures in 8 runs** — which keeps the check proving that a real injected double click
      opens a real popup, while the recognition rule itself stays pinned deterministically by
      `TimePickerTests` against the headless backend.
      *Method note:* this check has high run-to-run variance; take **≥5 runs** before concluding a change
      helped. Two single-run A/Bs during the investigation pointed at the wrong culprit.
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
(WebBrowser/WebView, printing, MDI) — those are decided when their milestone neighborhood ships.
`PropertyGrid` was on that list and has since shipped (§7.10, M10).

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
- **M10 — App shell & advanced controls (§7.10).** SegmentedControl, RangeSlider, InfoBar + Toast,
  NavigationView, TreeView label editing, TokenBox, ZoomPanel, ListView virtual mode, PropertyGrid
  (+ the `[GridEditable]` source generator), CodeTextBox. `[x]` — all ten shipped, tested,
  demoed and documented.
- **M11 — Native-peer promotion (§12).** Opt into real platform widgets for the controls that have a
  faithful counterpart, keeping the owner-drawn path as the fallback. `[~]` — the mechanism (gate,
  opt-in switch, declining backends, state-transparent re-realization, identical-behaviour tests) ships,
  with `CheckBox`, `ProgressBar` and `TrackBar` promoted on GTK; the Win32 halves and the remaining
  candidates follow.
- **M12 — Editor depth (§13).** The refinements the shipped M10 controls still want: undo/redo and
  find/replace in `CodeTextBox`, multiline and nested rows in `PropertyGrid`, virtual mode for
  `TreeView`. `[~]` (`DataGridView` virtual mode shipped)
- **M13 — Attribute-driven grids & lists (§14).** Extend the `[GridEditable]` source generator so one
  annotated model emits the `PropertyGrid` rows, the `DataGridView` columns and the `ListView` columns,
  with every member reference resolved (and diagnosed) at compile time. `[~]` — the `PropertyGrid`,
  `DataGridView` and `ListView` populators all generate today (kinds, widths, sort modes, per-row rules,
  and a `ToListViewItem()` row factory), with `NFG002`/`NFG003` catching bad names; the click, style and
  image attributes remain.

Each milestone: tests first (TDD, per house rule), green `dotnet build`/`dotnet test -c Release`
before commit, semantic single-concern commits with the `+ - * # !` prefix, no AI traces anywhere.

---

## 11. Coverage matrix — tested · demo-ed · documented

A §7 box may be `[x]` (implemented + unit-tested) while the feature is still invisible to users.
This matrix tracks the rest of "done": a section in the `NativeForms.Demo` tabbed gallery and a
reference page under `docs/`. Every change that ships a control/feature extends this table in the
same commit. `—` = not applicable.

| Feature                                                                          | Tests | Demo | Docs                                                                                                 |
| -------------------------------------------------------------------------------- | ----- | ---- | ---------------------------------------------------------------------------------------------------- |
| Architecture (core/peer/realization/containers/popups/modal)                     | ✔     | —    | [architecture.md](architecture.md)                                                                   |
| `Application` + `BackendRegistry`                                                | ✔     | ✔    | [controls/application.md](controls/application.md)                                                   |
| `Control` base (incl. `Margin`, `PointToScreen`, `ContextMenuStrip`)             | ✔     | ✔    | [controls/control.md](controls/control.md)                                                           |
| `Form` (modal, window management, icon)                                          | ✔     | ✔    | [controls/form.md](controls/form.md)                                                                 |
| `Timer`                                                                          | ✔     | ✔    | [controls/timer.md](controls/timer.md)                                                               |
| `ImageList` (+ badges)                                                           | ✔     | ✔    | [controls/imagelist.md](controls/imagelist.md)                                                       |
| `Button` (image, `DialogResult`)                                                 | ✔     | ✔    | [controls/button.md](controls/button.md)                                                             |
| `Label` (AutoSize/TextAlign/mnemonics/image)                                     | ✔     | ✔    | [controls/label.md](controls/label.md)                                                               |
| `LinkLabel`                                                                      | ✔     | ✔    | [controls/linklabel.md](controls/linklabel.md)                                                       |
| `TextBox`                                                                        | ✔     | ✔    | [controls/textbox.md](controls/textbox.md)                                                           |
| `MaskedTextBox`                                                                  | ✔     | ✔    | [controls/maskedtextbox.md](controls/maskedtextbox.md)                                               |
| `RichTextBox` (+ RTF subset)                                                     | ✔     | ✔    | [controls/richtextbox.md](controls/richtextbox.md)                                                   |
| `SearchBox`                                                                      | ✔     | ✔    | [controls/searchbox.md](controls/searchbox.md)                                                       |
| `FilePicker` / `FolderPicker`                                                    | ✔     | ✔    | [controls/filepicker.md](controls/filepicker.md) · [folderpicker.md](controls/folderpicker.md)       |
| `IconLabel` (image **and** text)                                                 | ✔     | ✔    | [controls/iconlabel.md](controls/iconlabel.md)                                                       |
| `CheckBox` / `RadioButton` (images)                                              | ✔     | ✔    | [controls/checkbox.md](controls/checkbox.md) · [radiobutton.md](controls/radiobutton.md)             |
| `ToggleSwitch`                                                                   | ✔     | ✔    | [controls/toggleswitch.md](controls/toggleswitch.md)                                                 |
| `SplitButton` / `DropDownButton`                                                 | ✔     | ✔    | [controls/splitbutton.md](controls/splitbutton.md)                                                   |
| `NumericUpDown` / `DomainUpDown`                                                 | ✔     | ✔    | [controls/numericupdown.md](controls/numericupdown.md) · [domainupdown.md](controls/domainupdown.md) |
| `TrackBar`                                                                       | ✔     | ✔    | [controls/trackbar.md](controls/trackbar.md)                                                         |
| `HScrollBar` / `VScrollBar`                                                      | ✔     | ✔    | [controls/scrollbar.md](controls/scrollbar.md)                                                       |
| `ProgressBar` (incl. marquee)                                                    | ✔     | ✔    | [controls/progressbar.md](controls/progressbar.md)                                                   |
| `ProgressTile` (Explorer-style drive tile)                                       | ✔     | ✔    | [controls/progresstile.md](controls/progresstile.md)                                                 |
| `Breadcrumb` (Explorer navigation bar)                                           | ✔     | ✔    | [controls/breadcrumb.md](controls/breadcrumb.md)                                                     |
| `DateTimePicker`                                                                 | ✔     | ✔    | [controls/datetimepicker.md](controls/datetimepicker.md)                                             |
| `MonthCalendar` (title drill-down)                                               | ✔     | ✔    | [controls/monthcalendar.md](controls/monthcalendar.md)                                               |
| `CalendarView` (Day/WorkWeek/Week/Month scheduler)                               | ✔     | ✔    | [controls/calendarview.md](controls/calendarview.md)                                                 |
| `TimePicker` (double-click analog clock)                                         | ✔     | ✔    | [controls/timepicker.md](controls/timepicker.md)                                                     |
| `ClockFace` (analog dial, stand-alone or popup)                                  | ✔     | ✔    | [controls/clockface.md](controls/clockface.md)                                                       |
| `ColorPicker` (SV/wheel mixer, RGB·HSL·HSV·CMYK tabs, alpha, eyedropper)         | ✔     | ✔    | [controls/colorpicker.md](controls/colorpicker.md)                                                   |
| `PictureBox`                                                                     | ✔     | ✔    | [controls/picturebox.md](controls/picturebox.md)                                                     |
| `Panel` (AutoScroll)                                                             | ✔     | ✔    | [controls/panel.md](controls/panel.md)                                                               |
| `GroupBox` (caption image, nesting)                                              | ✔     | ✔    | [controls/groupbox.md](controls/groupbox.md)                                                         |
| `TabControl` / `TabPage`                                                         | ✔     | ✔    | [controls/tabcontrol.md](controls/tabcontrol.md)                                                     |
| `SplitContainer`                                                                 | ✔     | ✔    | [controls/splitcontainer.md](controls/splitcontainer.md)                                             |
| `Expander`                                                                       | ✔     | ✔    | [controls/expander.md](controls/expander.md)                                                         |
| `Accordion` / `AccordionPane`                                                    | ✔     | ✔    | [controls/accordion.md](controls/accordion.md)                                                       |
| `Ribbon` (tabs, groups, item model, overflow, minimize-to-strip + tab flyout)    | ✔     | ✔    | [controls/ribbon.md](controls/ribbon.md)                                                             |
| `GridPicker` / `RibbonGridButton` (Office table-size chooser)                    | ✔     | ✔    | [controls/gridpicker.md](controls/gridpicker.md)                                                     |
| `DockPanel` / `DockContent` (dock, float, tab, split, auto-hide, persistence)    | ✔     | ✔    | [controls/dockpanel.md](controls/dockpanel.md)                                                       |
| `FlowLayoutPanel`                                                                | ✔     | ✔    | [controls/flowlayoutpanel.md](controls/flowlayoutpanel.md)                                           |
| `TableLayoutPanel`                                                               | ✔     | ✔    | [controls/tablelayoutpanel.md](controls/tablelayoutpanel.md)                                         |
| `ListBox` (selection modes, icons)                                               | ✔     | ✔    | [controls/listbox.md](controls/listbox.md)                                                           |
| `CheckedListBox`                                                                 | ✔     | ✔    | [controls/checkedlistbox.md](controls/checkedlistbox.md)                                             |
| `ComboBox`                                                                       | ✔     | ✔    | [controls/combobox.md](controls/combobox.md)                                                         |
| `ListView` (5 views, groups, checks, sort, label edit, virtual mode, scroll bar) | ✔     | ✔    | [controls/listview.md](controls/listview.md)                                                         |
| `TreeView`                                                                       | ✔     | ✔    | [controls/treeview.md](controls/treeview.md)                                                         |
| `TreeListView`                                                                   | ✔     | ✔    | [controls/treelistview.md](controls/treelistview.md)                                                 |
| `DataGridView` (kinds, editing, frozen, reorder, clipboard)                      | ✔     | ✔    | [controls/datagridview.md](controls/datagridview.md)                                                 |
| `MenuStrip` + item model                                                         | ✔     | ✔    | [controls/menustrip.md](controls/menustrip.md)                                                       |
| `ContextMenuStrip`                                                               | ✔     | ✔    | [controls/contextmenustrip.md](controls/contextmenustrip.md)                                         |
| `ToolStrip`                                                                      | ✔     | ✔    | [controls/toolstrip.md](controls/toolstrip.md)                                                       |
| `StatusStrip`                                                                    | ✔     | ✔    | [controls/statusstrip.md](controls/statusstrip.md)                                                   |
| `ToolTip`                                                                        | ✔     | ✔    | [controls/tooltip.md](controls/tooltip.md)                                                           |
| `NotifyIcon`                                                                     | ✔     | —    | [controls/notifyicon.md](controls/notifyicon.md)                                                     |
| Modal forms + `MessageBox` + common dialogs                                      | ✔     | ✔    | [controls/dialogs.md](controls/dialogs.md)                                                           |
| `SegmentedControl`                                                               | ✔     | ✔    | [controls/segmentedcontrol.md](controls/segmentedcontrol.md)                                         |
| `RangeSlider` (two-thumb)                                                        | ✔     | ✔    | [controls/rangeslider.md](controls/rangeslider.md)                                                   |
| `InfoBar` + `Toast` (stacked, fading)                                            | ✔     | ✔    | [controls/infobar.md](controls/infobar.md)                                                           |
| `NavigationView` (collapsible rail)                                              | ✔     | ✔    | [controls/navigationview.md](controls/navigationview.md)                                             |
| `TokenBox` (chips, autocomplete, per-chip style)                                 | ✔     | ✔    | [controls/tokenbox.md](controls/tokenbox.md)                                                         |
| `ZoomPanel` (wheel-zoom, pan, rulers, grid, zoom slider)                         | ✔     | ✔    | [controls/zoompanel.md](controls/zoompanel.md)                                                       |
| `PropertyGrid` (typed rows, pickers, attribute generator)                        | ✔     | ✔    | [controls/propertygrid.md](controls/propertygrid.md)                                                 |
| `CodeTextBox` (gutter, tokenizer, completion)                                    | ✔     | ✔    | [controls/codetextbox.md](controls/codetextbox.md)                                                   |
| `TreeView` inline label editing (F2)                                             | ✔     | ✔    | [controls/treeview.md](controls/treeview.md)                                                         |
| `[GridEditable]` source generator (packed as an analyzer in Core)                | ✔     | —    | [controls/propertygrid.md](controls/propertygrid.md#attributes)                                      |
| `DataGridView` virtual mode (known + unknown size)                               | ✔     | —    | [controls/datagridview.md](controls/datagridview.md)                                                 |
| Attribute-driven `DataGridView` columns (generator)                              | ✔     | —    | [controls/propertygrid.md](controls/propertygrid.md#grid-column-attributes)                          |
| Attribute-driven `ListView` columns + row factory (generator)                    | ✔     | —    | [controls/propertygrid.md](controls/propertygrid.md#grid-column-attributes)                          |
| MVVM primitives + binding + `ICommand` wiring                                    | ✔     | ✔    | [mvvm.md](mvvm.md)                                                                                   |
| Owner-draw engine (`IGraphics`/`ITheme`/canvas/shared primitives)                | ✔     | ✔    | [custom-controls.md](custom-controls.md)                                                             |

`NotifyIcon` has no gallery section (a tray icon in a demo is intrusive; Win32-only today).
Colour and font dialogs are demoed indirectly through the modal `MessageBox` round-trip. The file
dialog is no longer indirect: the `FilePicker`'s browse button opens the platform's real chooser and
the autopilot drives it — posting the click rather than awaiting it, then dismissing with Escape,
exactly as the `MessageBox` check does. That check runs **last**, with the modal one: a native
chooser is a toplevel that takes the keyboard focus with it and does not reliably hand it back, so
placed mid-script it strands every later typing check.

---

## 12. Native-peer promotion (opt into real widgets where the platform has one)

**Where we are.** The native-peer set is deliberately narrow: `IPlatformBackend` creates a window,
button, label, text box, rich text box, canvas, popup, timer and tray icon. Everything else — 48
classes, including `CheckBox`, `RadioButton`, `ComboBox`, `ListBox`, `ProgressBar`, `TrackBar`,
`ScrollBar`, `GroupBox`, `TabControl` — is owner-drawn on a canvas peer, even though every desktop
ships a perfectly good version of most of them.

**Why that was right, and why it is now limiting.** Drawing them ourselves is what makes one
implementation behave identically on every backend, and it is the only way to get a `DataGridView`
or `CalendarView` at all. But it costs the things only a real widget has: **screen-reader
accessibility**, IME and text-service integration, the OS's own hover/press animation, high-contrast
and per-widget theme overrides, and the last 5% of "feels like this desktop".

**The goal.** For each control with a faithful platform counterpart, realize a *native* peer when the
control's configured properties stay inside what that widget supports, and fall back to the existing
owner-drawn path otherwise. Owner-draw remains the universal path — it is still what runs on a
backend without the widget, and what runs the moment an app asks for something the widget cannot do.

### Design rules (decide these before the first control moves)

- [x] **Capability gate per control.** Each promotable control declares the property subset that keeps
      it native (`CheckBox`: no `Image`, since no platform box renders one beside the caption the way we
      do). Inside the gate → native peer; outside → canvas. Evaluated **at realization**, in the control's
      `CreatePeer` override, so the decision is made once, before a peer exists.
- [x] **Escaping the gate after realization.** The rule is **re-realize**, not ignore: setting a property
      that leaves the gate rebuilds the peer onto the canvas (and re-entering it takes the widget back),
      via `Control.RerealizePeer()` — `RemoveChild` → `DisposePeerTree` → `RealizeAddedChild`. The swap is
      **state-transparent**: managed state survives and keyboard focus is re-established on the new widget
      when the old one held it. The rebuild is
      driven by whether the *outcome* would differ (`IsNativeWidget != WouldBeNative`), not by the gate
      alone, so a control on a declining backend never churns its canvas for nothing — each control caches
      whether the backend has ever offered it a widget.
- [x] **No behavioral fork in the public API.** `Checked`/`CheckedChanged` behave identically either way;
      `NativePeerPromotionTests` asserts the *same* observable behaviour against both paths, including that
      a widget-originated toggle raises the public event exactly once.
- [x] **Opt-in switch.** `Application.PreferNativeWidgets` (default on) plus a per-control override
      (`Control.UseNativeWidget`, packed into the state flags so it costs no per-instance bytes), so an app
      that wants pixel-identical cross-platform rendering can keep everything owner-drawn.
      `ToolStripControlHost` sets it to `false` on what it hosts: a toolbar row is a fixed, deliberately
      short strip, and several desktops will not draw a combo box that small — the widget keeps its own
      minimum and is clipped.
- [x] **A subclass may withdraw.** Eligibility is `private protected virtual` where a control can be
      derived from, because a subclass that paints into a row the platform knows nothing about would lose
      it silently. `CheckedListBox` overrides it to `false` for exactly that reason.
- [x] **Backends may decline.** `IPlatformBackend.CreateCheckBox()` is a **default interface method
      returning `null`**, so a backend opts in by overriding rather than being broken by a new member.
      macOS and the headless test backend decline for free — which is what keeps the paint-level test
      suite on the owner-drawn path.

### Promotion candidates, in payoff order

Both backends ship every promotion below. Win32 is compile-verified and pattern-matched against the
peers already in the backend; the GTK half is additionally asserted on a live X11 and Wayland display by
the autopilot, which checks per control that each eligible one really reached a widget and each gated one
really stayed on the painter.

| Control                         | Win32                                | GTK 3             | Gate — stays native while…                                   |
| ------------------------------- | ------------------------------------ | ----------------- | ------------------------------------------------------------ |
| [x] `CheckBox`                  | `BUTTON` (`BS_AUTOCHECKBOX`)         | `GtkCheckButton`  | no `Image`                                                    |
| [x] `RadioButton`               | `BUTTON` (`BS_RADIOBUTTON`)          | `GtkRadioButton`  | no `Image`                                                    |
| [x] `ProgressBar`               | `msctls_progress32`                  | `GtkProgressBar`  | horizontal only                                               |
| [x] `TrackBar`                  | `msctls_trackbar32`                  | `GtkScale`        | always (ticks are decoration)                                 |
| [x] `HScrollBar` / `VScrollBar` | `SCROLLBAR`                          | `GtkScrollbar`    | always                                                        |
| [x] `GroupBox`                  | `BUTTON` (`BS_GROUPBOX`), hosted     | `GtkFrame`        | no caption image                                              |
| [x] `ComboBox`                  | `COMBOBOX` (`CBS_DROPDOWNLIST`)      | `GtkComboBoxText` | `DropDownList` style, no per-item image, no placeholder       |
| [x] `ListBox`                   | `LISTBOX`                            | `GtkTreeView`     | single selection, no per-item image, default `ItemHeight`     |
| [x] `LinkLabel`                 | `SysLink`                            | `GtkLinkButton`   | always (one link spanning the text is all this control models)|
| [ ] `NumericUpDown`             | `EDIT` + `msctls_updown32`           | `GtkSpinButton`   | no custom formatting delegate                                 |
| [ ] `ToolTip`                   | `tooltips_class32`                   | `GtkTooltip`      | text-only tips                                                |

Two of these needed a decision the obvious reading would have got wrong:

- **Radio grouping stays in the core**, which is also what makes a *mixed* group work — the gate is per
  control, so one button with an `Image` keeps the painter while its siblings are widgets, and the
  selection has to cross that split in both directions.
  Both peers are asked for a *non-automatic* radio. An automatic
  one defines its own group — on Windows from the `WS_GROUP` runs of the tab order, which is a different
  notion of "group" from the core's (the controls sharing a parent) — and the two would fight over the
  selection. GTK additionally refuses to leave a group with nothing selected, so each peer carries a
  private, never-parented group anchor that is activated to mean "none": the core allows a `RadioButton`
  to be cleared outright, and the widget has to allow it too. `MixedRadioButtonGroupTests` pins the mixed
  case specifically, including the one defect it found: re-realizing a control re-establishes its keyboard
  focus, and a radio button selects itself on focus — so the swap handed it a selection it was supposed to
  leave alone. `Control.IsRestoringFocus` tells the two apart.
- **The group box hosts its frame rather than being hosted by it.** Both platforms build it as a plain
  container carrying the control's own coordinate system, with the real frame widget behind everything,
  filling it. Parenting the children *into* the frame would shift them by whatever inset the platform
  reserves — so the same bounds would land in a different place on each rendering path — and on Windows it
  would strand their `WM_COMMAND` notifications at a stock window procedure that discards them.

Asymmetric candidates (one platform only, so the other keeps owner-draw): `ToggleSwitch` →
`GtkSwitch`; `Expander` → `GtkExpander`; `SplitContainer` → `GtkPaned`.

**Deliberately staying owner-drawn:** `DataGridView`, `ListView`, `TreeView`, `TreeListView`,
`CalendarView`, `Ribbon`, `DockPanel`, `MenuStrip`/`ToolStrip`/`StatusStrip`, and everything in
§7.9/§7.10. Their native counterparts either do not exist, differ too much between platforms, or
would not survive the feature set we already ship (15 column kinds, merged rows, virtual mode…).

### Acceptance

- [x] A control with a native peer and its owner-drawn twin pass the **same** behavior test suite.
- [x] The allocation budgets of §4 hold on both paths.
- [x] Every promotion is asserted on a live display, not only headlessly: the peers are pure interop, and
      nothing but a real desktop proves they were built at all.
- [x] The demo's **Native** page builds every promotable control twice from one method with only the pin
      flipped, so the two renderings sit side by side in one screenshot and can be compared directly; a
      third column holds the states that leave the gate. The autopilot asserts, per control, that each pin
      held.
- [ ] A screen reader announces a promoted `CheckBox` on Windows and on Linux (the point of the
      exercise — verified manually, once, per control family).
- [x] `docs/README.md`'s strategy column and each control page's header say which path a control
      takes and what the gate is.

---

## 13. Colour emoji in text on Win32

**Where we are.** GTK gets this for free: `pango_cairo_show_layout` is colour-glyph-capable and picks up
the system emoji font, so `🐣` in a `Text` renders in colour on both the native widgets and the
owner-drawn path — visible in every Linux screenshot in `docs/`. Win32 does not. GDI's `DrawTextW` and
`ExtTextOut` rasterize one monochrome alpha mask per glyph; the COLR/CPAL layer table that Segoe UI Emoji
carries is invisible to them, so the same string comes out as flat outlines. Nothing in GDI, GDI+,
`DrawThemeTextEx` or Uniscribe changes that — colour glyph rendering on Windows lives in DirectWrite.

**The goal.** The same string looks the same on both desktops, without giving up the GDI paint path that
every owner-drawn control already runs on, without a new redistributable, and without spending anything
on the overwhelming majority of strings that contain no emoji at all.

### The shape

- [x] **Detect, then divert.** `Win32Graphics.DrawText`/`MeasureText` first ask a cheap scanner whether
      the string contains anything that could be a colour glyph — a surrogate pair in the U+1F000 range,
      or U+FE0F, or U+2600–U+27BF. No hit (the normal case) → the existing GDI call, byte for byte
      unchanged, no allocation, no new object on the paint path. A hit → the colour path below.
- [x] **`ID2D1DCRenderTarget` bound to the HDC we already have.** `D2D1CreateFactory` →
      `CreateDCRenderTarget` → `BindDC(hdc, rect)` → `DrawText` with
      `D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT`. This is the interop Direct2D exists for: it draws
      straight onto a GDI device context, so the canvas peer, the double buffer and the clip stack all
      stay exactly as they are. One flag is the whole feature.
- [x] **Measure with the renderer that paints.** The colour path measures through `IDWriteTextLayout`
      rather than `GetTextExtentPoint32W`, so hit-testing, caret positions and layout agree with what
      lands on screen. A string that takes the GDI path keeps measuring through GDI — the two never mix
      within one string.
- [x] **COM without COM interop.** No `[ComImport]`, no `Marshal.GetObjectForIUnknown`, no
      `[DllImport]` — none of which survive the rules of §2. Only two entry points are imported
      (`D2D1CreateFactory`, `DWriteCreateFactory`, both `[LibraryImport]`); every interface is reached by
      indexing its vtable and calling through a `delegate* unmanaged<...>`. That is AOT-safe, allocation-
      free per call, and the same technique the backend already uses for GTK signal trampolines.
- [x] **Degrade, never fail.** `d2d1.dll`/`dwrite.dll` missing, factory creation failing, `BindDC`
      refusing the DC: each falls back to the GDI call and latches a flag so the attempt is made once per
      process, not once per paint. Monochrome emoji is a worse rendering, not a broken one.
- [x] **Native widgets need nothing.** A promoted `CheckBox` or `Button` is a real HWND drawn by the OS,
      which has been colour-emoji-capable since Windows 8.1. This work is only about the owner-drawn
      surface — which is why it belongs in `Win32Graphics` and nowhere else.

### Why not the alternatives

| Approach | Why not |
| --- | --- |
| Bundle emoji bitmaps and draw them through `IImage` | Tens of thousands of glyphs; kills the kilobytes goal of §4, and freezes the emoji set at build time |
| `IDWriteFactory2.TranslateColorGlyphRun` + manual layer compositing into a DIB | Correct, but it is re-implementing what `ENABLE_COLOR_FONT` already does, with shaping and bidi to get right by hand |
| Move the whole Win32 paint path to Direct2D | Far larger change, a different resource lifetime model, and it buys nothing for the 99% of drawing that is lines, rectangles and images |
| Render text through a native `STATIC` child per label | Reintroduces a native object per control, which §2's buffered-then-flushed design exists to avoid |

### Acceptance

- [~] A string mixing text and emoji renders in colour on Windows in an owner-drawn control. The divert
      itself is asserted — `Win32NativePromotionTests` checks that a string with an emoji reaches the
      colour path exactly once and one without never does — and the path was driven end to end under wine:
      factories, DC render target, `BindDC`, brush, text format, `DrawText` and `EndDraw` all succeed, and
      the glyphs are shaped by DirectWrite rather than GDI (one glyph per emoji instead of two `.notdef`
      boxes for the two UTF-16 units, and a measurement that changes to match). What is *not* verified here
      is the colour itself: wine's DirectWrite does not render Noto's CBDT table, and the prefix has no
      Segoe UI Emoji. That last step needs a real Windows desktop.
      Running it is what found the one real bug in the interop, so it earned its keep: the pixel format
      asked for `D2D1_ALPHA_MODE` 2, which is `STRAIGHT` and not `IGNORE` — a DC render target rejects it
      with `WINCODEC_ERR_UNSUPPORTEDPIXELFORMAT`, so every string would have silently fallen back forever.
- [x] A string with no emoji produces the identical byte stream from the recording graphics as before,
      proving the fast path is untouched.
- [x] Measurement and painting agree: both go through the same renderer for the same string —
      `IDWriteTextLayout` when the string is diverted, GDI when it is not — so a mixed string is never
      measured by one and painted by the other.
- [x] The AOT publish stays clean, and the toolkit still starts on a Windows build with the DirectWrite
      DLLs unavailable.

---

## 14. What's next — candidate workstreams

Ranked by how much they unblock, not by effort. Nothing here is committed; this section exists so the
next change starts from a considered list instead of an inbox.

1. **Accessibility (the biggest real gap).** 48 owner-drawn controls are invisible to a screen
   reader — they are one canvas with no accessible tree. Options: expose an accessibility bridge from
   `OwnerDrawnControl` (UIA on Windows, AT-SPI on Linux), or lean on §12 promotion for the controls
   that can become real widgets. Realistically both. Without this the toolkit is unusable for anyone
   who needs assistive tech, which also blocks public-sector adoption.
2. **Attribute-driven grids & lists.** Extend the Roslyn generator so one `[GridEditable]` model emits
   `PopulateColumns(DataGridView)` (and later `ListView`) alongside the shipped
   `PopulateGrid(PropertyGrid)` — the `C--FrameworkExtensions` ergonomic, with every member reference
   resolved at compile time instead of by reflection. Specified in **§14**.
3. ~~**`DataGridView` virtual mode.**~~ **Shipped** — `VirtualMode` + `VirtualRowCount` (`-1` probes for
   an unknown size) + `RetrieveVirtualRow`, mirroring `ListView`. Sorting is left to the source while
   virtual, since the grid cannot compare rows it never fetched.
4. **An undo/redo service.** `CodeTextBox`, `PropertyGrid`, `DataGridView` and `CalendarView` each
   want it and none has it. One `IUndoContext` with a command stack, opted into per control, beats
   four bespoke implementations.
5. **Live theming: dark mode and high contrast without a restart.** `ITheme` is queried once;
   `ThemeChanged` exists but nothing re-derives cached brushes/bitmaps from it. The colour-mixer
   bitmaps and every cached gradient need an invalidation path.
6. **Per-monitor DPI.** Geometry is integer device pixels throughout. Moving a window between a 100%
   and a 175% monitor currently does the wrong thing. This is a deep change (scale factor in the
   layout pass, bitmap re-rasterization) and should be planned before more pixel geometry accretes.
7. **A dogfooding sample app.** The §7.10 controls were built for "a file explorer / image editor /
   IDE". Actually shipping a small file explorer (Breadcrumb + NavigationView + virtual ListView +
   TreeView + PropertyGrid) would surface integration bugs the per-control demo cannot, and doubles
   as the honest answer to "can you really build an app with this?".
8. **Keyboard command routing.** Shortcuts are per-control today; there is no application-level
   accelerator table, no chord support, and no single place to ask "what is bound to Ctrl+S?".
9. **`CodeTextBox` depth.** Find & replace, bracket matching, code folding, word wrap, multi-caret —
   in that order. Each is self-contained and independently testable.
10. **Drag & drop between controls.** `AllowDrop` exists on `Control` and `TreeView` has intra-tree
   reordering, but there is no cross-control or cross-application data transfer.
11. **Localization beyond `Strings`.** Day/month names come from the OS, but the toolkit's own
    literals live in one static class with no per-culture resource path and no RTL mirroring of
    owner-drawn layout.

---



## 15. Attribute-driven grids & lists — extend the generator to `DataGridView`

**The reference.** `Hawkynt/C--FrameworkExtensions` (`System.Windows.Forms.Extensions`) drives a
`DataGridView` entirely from attributes on the bound row type: annotate the model, call
`grid.EnableExtendedAttributes()`, set `DataSource`, and the grid configures itself — **column type,
width, sort mode, images, tooltips, per-cell and per-row styling, conditional read-only/hidden/
selectable, row height and click handlers**. Its central idea is worth copying wholesale: **an
attribute never carries a delegate — it carries the *name* of another member on the model**
(`conditionalPropertyName`, `isReadOnlyWhen`, `onClickMethodName`, `imageListPropertyName`). That is
what lets a static annotation express dynamic, per-row behavior.

**Why we can do it better.** There those names are resolved by reflection at run time, so a typo is a
silent no-op or a run-time throw — and reflection is banned here (§1.3). A **source generator** resolves
the same names against the Roslyn symbol model at **compile time**: a misspelled property, a condition
that is not `bool`, or a click method with the wrong signature becomes a build error. Same ergonomics,
strictly better failure mode, AOT-clean.

**The goal.** One `[GridEditable]` model emits both `PopulateGrid(PropertyGrid)` (shipped) and
`PopulateColumns(DataGridView)` (this section), wiring our existing delegate-based column surface —
`ValueSelector`/`ValueSetter`, `CheckedSelector`/`CheckedSetter`, `ProgressSelector`, `ImageSelector`,
`ImagesSelector`, `TooltipSelector`, `CellStyleSelector`, `EnabledSelector`, `ReadOnlyCellSelector`,
`ItemsSelector`, `SortComparison` — from generated, strongly-typed lambdas.

### 15.1 Column types

The reference ships custom column types plus attributes that select them. Ours already has 15 kinds, so
this is mostly a mapping exercise — and we cover more kinds than the reference does.

| Reference column / attribute                                                   | Our `DataGridViewColumnKind`                                                             |
| ------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------- |
| `DataGridViewBoundComboBoxColumn` · `DataGridViewComboboxColumnAttribute`      | `ComboBox` + `ItemsSelector`                                                             |
| `DataGridViewDateTimePickerColumn`                                             | `DateTime`                                                                               |
| `DataGridViewDisableButtonColumn` · `DataGridViewButtonColumnAttribute`        | `Button` + `EnabledSelector`                                                             |
| `DataGridViewImageAndTextColumn` · `DataGridViewImageAndTextColumnAttribute`   | `Text` + `ImageSelector`                                                                 |
| `DataGridViewMultiImageColumn` · `DataGridViewMultiImageColumnAttribute`       | `MultiImage` + `ImagesSelector`                                                          |
| `DataGridViewNumericUpDownColumn` · `DataGridViewNumericUpDownColumnAttribute` | `NumericUpDown`                                                                          |
| `DataGridViewProgressBarColumn` · `DataGridViewProgressBarColumnAttribute`     | `Progress` + `ProgressSelector`                                                          |
| `DataGridViewCheckboxColumnAttribute`                                          | `Check` + `CheckedSelector`/`CheckedSetter`                                              |
| `DataGridViewImageColumnAttribute`                                             | `Text` + `ImageSelector` (image-only cell)                                               |
| — (no reference equivalent)                                                    | `Link`, `MaskedText`, `DomainUpDown`, `Color`, `ListBox`, `CheckedListBox`, `TimePicker` |

- [x] Attributes must be able to select **every one of our 15 kinds**, not only the nine the reference
      covers. `[GridColumnKind]` takes the enum, so every kind is nameable, and the eight a property type
      implies are inferred; `GeneratedColumnKindTests` sweeps the enum and fails if a kind is ever added
      without an annotation route.
- [x] Kind is **inferred from the property type** and overridable by `[GridColumnKind]`: `bool` → `Check`,
      numeric → `NumericUpDown`, `DateTime`/`DateOnly` → `DateTime`, `TimeOnly` → `TimePicker`,
      `Color` → `Color`, `enum` → `ComboBox`, `[Flags]` enum → `CheckedListBox`, else `Text`.

### 15.2 Images

The reference has a richer image model than a single selector, and this is the part of the parity map
that needs new grid capability rather than new plumbing:

| Capability                                      | Reference                                                                                                   | Ours today                                                                                                                                            |
| ----------------------------------------------- | ----------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| Image from an `ImageList` + key/index property  | `imageListPropertyName` + `imageKeyPropertyName`                                                            | expressible — the generated `ImageSelector` closes over our [`ImageList`](controls/imagelist.md)                                                      |
| Image straight from a property                  | `imagePropertyName`                                                                                         | expressible — `ImageSelector`                                                                                                                         |
| Several images per cell                         | `DataGridViewMultiImageColumnAttribute` (+ max size, padding, margin, per-image click and tooltip provider) | `ImagesSelector` + `MaxImageSize`/`ImageGap`/`ImagePadding`/`ImageTooltipSelector`; per-icon click reports its index via `CellContentClick` (shipped) |
| Image *and* text in one cell, with relation     | `textImageRelation` (`ImageBeforeText`, …)                                                                  | `ImageSelector` + `TextImageRelation` (shipped)                                                                                                       |
| Fixed image box + aspect ratio                  | `fixedImageWidth`, `fixedImageHeight`, `keepAspectRatio`                                                    | `ImageSize` + `KeepImageAspectRatio` (shipped)                                                                                                        |
| Conditional image overlay, stackable            | `SupportsConditionalImageAttribute` (`AllowMultiple`)                                                       | `OverlayImagesSelector` (shipped)                                                                                                                     |
| Repeated image N times (rating/severity strips) | `ListViewRepeatedImageAttribute` (list side)                                                                | expressible — `[GridColumnImages]` over a list of the right length                                                                                    |

- [x] Add `TextImageRelation` to `DataGridViewColumn` (we already have the enum, used by
      [`IconLabel`](controls/iconlabel.md) and [`Button`](controls/button.md)) — placed through the shared
      `ContentLayout` helper, so a grid cell arranges its icon exactly like every other icon+text control.
- [x] Add a fixed image box with aspect-ratio control to image-bearing cells (`ImageSize`,
      `KeepImageAspectRatio`; an unset size keeps the historical row-height square).
- [x] Add per-image metadata to `MultiImage` (`MaxImageSize`, `ImageGap`, `ImagePadding`) plus
      `ImageTooltipSelector`; painting and per-icon hit-testing share one metrics helper so they cannot
      drift apart.
- [x] Add a conditional image overlay list, so several conditional badges can stack on one cell
      (`OverlayImagesSelector` + `OverlaySize`; the selector returns only the badges that currently
      apply, so several conditions compose).
- [x] Reach all of it from the model: `[GridColumnImage]`, `[GridColumnImages]`,
      `[GridColumnOverlayImages]`, `[GridColumnImageSize]` and `[GridColumnTextImageRelation]` name a
      member the same way the conditional attributes do, and the generator resolves it at compile time —
      an image property that does not exist, or is not an `IImage`, is `NFG002`/`NFG003` rather than a
      column that silently shows nothing.

### 15.3 The rest of the parity map

| Reference attribute                         | Capability                                                                                                                         | Our target                                       |
| ------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------ |
| `DataGridViewCellDisplayTextAttribute`      | Cell text sourced from another property                                                                                            | `ValueSelector` pointed at the named property    |
| `DataGridViewCellStyleAttribute`            | Per-cell fore/back/format/alignment/wrap — each literal *or* from a named property — gated by `conditionalPropertyName`; stackable | `CellStyleSelector`                              |
| `DataGridViewCellTooltipAttribute`          | Tooltip literal or property-sourced, with format + condition; stackable                                                            | `TooltipSelector`                                |
| `DataGridViewClickableAttribute`            | `onClickMethodName` / `onDoubleClickMethodName` per cell                                                                           | `CellContentClick` routed to the resolved method |
| `DataGridViewColumnSortModeAttribute`       | Per-column sort mode                                                                                                               | `SortMode` (+ `SortComparison`)                  |
| `DataGridViewColumnWidthAttribute`          | Width in pixels, in characters, or sized to a sample string; auto-size mode                                                        | `Width` / `AutoSizeMode`                         |
| `DataGridViewConditionalReadOnlyAttribute`  | Read-only while a named `bool` property is true                                                                                    | `ReadOnlyCellSelector`                           |
| `DataGridViewConditionalRowHiddenAttribute` | Hide the row while a named property is true                                                                                        | `RowHiddenSelector` (shipped)                    |
| `DataGridViewFullMergedRowAttribute`        | Row drawn as one merged heading cell, heading text from a property                                                                 | bind to our existing merged rows                 |
| `DataGridViewRowHeightAttribute`            | Fixed height, or height from a named property, condition-gated                                                                     | `RowHeightSelector` (shipped)                    |
| `DataGridViewRowSelectableAttribute`        | Row selectable only while a named property is true                                                                                 | `RowSelectableSelector` (shipped)                |
| `DataGridViewRowStyleAttribute`             | Per-row fore/back/format + bold/italic/underline/strikeout, literal or property-sourced, condition-gated; stackable                | `CellStyleSelector` applied row-wide             |

Beyond the grid the same library annotates `ListView` — `ListViewColumnAttribute`,
`ListViewColumnColorAttribute`, `ListItemImageAttribute` (image list + key/index),
`ListItemStyleAttribute`, `ListViewRepeatedImageAttribute`. Our [`ListView`](controls/listview.md) has
the matching surface, so it is a cheap follow-on from the same symbol walk: same generator, different
populator.

### 15.4 Design rules

- [x] **One marker, several populators.** `[GridEditable]` emits `PopulateGrid(PropertyGrid)`,
      `PopulateColumns(DataGridView)`, `PopulateColumns(ListView)` and `ToListViewItem()`.
- [x] **Member references are `nameof`-friendly strings, resolved at compile time.** An unresolved name
      is `NFG002` and a wrong-typed one is `NFG003` — both **build errors**. Handler-signature checking
      arrives with the click attributes.
- [x] **Conditions compose — through the model, not through stacked attributes.** The reference makes
      its conditional-image attribute `AllowMultiple` and evaluates the list in order. We reached the same
      capability from the other side: `[GridColumnOverlayImages]` names one member that returns *the badges
      that currently apply*, so several conditions compose in ordinary C# where they can be read, debugged
      and unit-tested, and the order is the order of the returned list. That is one attribute instead of a
      stack of them, and it removes the question of what an attribute's evaluation order even means when
      two of them disagree. The porting table on [`datagridview.md`](controls/datagridview.md) maps the
      reference's spelling onto it.
- [x] **Reuse the inspector vocabulary where the meaning is identical**: `[GridIgnore]` drops a member
      from both populators, `[GridDisplayName]` names both the row and the column header, and
      `[GridRange]` now clamps the grid's numeric editor as well as the inspector's — one model does not
      describe two ranges. `[GridDescription]` stays inspector-only, since a grid has nowhere to put it
      that is not a tooltip, and tooltips are per cell rather than per column here. Grid-only concerns
      keep grid-only attributes.
- [x] **Columns only.** The populator never materializes rows, so it composes with §13's virtual mode.
- [x] **Degrades like the inspector path.** Without the analyzer the attributes still compile and
      hand-built columns still work; only the generated method is absent.

### 15.5 Acceptance

- [x] An annotated model generates a grid whose column kinds, headers, widths, order, images and
      per-row styling match the annotations, asserted headlessly.
- [x] Editing a generated cell writes through to the model instance (`ValueSetter`; a get-only property
      yields a read-only column).
- [~] A misspelled `…PropertyName` (`NFG002`) and a wrong-typed condition (`NFG003`) fail the **build**;
      handler-signature checking lands with the click attributes.
- [x] Every one of the 15 column kinds is reachable from attributes.
- [x] The same model still generates a working `PropertyGrid`.
- [x] `dotnet publish -p:PublishAot=true` on a consumer stays clean.
- [x] [`datagridview.md`](controls/datagridview.md) and [`propertygrid.md`](controls/propertygrid.md)
      document the shared vocabulary, with a porting table from the reference library's names.
