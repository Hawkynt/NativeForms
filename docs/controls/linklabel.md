# LinkLabel

> An owner-drawn hyperlink label: its whole text paints in the theme's accent color with an underline, shows the hand cursor and shifts subtly while hovered, and raises `LinkClicked` on a click inside the text or on Enter/Space when focused.

![LinkLabel in the NativeForms demo](../screenshots/01-basics.png)

`Hawkynt.NativeForms.LinkLabel` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var link = new LinkLabel { Text = "Release notes", Bounds = new(20, 20, 160, 24) };
link.LinkClicked += (_, _) =>
{
    OpenBrowser("https://example.test/notes");
    link.LinkVisited = true;
};
form.Controls.Add(link);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `LinkVisited` | `bool` | `false` | Whether the link has been followed; blends the paint color halfway toward the theme's grey. Not set automatically — flip it in your `LinkClicked` handler. |
| `Visited` | `bool` | `false` | Alias for `LinkVisited` (the pre-rename spelling); reads and writes the same flag. Prefer `LinkVisited`. |

### Events

| Name | Description |
|---|---|
| `LinkClicked` | Raised when the link is activated: a left mouse-button release inside the text extent, or Enter/Space while focused. |

A mouse activation also raises the inherited `Click` (Click first, then `LinkClicked`), matching WinForms; keyboard activation raises `LinkClicked` only. Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of `OwnerDrawnControl` (`Invalidate`, `Focus`). The inherited `Text` is the link text.

## Native widget promotion

| Name | Type | Default | Description |
|---|---|---|---|
| `IsNativeWidget` | `bool` (get) | — | Whether the control is currently backed by a real platform hyperlink. |
| `UseNativeWidget` | `bool?` | `null` | Overrides [`Application.PreferNativeWidgets`](application.md) for this control. |

Nothing gates it: one link spanning the whole caption is all this control models, and both platforms carry
that plus the visited flag. (Per-character link ranges — the WinForms `LinkArea` — remain a non-goal, and
are the thing that would need a gate if they arrived.)

**Neither platform is allowed to follow the link itself**, because `LinkClicked` is the application's hook:
GTK is handed an empty URI and told the activation was handled, and the Windows `SysLink` gets an anchor
with no `href`. An activation raises `Click` and then `LinkClicked`, the same pair the owner-drawn mouse
path raises.

`UseNativeWidget` itself is read at realization only — a form that is already
showing should not change its rendering out from under the user. See [PRD §12](../PRD.md#12-native-peer-promotion-opt-into-real-widgets-where-the-platform-has-one).

## Notes

- Painted with the platform `ITheme`: `Accent` text with an underline drawn under the measured text extent, over the `ControlBackground`. Hovering the text shifts the color 30 % toward `ControlText`; `LinkVisited` blends it 50 % toward `DisabledText` first.
- Hit testing is precise: clicks (and hover) count only inside the rectangle the middle-left-aligned text actually occupies, not the whole control bounds — a click in the empty space to the right of a short caption does nothing. Hovering that rectangle switches the cursor to `Cursors.Hand`; leaving resets it.
- The control is focusable; Enter and Space activate it (it claims Enter, so a focused link wins over the form's `AcceptButton`). `LinkLabelTests` pin painting, hit testing, hover/visited color shifts and the keyboard path headlessly.
- Not yet implemented (see [docs/PRD.md](../PRD.md) §7.3): per-character link ranges (WinForms `LinkArea`) — the entire text is the link.

## Differences from System.Windows.Forms.LinkLabel

- **The whole text is the link** — no `LinkArea`, no `Links` collection, and `LinkClicked` carries plain `EventArgs`, not `LinkLabelLinkClickedEventArgs`.
- **Colors are theme-bound, not settable**: no `LinkColor`/`VisitedLinkColor`/`ActiveLinkColor`/`LinkBehavior` — the accent, hover shift and visited blend come from the platform theme so the link matches the host desktop.
- `LinkVisited` matches the WinForms name (`Visited` remains as an alias); the hand cursor and the Click-then-LinkClicked mouse order match WinForms; keyboard activation adds Enter to WinForms' Space-only model.
