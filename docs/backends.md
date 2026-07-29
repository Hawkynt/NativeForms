# Backends: what differs, and what it looks like

One `Control` tree, three native implementations. This page is the honest account of where they
diverge — because they do, and a toolkit that claims otherwise is hiding something you will find on
the day you ship.

The screenshots are the same gallery, the same page, taken by the same walkthrough on each platform:
`dotnet run --project NativeForms.Demo -- --shoot <dir>` writes one PNG per page using each backend's
own in-process capture. Nothing is staged, and nothing is a mock-up.

## At a glance

| | **GTK 3** (Linux) | **Win32** (Windows) | **Cocoa** (macOS) |
|---|---|---|---|
| Status | Complete | Complete | **Under construction** |
| Owner-drawn painting | Cairo | GDI | CoreGraphics |
| Text measurement & drawing | Pango | GDI, DirectWrite for colour glyphs | CoreText |
| Native widget promotion (§12) | 9 controls | 9 controls | none yet |
| Colour emoji in owner-drawn text | via Pango | via Direct2D/DirectWrite | not yet |
| Accessibility | ATK | MSAA, borrowed from a shadow control | not yet |
| Mouse & keyboard | complete | complete | routed; injection checked in CI |
| Dialogs (message box, file, colour, font) | complete | complete | answer as if cancelled |
| CI verification | autopilot, 160 checks, gating | 16-page shoot + real `SendInput`, gating | 16-page shoot + real `CGEvent`, reporting |

## Side by side

Left to right: GTK 3, Win32, Cocoa.

### DataGridView

![The Grid page on all three backends](screenshots/backends/03-grid.png)

### Widgets

![The Widgets page on all three backends](screenshots/backends/13-widgets.png)

## Where the differences come from

**Chrome belongs to the platform.** The Win32 shot carries a title bar because the capture is of the
window; GTK and Cocoa capture the client area. None of that is the toolkit's doing, and none of it is
worth normalising — a window that looks foreign on its own desktop is the failure this project exists
to avoid.

**Fonts and metrics differ, so layout differs.** Each backend measures with the platform's own text
engine, so the same label is a different number of pixels wide on each. Controls that size to their
content therefore differ slightly in width between the columns above. This is deliberate: measuring
with anything other than the engine that will draw the text produces layout that is wrong everywhere
rather than consistent everywhere.

**Scroll bars, check marks and focus rings are the desktop's, not ours.** Owner-drawn controls query
`ITheme` for colours, metrics and fonts, so a check box drawn by the toolkit follows the same palette
as one drawn by the platform. The shapes are ours; the values are the desktop's.

**Native promotion changes what is drawn at all.** On GTK and Win32 nine controls realise onto real
platform widgets when nothing in their state needs the painter (PRD §12) — a `Button` is a real
`GtkButton` or `BUTTON`, until you give it an image the platform cannot draw, at which point it swaps
to the owner-drawn twin and swaps back when you take it away. Cocoa offers none of this yet, so every
control there is owner-drawn except `Label`, `Button` and `TextBox`, which are always native.

## The macOS backend, specifically

It is genuinely incomplete, and the table above says so rather than leaving you to discover it.

Working: AppKit loads, windows open, the view hierarchy builds with the toolkit's coordinates,
the event loop runs, owner-drawn painting and text render through CoreGraphics and CoreText, images
and text measurement work, the clipboard works both ways, and the gallery's sixteen pages all pass
the walkthrough's state round-trip and layout audit.

Not working yet: no native-widget promotion, no accessibility, dialogs answer as if cancelled rather
than opening, multiline text is a single-line field, rich text shows its plain text, popups do not
light-dismiss, and while mouse and keyboard events are routed into the toolkit they are not yet
verified end to end the way the Win32 backend's are.

## How the screenshots are produced

`--shoot` walks the tab strip through the tab control's own `SelectedIndex` rather than by
synthesizing clicks, which is what lets it run on every backend: the autopilot's input injection is
`gdk_test_simulate_*` and stops at the Linux border. Each page is captured in-process — `gtk_widget_draw`
into a Cairo surface, GDI into a DIB section, `cacheDisplayInRect:` into an `NSBitmapImageRep` — because
a CI runner has no desktop session to point a screenshot tool at, and because asking the widgets to
paint gives the toolkit's own output rather than whatever was stacked on a desktop.

Regenerate the comparison strips with the three artifacts side by side; the CI jobs
`screenshots (windows)` and `macos probe` upload theirs on every push.

Putting them next to each other is worth the trouble on its own: the macOS window laying its children
out bottom-up — menu bar at the foot, tab strip off the top edge — was invisible in the macOS shot
alone, obvious the moment it stood beside the other two, and could not have been caught by any test
in the suite.
