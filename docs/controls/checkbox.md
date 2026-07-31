# CheckBox

> An owner-drawn check box painted in the native theme — themed box, accent checkmark, themed text — that toggles on click or Space.

![CheckBox in the NativeForms demo](../screenshots/01-basics.png)

`Hawkynt.NativeForms.CheckBox` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var check = new CheckBox { Text = "Remember me", Bounds = new(20, 20, 160, 20) };
check.CheckedChanged += (_, _) => Console.WriteLine(check.Checked);
form.Controls.Add(check);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Checked` | `bool` | `false` | Whether the box is checked. `Indeterminate` reads as `true` — the Windows Forms rule, so code asking only the boolean question never mistakes mixed for off. Assigning projects back onto the two plain states. |
| `CheckState` | `CheckState` | `Unchecked` | The three-valued state: `Unchecked`, `Checked`, `Indeterminate`. |
| `ThreeState` | `bool` | `false` | Whether clicking cycles through `Indeterminate` as well. The third state can still be *assigned* without this, which is how a box that summarises a set shows the set disagreeing without inviting the user to pick "mixed" by hand. |
| `Image` | `IImage?` | `null` | An optional icon rendered between the check square and the caption through the shared `ContentLayout`; the text shifts right to make room. |

### Events

| Name | Description |
|---|---|
| `CheckedChanged` | Raised when `Checked` changes — whether toggled by the user or set in code. |
| `CheckStateChanged` | Raised when `CheckState` changes, **including** the moves between checked and indeterminate that leave `Checked` alone. |

Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`).

## Native widget promotion

On a backend that offers one — Win32 and GTK both do — a check box realizes onto a **real platform
widget** (a `BS_AUTOCHECKBOX` button, a `GtkCheckButton`) instead of the owner-drawn canvas, so the desktop draws the indicator, animates
the hover and press, and exposes the control to assistive technology. The public surface is identical
either way; `IsNativeWidget` reports which path was taken.

| Name | Type | Default | Description |
|---|---|---|---|
| `IsNativeWidget` | `bool` (get) | — | Whether the control is currently backed by a real platform check box. |
| `UseNativeWidget` | `bool?` | `null` | Overrides [`Application.PreferNativeWidgets`](application.md) for this control. |

Promotion happens only when the configured properties stay inside what the platform widget can express. An
`Image` is the one thing neither `BS_AUTOCHECKBOX` nor `GtkCheckButton` renders beside the caption the way
this control does, so a box with one stays owner-drawn.

**Setting or clearing `Image` on a live control swaps the peer** rather than silently dropping the image:
the box falls back to the canvas when it gains one and returns to the widget when it loses one, keeping
`Checked` and the rest of its state. Keyboard focus survives the swap — the promotion is state-transparent.
`UseNativeWidget` itself is read at realization only — a form that is already showing
should not change its rendering out from under the user. See
[PRD §12](../PRD.md#12-native-peer-promotion-opt-into-real-widgets-where-the-platform-has-one).

## Notes

- Painted with the platform `ITheme` (`FieldBackground` box, `Accent` checkmark and checked border, `ControlText`/`DisabledText` label), so it matches the host desktop; testable headlessly through the test backend's recording canvas. The 14 px check square itself is drawn by the shared `GlyphRenderer` (`DrawCheckBox`), the same glyph `DateTimePicker`'s check box uses.
- A left mouse-button release inside the bounds toggles `Checked`; so does the Space key when focused (the control is focusable). Each user toggle raises `CheckedChanged` and the inherited `Click`.
- With an `Image`, the icon and text lay out via the shared `ContentLayout` (`ImageBeforeText`, middle-left) in the area right of the glyph; without one, the classic text placement stays untouched.
- **The third state.** The click cycle is unchecked → checked → indeterminate → unchecked under `ThreeState`; a box left indeterminate *without* it clears on the next click rather than sticking, since an application that assigned the state directly otherwise leaves the user no way out. The painted mixed mark is a filled square, not a dimmed check — at fourteen pixels a dimmed check reads as a disabled *checked* box. Each backend keeps its own indicator: `BS_AUTO3STATE`, `gtk_toggle_button_set_inconsistent`, `allowsMixedState`. Win32 and AppKit run the three-step click cycle themselves and the core mirrors what the widget reached; GTK has no such cycle and treats inconsistent as presentation only, so its peer takes that step itself — otherwise the box would skip from checked straight back to unchecked on one desktop out of three.
- `OwnerDrawnControlTests` pin the behavior: two clicks toggle on then off with two `CheckedChanged` events, Space toggles, and the label text is painted. `CheckBoxImageTests` pin the icon placement, the text shift and the invalidation on image change. `CheckBoxThreeStateTests` pin the cycle, the two events and how they differ, and the same behaviour on the promoted native path.

## Differences from System.Windows.Forms.CheckBox

- **No `AutoCheck` opt-out**: the click pipeline always advances the state.
- **`PerformClick()` toggles**: the `Click` pipeline flips `Checked` first, so a programmatic click behaves exactly like a user click (gated by effective `Enabled`/`Visible`).
- **Space toggles on key-up**, matching the native press-then-release feel; there is no `Appearance.Button`, no `CheckAlign`/`TextAlign` placement control.
