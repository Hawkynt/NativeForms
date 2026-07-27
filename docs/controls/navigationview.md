# NavigationView

> An owner-drawn left navigation rail — the modern app-shell side bar: a vertical list of icon + caption items with a hamburger button that collapses the rail to icons only, an accent stripe on the selected item, and a change event so the host swaps the content beside it.

![NavigationView in the NativeForms demo](../screenshots/28-widgets.png)

`Hawkynt.NativeForms.NavigationView` · strategy: **owner-drawn** · peer: `ICanvasPeer`

## Usage

```csharp
var nav = new NavigationView { Bounds = new(0, 0, 170, 400), ImageList = icons };
nav.AddItem("Home", iconHome);
nav.AddItem("Files", iconFolder);
nav.AddItem("Settings", iconGear);
nav.SelectedIndexChanged += (_, _) => ShowPage(nav.SelectedIndex);
form.Controls.Add(nav);
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `Collapsed` | `bool` | `false` | Whether the rail is collapsed to an icons-only strip. Setting it **changes `Width`** — to 44 px when collapsing, back to the previous width when expanding — so the content region reflows. |
| `ImageList` | `ImageList?` | `null` | The icon source for the item images. |
| `Items` | `IReadOnlyList<string>` | empty | The item captions, top to bottom. |
| `PreferredWidth` | `int` | — | The width the rail wants: 44 px while collapsed, otherwise its current width. |
| `SelectedIndex` | `int` | `-1` (`0` once items exist) | The selected item. Setting it repaints and raises `SelectedIndexChanged`. |

### Methods

| Name | Description |
|---|---|
| `AddItem(string text, int imageIndex = -1)` | Appends an item and returns its index. The first item added becomes selected. |

### Events

| Name | Description |
|---|---|
| `CollapsedChanged` | Raised when `Collapsed` changes. |
| `SelectedIndexChanged` | Raised when `SelectedIndex` changes. |

## Notes

- The hamburger occupies the top row; clicking it toggles `Collapsed`.
- The selected row carries a 3-px accent stripe plus a tinted background.
- Up/Down arrows move the selection while focused.

## Differences from WinForms

No equivalent. [`Accordion`](accordion.md) is the nearest shipped control but is an expander stack, not a navigation shell.
