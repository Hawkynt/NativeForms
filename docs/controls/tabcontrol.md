# TabControl / TabPage

> An owner-drawn tab control: a themed header strip — caption and optional icon per tab, accent
> underline on the active tab, hover feedback, scroll arrows on overflow — over pages that are real
> nested containers hosting native child peers. Exactly one page is visible at a time.

`Hawkynt.NativeForms.TabControl` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var tabs = new TabControl { Bounds = new(10, 10, 300, 200) };

var general = new TabPage("General");
general.Controls.Add(new Button { Text = "Apply", Bounds = new(10, 10, 80, 24) });
tabs.TabPages.AddRange(general, new TabPage("Advanced"));

tabs.SelectedIndexChanged += (_, _) => Console.WriteLine(tabs.SelectedIndex);
form.Controls.Add(tabs);
```

Icon headers use the shared `ImageList` pattern — raw-ARGB images referenced per page:

```csharp
var icons = new ImageList(16);            // 16×16 icons
general.ImageIndex = icons.Add(iconArgb); // int[] of width*height ARGB pixels
tabs.ImageList = icons;
```

## API

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `TabPages` | `TabPageCollection` | empty | The pages, in tab order. Adding parents the page into `Controls`; the first page added becomes the selected one. |
| `ImageList` | `ImageList?` | `null` | The icons referenced by each page's `ImageIndex`. |
| `SelectedIndex` | `int` | `-1` | Index of the visible page, `-1` while there are no pages. Out-of-range values coerce to `-1`. |
| `SelectedTab` | `TabPage?` | `null` | The visible page; setting selects by `IndexOf`. |
| `Alignment` | `TabAlignment` | `Top` | Which edge the header strip runs along — `Top`, `Bottom`, `Left` or `Right`. |
| `ShowCloseButtons` | `bool` | `false` | Whether each tab paints an × close button that the caption makes room for. |
| `HeaderHeight` | `int` (get) | theme row height + 6 | Pixel height of a horizontal header strip (and the height of each stacked side tab). |

### Events

| Event | Description |
|---|---|
| `SelectedIndexChanged` | Raised when `SelectedIndex` changes — including when removing the selected page hands the selection to a neighbor. Not raised when re-assigning the current index. |
| `TabClosing` | Raised when a tab's close button is pressed, before the page is removed; a handler can veto by setting `Cancel`. |
| `TabClosed` | Raised after a close button removed its page. |

`TabPageCollection` is an `IReadOnlyList<TabPage>` with `Add`, `AddRange`, `Remove`, `Clear` and
`IndexOf`. Inherits the common members of [`Control`](control.md), plus the owner-drawn surface of
`OwnerDrawnControl` (`Invalidate`, `Focus`).

### TabPage

One page: a [`Panel`](panel.md) whose inherited `Text` is the tab caption and whose children fill
the tab control's content area. Constructors: `TabPage()` and `TabPage(string text)`.

| Property | Type | Default | Description |
|---|---|---|---|
| `ImageIndex` | `int` | `-1` | Index of this page's icon in the owning `TabControl.ImageList`, `-1` for none. Painted before the caption in the header. |

## Notes

- Pages are real nested children — each realizes its own canvas peer, and its children realize as
  native peers inside it. Switching pages only flips peer visibility; removing a page disposes its
  peer tree and restores its stand-alone `Visible` default.
- The tab control owns each page's bounds: the content area below the header is re-applied on
  realization, on resize and on every switch. Changing a page's `Text` or `ImageIndex` repaints the
  header.
- `Alignment` moves the strip to any edge. `Top`/`Bottom` lay the tabs out horizontally with widths
  measured from the captions; `Left`/`Right` run a vertical strip whose width is the widest caption
  and stack the tabs as themed rows, drawing each caption horizontally (the toolkit has no
  rotated-text primitive, so side tabs read left-to-right rather than rotated). The active tab's
  accent bar always sits on the edge that faces the content.
- When the tabs outgrow the strip they appear with two arrow buttons at its trailing end — left/right
  for a horizontal strip, up/down for a vertical one — that scroll one tab per click; the strip snaps
  back to the first tab once everything fits again.
- Keyboard (the control is focusable): Ctrl+Tab / Ctrl+Shift+Tab cycle with wraparound, Left/Right
  move the selection without wrapping. A left-click on a header selects its tab; clicks below the
  header go to the page.
- Header hit zones come from the most recent paint, which is when tab captions are measured.
- Painted with the platform `ITheme` (`HeaderBackground`, `ControlBackground`, `Accent`, `Border`,
  `HeaderText`, `ControlText`, `DefaultFont`); testable headlessly through the test backend's
  recording canvas.
- `ShowCloseButtons` gives every tab an × the caption reserves room for; a click raises the
  cancelable `TabClosing`, and unless a handler vetoes it the page is removed and `TabClosed` follows.
  The close box is hit-tested ahead of selection, so clicking the × closes rather than selects.

## Differences from System.Windows.Forms.TabControl

- **No `Selecting`/`Deselecting`** — page switches cannot be vetoed; `SelectedIndexChanged` is the
  only selection event.
- **`Alignment` covers all four edges but `Left`/`Right` draw captions horizontally** rather than
  rotating them (no rotated-text primitive); **no `Multiline`** (a single strip that scrolls on
  overflow) and **no `GetTabRect`** — header hit zones are internal.
- **`Controls.Add` routes into `TabPages`**: adding a `TabPage` through either collection keeps both
  in sync, and adding a non-`TabPage` child throws `InvalidOperationException` — put content on a
  page, exactly like WinForms' designer enforces.
