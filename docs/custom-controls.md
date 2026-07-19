# Custom controls

> Subclass `OwnerDrawnControl`, override `OnPaint` and the input hooks, paint exclusively through `ITheme`, call `Invalidate()` on state changes — and the control runs, looks native, and is headlessly testable on every backend.

`Hawkynt.NativeForms.OwnerDrawnControl` · strategy: **owner** · peer: `ICanvasPeer`

## Usage

A minimal interactive control — a click-to-toggle swatch:

```csharp
using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;

public class Swatch : OwnerDrawnControl
{
    private bool _active;

    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
                return;

            _active = value;
            this.Invalidate();
        }
    }

    protected override bool Focusable => true;

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            this.Active = !this.Active;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is not Keys.Space)
            return;

        this.Active = !this.Active;
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(_active ? theme.Accent : theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));
        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
        g.DrawText(this.Text, theme.DefaultFont, theme.ControlText, new Rectangle(0, 0, this.Width, this.Height), ContentAlignment.MiddleCenter);
    }
}
```

Use it like any other control: `form.Controls.Add(new Swatch { Text = "On air", Bounds = new(20, 20, 120, 28) });`.

## How it works

`OwnerDrawnControl` realizes onto a single `ICanvasPeer` — the one paintable, focusable native surface each backend provides (`Win32CanvasPeer`, `GtkCanvasPeer`). On realization it:

- swaps `Theme` from the `DefaultTheme` fallback to the backend's native theme (`IPlatformBackend.Theme`);
- pushes `Focusable` into the peer via `SetFocusable`;
- wires every peer event (`Paint`, `MouseDown/Up/Move/Wheel/Leave`, `KeyDown/Up/Press`, `GotFocus`/`LostFocus`) to the matching protected `On*` virtual.

You never touch the peer. State changes call `Invalidate()` (or `Invalidate(Rectangle)` for a sub-region); the platform schedules a repaint and calls back into `OnPaint` with an `IGraphics` surface and a clip rectangle. `OnTextChanged` already invalidates for you.

The canvas is also a **container** (`IContainerPeer`): an owner-drawn control can host real native children on top of its painted surface. That is how `Panel`, `GroupBox` and `TabPage` parent buttons and text boxes — you just add to `Controls`, the realization walk does the rest (see [architecture.md](architecture.md)).

## ContextMenuStrip and ToolTip come for free

Two component integrations are wired into the owner-drawn mouse pipeline, so a custom control gets them without writing a line:

- **Context menu.** Assign a [`ContextMenuStrip`](controls/control.md) to `Control.ContextMenuStrip` and a right-click on the control opens it at the cursor — the base class routes `MouseDown` with the right button into `menu.Show(this, e.Location)` after your own `OnMouseDown` ran.
- **Tool tips.** `ToolTip.SetToolTip(control, "…")` observes the control's canvas mouse traffic through internal mirror events (raised *after* your `OnMouseMove`/`OnMouseLeave`/`OnMouseDown`), shows a themed popup near the cursor after `InitialDelay`, and hides it on leave, press or `AutoPopDelay`.

Both integrations are owner-drawn-only today: native-widget controls (Button, TextBox …) need right-click/hover events on their peers first — tracked in [PRD.md](PRD.md).

## Shared paint primitives

Several themed visuals recur across controls, so they live in reusable helpers instead of being re-derived per control. All of them are `internal` — toolkit controls use them freely, external subclasses cannot yet (the public native-style primitive surface is an open box in [PRD.md](PRD.md) §5):

- `Drawing.GlyphRenderer` — the check glyph (`CheckBox` and grid check cells), the progress fill (`ProgressBar` and progress cells), a push-button face, sort arrows and the current-row marker. Stroke/fill only, no allocation, safe on the paint path.
- `Drawing.ContentLayout` — the shared icon+text geometry: image and text sizes are stacked per `TextImageRelation`, anchored per `ContentAlignment`, clipped to the bounds. Pure geometry (the caller measures and draws), so image placement behaves identically in `CheckBox`, `RadioButton`, `GroupBox` captions, `PictureBox` and the native button/label peers.
- **Two scrollbar renderers** — an honest wart. `Drawing.ScrollBarRenderer` speaks extent/viewport/position and paints the minimal track+thumb bars `Panel.AutoScroll` uses; `Hawkynt.NativeForms.ScrollBarRenderer` (next to the `ScrollBar` control) speaks minimum/maximum/value/largeChange and adds arrow buttons, channel paging and hit-testing. Unifying them into one implementation is an explicit debt box in [PRD.md](PRD.md) §7.5.

## Painting `ImageList` images

`ImageList` stores raw ARGB pixels backend-free; the native `IImage` for an entry is materialized lazily against a concrete backend. A realized control reaches its backend through the internal `Control.Backend` property and calls `imageList.GetImage(index, backend)` inside `OnPaint` — first use creates and caches the native bitmap, later frames are lookups. `ComboBox`, `TreeView`, `TabControl` and the tool-strip items all paint through this seam; before realization (no backend yet) controls simply skip the image.

## Worked example: CheckBox

`NativeForms.Core/Controls/CheckBox.cs` is the pattern in ~110 lines. The essentials:

- **State + invalidate + event.** `Checked` compares, stores, calls `this.Invalidate()`, then raises `CheckedChanged`. Never paint directly from a setter — invalidate and let the platform drive `OnPaint`.
- **Focus.** `protected override bool Focusable => true;` — interactive controls opt in; purely visual ones (`ProgressBar`) keep the `false` default.
- **Input.** `OnMouseUp` toggles when the left button releases inside `new Rectangle(0, 0, this.Width, this.Height)`; `OnKeyDown` toggles on `Keys.Space` and sets `e.Handled = true`.
- **Paint, entirely through the theme and the shared primitives:**

```csharp
protected override void OnPaint(PaintEventArgs e)
{
    var g = e.Graphics;
    var theme = this.Theme;
    g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));

    var boxTop = Math.Max(0, (this.Height - GlyphRenderer.CheckBoxSize) / 2);
    var box = new Rectangle(0, boxTop, GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize);
    GlyphRenderer.DrawCheckBox(g, theme, box, this.Checked);

    var content = new Rectangle(GlyphRenderer.CheckBoxSize + _TextGap, 0, this.Width - GlyphRenderer.CheckBoxSize - _TextGap, this.Height);
    var textColor = this.Enabled ? theme.ControlText : theme.DisabledText;
    // … optional Image via ContentLayout.Arrange, then …
    g.DrawText(this.Text, theme.DefaultFont, textColor, content, ContentAlignment.MiddleLeft);
}
```

## Theming rules

Paint with `ITheme`, never with hard-coded colors — that is the whole trick that makes an owner-drawn control look native. The backend populates the theme from the OS (system colors and non-client metrics on Win32, `GtkStyleContext` and `gtk-font-name` on GTK); `DefaultTheme.Instance` is the neutral light fallback used headlessly and before realization. Use `Accent` for checkmarks/fills/focus, `ControlBackground`/`FieldBackground` for surfaces, `ControlText`/`DisabledText` for captions, `Border`/`GridLine` for outlines, `DefaultFont` for text, and `RowHeight`/`ScrollBarSize` for metrics.

## Testing headlessly

`NativeForms.Tests/Fakes/HeadlessBackend.cs` is an `IPlatformBackend` whose `Run` returns immediately and whose `HeadlessCanvasPeer` lets a test raise the native events itself; `RaisePaint()` returns a `RecordingGraphics` that logs every draw call. The pattern from `NativeForms.Tests/OwnerDrawnControlTests.cs`:

```csharp
private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
{
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(control);
    Application.Run(form, backend);            // returns immediately — no display needed
    return backend.Created.OfType<HeadlessCanvasPeer>().Single();
}

[Test]
public void CheckBox_toggles_on_click_and_raises_events()
{
    var check = new CheckBox { Text = "Enable", Bounds = new(0, 0, 120, 20) };
    var canvas = Realize(check);

    canvas.RaiseMouseUp(5, 10);
    Assert.That(check.Checked, Is.True);
}

[Test]
public void CheckBox_paints_label_text()
{
    var check = new CheckBox { Text = "Remember me", Bounds = new(0, 0, 160, 20) };
    var canvas = Realize(check);

    var g = canvas.RaisePaint();
    Assert.That(g.DrewText("Remember me"), Is.True);
}
```

Input goes in through `RaiseMouseDown/Up/Move/Wheel` (with optional button and modifier arguments), `RaiseKeyDown/Up`, `RaiseKeyPress`, `RaiseMouseLeave`, `RaiseGotFocus/LostFocus`; output comes back as `RecordingGraphics.Operations` strings (`"fill …"`, `"rect …"`, `"text …"`).

## API

`OwnerDrawnControl` (on top of [`Control`](controls/control.md)):

| Member | Kind | Description |
|---|---|---|
| `Theme` | `protected ITheme` | The theme to paint with; `DefaultTheme.Instance` until realized, then the backend's native theme |
| `Focusable` | `protected virtual bool` (default `false`) | Override to `true` so the surface takes keyboard focus |
| `Invalidate()` / `Invalidate(Rectangle)` | method | Request a repaint of the whole surface or a sub-region; safe no-ops before realization |
| `Focus()` | method | Move keyboard focus to this control |
| `OnPaint(PaintEventArgs)` | `protected virtual` | Draw through `e.Graphics`, clipped to `e.ClipRectangle` |
| `OnMouseDown/Up/Move/Wheel(MouseEventArgs)`, `OnMouseLeave(EventArgs)` | `protected virtual` | Pointer input in client coordinates; `MouseEventArgs` carries the modifier keys |
| `OnKeyDown/Up(KeyEventArgs)`, `OnKeyPress(KeyPressEventArgs)` | `protected virtual` | Keyboard input while focused; set `Handled` to consume |
| `OnGotFocus/OnLostFocus(EventArgs)` | `protected virtual` | Focus transitions |

`IGraphics` — the immediate-mode surface passed to `OnPaint` (GDI on Win32, Cairo/Pango on GTK):

| Method | Description |
|---|---|
| `FillRectangle(Color, Rectangle)` / `DrawRectangle(Color, Rectangle, int)` | Solid fill / outline |
| `FillEllipse(Color, Rectangle)` / `DrawEllipse(Color, Rectangle, int)` | Ellipse inscribed in bounds |
| `DrawLine(Color, int, int, int, int, int)` | Straight line with thickness |
| `DrawText(string, Font, Color, Rectangle, ContentAlignment)` | Aligned text in the native font |
| `MeasureText(string, Font)` | Pixel size of a string (also on `IPlatformBackend` for measuring between paints) |
| `DrawImage(IImage, Rectangle)` | Image scaled into bounds |
| `PushClip(Rectangle)` / `PopClip()` | Clip-region stack |

`ITheme` — colors: `WindowBackground`, `ControlBackground`, `ControlText`, `DisabledText`, `FieldBackground`, `Accent`, `SelectionBackground`, `SelectionText`, `Border`, `GridLine`, `HeaderBackground`, `HeaderText`; plus `DefaultFont` (`Font` struct), `RowHeight`, `ScrollBarSize`.

`IImage` — an opaque backend-owned bitmap (`Width`, `Height`, `IDisposable`), created decoder-free from 32-bit ARGB pixels via `IPlatformBackend.CreateImage(width, height, argb)`.

## Notes

**Allocation rules.** No per-frame allocation on the paint path: `OnPaint` runs on every repaint, so no string building, no LINQ, no captured closures, no `new` of reference types there — `Rectangle`/`Point`/`Size`/`Font` are value types and cost nothing. Construction is budgeted too: `AllocationBudgetTests` asserts an owner-drawn control allocates under 768 bytes.

**Realization.** Like every control, state set before `Application.Run` is buffered and flushed at realization; `Invalidate()`/`Focus()` before realization are safe no-ops. Owner-drawn containers realize their children too — see [controls/control.md](controls/control.md).

**Theming.** A control that only touches `Theme` members inherits native looks — and will inherit dark mode when theme-change notifications land.

**Not yet implemented.** Double buffering, per-monitor DPI, light/dark change notifications, rounded rects, and a *public* native-style primitive surface (button/radio/arrow/header/scrollbar helpers) are open boxes in [PRD.md](PRD.md) §5; unifying the two internal scrollbar renderers is a debt box in §7.5; mnemonic-aware layout and item-cell adoption of `ContentLayout` are pending in §5.
