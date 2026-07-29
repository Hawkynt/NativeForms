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
| Native widget promotion (§12) | 9 controls | 9 controls | 4: check box, radio, progress, group box |
| Colour emoji in owner-drawn text | via Pango | via Direct2D/DirectWrite | via CoreText |
| Accessibility | ATK | MSAA, borrowed from a shadow control | NSAccessibility |
| Mouse & keyboard | complete | complete | press, drag, wheel, keys; hover wired, not witnessed end to end |
| Dialogs (message box, file, colour, font) | complete | complete | message box and file chooser native (`NSAlert`, `NSOpen`/`NSSavePanel`); colour and font answer as if cancelled |
| CI verification | autopilot, 160 checks, gating | 16-page shoot + real `SendInput`, gating | 16-page shoot, reporting; `CGEvent` injection skips — a runner grants no Accessibility permission |

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
to the owner-drawn twin and swaps back when you take it away. Cocoa promotes four of the nine — the
check box, the radio button, the progress bar and the group box — on top of `Label`, `Button` and
`TextBox`, which are always native there; everything else is owner-drawn.

## The macOS backend, specifically

It is genuinely incomplete, and the table above says so rather than leaving you to discover it.

Working: AppKit loads, windows open, the view hierarchy builds with the toolkit's coordinates,
the event loop runs, owner-drawn painting and text render through CoreGraphics and CoreText, images
and text measurement work, the clipboard works both ways, a multiline `TextBox` is a real
`NSTextView` in an `NSScrollView`, and the gallery's sixteen pages all pass the walkthrough's state
round-trip and layout audit.

Not working yet: the colour and font choosers answer as if cancelled — both are shared modeless
panels on macOS, which is a different shape from this seam's blocking call. Mouse and keyboard events
are routed into the toolkit but not verified end to end the way the Win32 backend's are.

Hover is asked for in both places AppKit needs it asked. A window generates no mouse-moved events
until `setAcceptsMouseMovedEvents:` says so, and even then it sends `mouseMoved:` to whichever view
holds the keyboard rather than to the one under the pointer — so each canvas carries an
`NSTrackingArea` as well, in-visible-rect so it follows the view when the layout moves it and
active-always because a menu surface is never the key window. The same area is what delivers
`mouseEntered:`/`mouseExited:`, so a highlight goes out again. What the probe can show is the wiring:
it reads back off the running window whether moved events are accepted and how many views carry a
tracking area — 187 of the gallery's, one per owner-drawn canvas; the rest are AppKit's own controls,
which track themselves. (Only the tracked count is worth reading: the total moves run to run with how
much of the tab strip has realized by the time the shutter arms.) What it cannot show is delivery: the
window server drops this job's injected pointer for want of an Accessibility grant, so hover is stated
here as wired rather than as witnessed.

Four of PRD §12's nine promotions are served: `CheckBox` and `RadioButton` become an `NSButton` in
its switch and radio types, `ProgressBar` an `NSProgressIndicator`, and `GroupBox` an `NSBox` filling a
plain flipped view that holds the children on top of it — the frame and the caption come from the
desktop, and the children keep the bounds the application gave them rather than being shifted by the
inset AppKit reserves. The five that decline do so on purpose — a combo box, a list box, a track bar, a
hyperlink and a scroll bar each carry state AppKit's nearest object does not hold, and a half-answer
would show; the seam is built so that returning nothing keeps the owner-drawn twin, which already
works. Grouping for radios stays in the core; AppKit's own rule is the same one (buttons sharing a
superview), so the two cannot reach different answers.

Colour emoji need nothing: `CTLineDraw` renders them in colour on the same path as every other
glyph, so the bell in the gallery's toggle-switch caption arrives without the second text engine the
Win32 backend has to reach for. That is worth stating because this page claimed otherwise for as long
as nobody looked at a macOS screenshot closely enough to notice.

Accessibility goes through `NSAccessibility`: a label, a help string and a role on the view. A real
AppKit control already answers for itself, so this mostly refines what the platform knows — except on
an owner-drawn canvas, which is one unlabelled rectangle of pixels to a screen reader and where it is
the only description there will ever be. A role macOS has no word for is left alone rather than
guessed at.

Popups do light-dismiss. There is no pointer grab behind it — AppKit's own route to an event before
dispatch is a block, which the interop rules keep out of this assembly — so the application's event
loop makes the decision instead, offering every press to the deepest open surface first. A press
outside it is offered to the owner (which is how a menu cascade routes a click on a shallower level of
itself) and otherwise closes it, and either way it is swallowed rather than also landing on whatever
was underneath.

`RichTextBox` reads and writes real RTF, through AppKit's own parser and writer rather than the
toolkit's subset — a document arrives as attributes on the text storage instead of as the readable
text with its formatting thrown away. Bold, italic, underline, strikethrough, colour, size, alignment
and bullets all reach that storage. Two limits are worth stating rather than discovering: a style
applied with nothing selected does nothing (Windows Forms would hold it for the next characters
typed), and a selection spanning two typefaces takes the one it starts in, because walking the runs
means `enumerateAttribute:…usingBlock:` and a block is exactly the Objective-C object this backend's
interop rules keep out. Zoom goes through the enclosing scroll view's magnification, which is
absolute — the text view's own `scaleUnitSquareToSize:` multiplies whatever scale it already carries,
so an absolute factor served that way would drift with every call. The link-activation event is still
unwired.

The class-at-construction rule no longer shows anywhere in the text box. AppKit picks between
`NSTextField`, `NSSecureTextField` and an `NSTextView` in an `NSScrollView` when the object is made, so
a box told to mask or to go multiline after realization gets the other object built and swapped into
its superview, carrying its text, frame, placeholder and flags across — the parent's child order is
kept and the core never learns that the widget it holds is a different one. The one combination with
no class behind it is a masked multiline box: AppKit's secure editing lives in the field rather than
in the text view, so the wish is kept and applied if the box ever goes back to a single line.

## How the screenshots are produced

`--shoot` walks the tab strip through the tab control's own `SelectedIndex` rather than by
synthesizing clicks, which is what lets it run on every backend: the autopilot's input injection is
`gdk_test_simulate_*` and stops at the Linux border. Each page is captured in-process — `gtk_widget_draw`
into a Cairo surface, GDI into a DIB section, `cacheDisplayInRect:` into an `NSBitmapImageRep` — because
a CI runner has no desktop session to point a screenshot tool at, and because asking the widgets to
paint gives the toolkit's own output rather than whatever was stacked on a desktop.

Each backend finds the window its own way, and on macOS the obvious way is wrong: `mainWindow` and
`keyWindow` are both nil while the application is inactive, which on a runner it is for the first
several seconds — nothing competes for the focus, so nothing hurries the window server along. The
shot count wandered between none and fifteen of sixteen until the capture started picking the window
out of `[NSApp windows]`, which does not depend on activation.

The macOS probe closes with a census of the classes the finished window is actually built from, read
back off the live view tree. Promotion is the one claim here a screenshot cannot settle — a promoted
control and the owner-drawn twin it replaces are meant to look alike, since the twin is drawn from the
same theme — so the class name is where the difference shows, and the census is taken after the last
page because a tab page realizes its children only once it has been shown.

Regenerate the comparison strips with the three artifacts side by side; the CI jobs
`screenshots (windows)` and `macos probe` upload theirs on every push.

Putting them next to each other is worth the trouble on its own: the macOS window laying its children
out bottom-up — menu bar at the foot, tab strip off the top edge — was invisible in the macOS shot
alone, obvious the moment it stood beside the other two, and could not have been caught by any test
in the suite.
