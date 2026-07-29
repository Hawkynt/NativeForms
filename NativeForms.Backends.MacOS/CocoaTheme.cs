using System.Drawing;
using Hawkynt.NativeForms.Drawing;
using Font = Hawkynt.NativeForms.Drawing.Font;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// The native macOS theme: the palette from <c>NSColor</c>'s semantic colours, the UI font from
/// <c>NSFont</c>'s system font, and the double-click interval from <c>NSEvent</c>. Snapshotted once at
/// construction, like the other two backends' themes.
/// </summary>
/// <remarks>
/// <para>
/// Until now this backend served <see cref="DefaultTheme"/> — a Windows palette and Segoe UI at 9pt,
/// on a desktop that has neither. That is not only a look: a point is a pixel here and 96 dpi's worth
/// of them on Windows, so everything owner-drawn photographed a quarter smaller than the text beside
/// it in a real <c>NSTextField</c>, and an auto-sized label measured with the theme's font and drawn
/// with AppKit's came out too narrow for its own caption.
/// </para>
/// <para>
/// Every read is guarded by <c>respondsToSelector:</c> and falls back to the shared theme's value.
/// Some of these colours arrived in 10.14 and an unrecognized selector does not return nil, it aborts
/// the process — and a backend that refuses to start on an older system in order to get a border
/// colour right has made a bad trade.
/// </para>
/// <para>
/// Every colour leaves here opaque, composited onto the window's own — which is what the other two
/// backends hand over and what an owner-drawn control assumes. Several of these are translucent:
/// <c>separatorColor</c> is a tenth of an opaque black. Filling with one of those directly comes out
/// right either way, because the alpha does the work at draw time; *arithmetic* on one does not. A
/// control that mixes two palette entries — the scrollbar trough is the control background half-way
/// to the border — averages the channels, and averaging against a colour whose channels are zero
/// gives near-black rather than a lighter shade of the surface. That is what every scrollbar on this
/// backend was painted in.
/// </para>
/// </remarks>
internal sealed class CocoaTheme : ITheme
{
    /// <summary>Reads the desktop's palette, font and metrics into an immutable snapshot.</summary>
    public CocoaTheme()
    {
        var fallback = DefaultTheme.Instance;
        var colors = CocoaRuntime.objc_getClass("NSColor");

        var pushed = PushAppearance(out var previous);
        try
        {
            // The surface everything else is composited onto, and the first thing read for that reason.
            // It is opaque on this desktop, and it is stated as opaque rather than assumed to be: there
            // is nothing behind a window to composite it against.
            var surface = Opaque(Read(colors, "windowBackgroundColor", fallback.WindowBackground, fallback.WindowBackground));
            this.WindowBackground = surface;

            // The window's own colour again, and not controlColor, which reads like the obvious answer
            // and is the wrong surface: it is the white a bezelled control fills itself with, so every
            // panel, page and button at rest came out the colour of a text field. The other two backends
            // give this and the window the same value — COLOR_BTNFACE twice on Win32, the theme
            // background twice on GTK — because a control at rest is chrome, and chrome on this desktop
            // is the window's grey.
            this.ControlBackground = surface;
            this.ControlText = Read(colors, "controlTextColor", fallback.ControlText, surface);
            this.DisabledText = Read(colors, "disabledControlTextColor", fallback.DisabledText, surface);
            this.FieldBackground = Read(colors, "textBackgroundColor", fallback.FieldBackground, surface);
            this.Accent = Read(colors, "controlAccentColor", fallback.Accent, surface);
            this.SelectionBackground = Read(colors, "selectedContentBackgroundColor", fallback.SelectionBackground, surface);
            this.SelectionText = Read(colors, "alternateSelectedControlTextColor", fallback.SelectionText, surface);
            this.Border = Read(colors, "separatorColor", fallback.Border, surface);
            this.GridLine = Read(colors, "gridColor", fallback.GridLine, surface);

            // A table header here is the window's own grey with a rule under it rather than a surface of
            // its own, so it takes the window colour instead of inventing a shade the desktop does not
            // use.
            this.HeaderBackground = Read(colors, "windowBackgroundColor", fallback.HeaderBackground, surface);
            this.HeaderText = Read(colors, "headerTextColor", fallback.HeaderText, surface);
        }
        finally
        {
            if (pushed)
                PopAppearance(previous);
        }

        this.DefaultFont = ReadSystemFont(fallback.DefaultFont);
        this.RowHeight = ReadRowHeight(this.DefaultFont, fallback.RowHeight);
        this.DoubleClickTime = ReadDoubleClickTime(fallback.DoubleClickTime);
        this.IsHighContrast = ReadHighContrast();
    }

    /// <summary>
    /// Makes the application's own appearance the one a dynamic colour resolves against, answering
    /// whether it had to be pushed and what was current before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A semantic <c>NSColor</c> is not a colour, it is a rule, and the rule is evaluated against
    /// <c>NSAppearance</c>'s current one — which AppKit sets while a view is drawing and nothing sets
    /// here. Read outside a draw it resolves against the default appearance rather than the
    /// application's, so this palette came back in light mode however the desktop was set: a Mac
    /// already in dark mode got the light twelve at startup, and the first live appearance change
    /// raised its event, built a fresh theme and got the very same twelve back.
    /// </para>
    /// <para>
    /// <c>setCurrentAppearance:</c> is deprecated rather than gone, and its replacement is
    /// <c>performAsCurrentDrawingAppearance:</c>, which takes an Objective-C block — the one kind of
    /// object this assembly's interop rules keep out. So it is offered with
    /// <c>respondsToSelector:</c> instead of sent, and a system that has finally dropped it reads what
    /// it read before rather than aborting the process over a palette.
    /// </para>
    /// </remarks>
    private static bool PushAppearance(out nint previous)
    {
        previous = 0;

        var appearances = CocoaRuntime.objc_getClass("NSAppearance");
        if (appearances == 0 || !CocoaRuntime.Responds(appearances, "setCurrentAppearance:"))
            return false;

        var application = CocoaRuntime.SendToClass("NSApplication", "sharedApplication");
        if (application == 0 || !CocoaRuntime.Responds(application, "effectiveAppearance"))
            return false;

        var effective = CocoaRuntime.SendPointer(application, CocoaRuntime.sel_registerName("effectiveAppearance"));
        if (effective == 0)
            return false;

        previous = CocoaRuntime.SendPointer(appearances, CocoaRuntime.sel_registerName("currentAppearance"));
        CocoaRuntime.SendVoid(appearances, CocoaRuntime.sel_registerName("setCurrentAppearance:"), effective);
        return true;
    }

    /// <summary>Puts back whatever was current, which is nil when nothing was.</summary>
    private static void PopAppearance(nint previous)
    {
        var appearances = CocoaRuntime.objc_getClass("NSAppearance");
        if (appearances != 0)
            CocoaRuntime.SendVoid(appearances, CocoaRuntime.sel_registerName("setCurrentAppearance:"), previous);
    }

    /// <inheritdoc/>
    public Color WindowBackground { get; }

    /// <inheritdoc/>
    public Color ControlBackground { get; }

    /// <inheritdoc/>
    public Color ControlText { get; }

    /// <inheritdoc/>
    public Color DisabledText { get; }

    /// <inheritdoc/>
    public Color FieldBackground { get; }

    /// <inheritdoc/>
    public Color Accent { get; }

    /// <inheritdoc/>
    public Color SelectionBackground { get; }

    /// <inheritdoc/>
    public Color SelectionText { get; }

    /// <inheritdoc/>
    public Color Border { get; }

    /// <inheritdoc/>
    public Color GridLine { get; }

    /// <inheritdoc/>
    public Color HeaderBackground { get; }

    /// <inheritdoc/>
    public Color HeaderText { get; }

    /// <inheritdoc/>
    public bool IsHighContrast { get; }

    /// <inheritdoc/>
    public Font DefaultFont { get; }

    /// <inheritdoc/>
    public int RowHeight { get; }

    /// <summary>
    /// The shared fallback's 16, which is also what a legacy <c>NSScroller</c> reports for a regular
    /// control size — the platform's own answer needs a control size and a scroller style that only a
    /// live scroller carries, and this backend draws its own scrollbars.
    /// </summary>
    public int ScrollBarSize => DefaultTheme.Instance.ScrollBarSize;

    /// <inheritdoc/>
    public int DoubleClickTime { get; }

    /// <summary>
    /// One of <c>NSColor</c>'s semantic colours in sRGB, composited onto <paramref name="surface"/>, or
    /// <paramref name="fallback"/>.
    /// </summary>
    /// <remarks>
    /// The conversion is not optional: a semantic colour is a dynamic one that resolves against the
    /// current appearance, and asking it for a red component before it is in a component-bearing space
    /// raises rather than converting.
    /// </remarks>
    private static Color Read(nint colors, string name, Color fallback, Color surface)
    {
        if (colors == 0)
            return fallback;

        var selector = CocoaRuntime.sel_registerName(name);
        if (!CocoaRuntime.SendBool(colors, CocoaRuntime.sel_registerName("respondsToSelector:"), selector))
            return fallback;

        var color = CocoaRuntime.SendPointer(colors, selector);
        var space = CocoaRuntime.SendToClass("NSColorSpace", "sRGBColorSpace");
        var converted = color == 0 || space == 0
            ? 0
            : CocoaRuntime.SendPointer(color, CocoaRuntime.sel_registerName("colorUsingColorSpace:"), space);

        if (converted == 0)
            return fallback;

        var red = CocoaRuntime.SendDouble(converted, CocoaRuntime.sel_registerName("redComponent"));
        var green = CocoaRuntime.SendDouble(converted, CocoaRuntime.sel_registerName("greenComponent"));
        var blue = CocoaRuntime.SendDouble(converted, CocoaRuntime.sel_registerName("blueComponent"));
        var alpha = CocoaRuntime.SendDouble(converted, CocoaRuntime.sel_registerName("alphaComponent"));
        return Flatten(Color.FromArgb(Channel(alpha), Channel(red), Channel(green), Channel(blue)), surface);
    }

    /// <summary>One component of a colour, as a byte.</summary>
    private static int Channel(double component) => (int)Math.Clamp(Math.Round(component * 255), 0, 255);

    /// <summary>The same colour with the alpha dropped, which is what a surface answers with.</summary>
    private static Color Opaque(Color color) => Color.FromArgb(byte.MaxValue, color);

    /// <summary>The colour as it lands on <paramref name="surface"/>, with nothing left to composite.</summary>
    private static Color Flatten(Color color, Color surface)
    {
        if (color.A == byte.MaxValue)
            return color;

        var alpha = color.A / 255.0;
        return Color.FromArgb(
            byte.MaxValue,
            Mix(color.R, surface.R, alpha),
            Mix(color.G, surface.G, alpha),
            Mix(color.B, surface.B, alpha));
    }

    /// <summary>One channel of <paramref name="over"/> laid on <paramref name="under"/> at that alpha.</summary>
    private static int Mix(byte over, byte under, double alpha)
        => (int)Math.Clamp(Math.Round((over * alpha) + (under * (1 - alpha))), 0, 255);

    /// <summary>
    /// The system UI font at its own size — <c>systemFontOfSize:0</c> is how AppKit is asked for the
    /// size it would use itself, which is the size every native widget on the same window is wearing.
    /// </summary>
    private static Font ReadSystemFont(Font fallback)
    {
        var fonts = CocoaRuntime.objc_getClass("NSFont");
        var font = fonts == 0 ? 0 : CocoaRuntime.SendLength(fonts, CocoaRuntime.sel_registerName("systemFontOfSize:"), 0);
        if (font == 0)
            return fallback;

        var family = CocoaRuntime.SendPointer(font, CocoaRuntime.sel_registerName("familyName"));
        var name = family == 0 ? string.Empty : CocoaNative.ReadString(family);
        var size = CocoaRuntime.SendDouble(font, CocoaRuntime.sel_registerName("pointSize"));

        return name.Length == 0 || size <= 0
            ? fallback
            : new(name, (float)size);
    }

    /// <summary>A row tall enough for a line of the UI font, plus the padding a list puts around it.</summary>
    private static int ReadRowHeight(Font font, int fallback)
        => CocoaNative.TryMeasure("Hg", font, out _, out var height) && height > 0
            ? (int)Math.Ceiling(height) + 6
            : fallback;

    /// <summary>The user's own double-click interval, which AppKit states in seconds.</summary>
    private static int ReadDoubleClickTime(int fallback)
    {
        var events = CocoaRuntime.objc_getClass("NSEvent");
        var selector = CocoaRuntime.sel_registerName("doubleClickInterval");
        if (events == 0 || !CocoaRuntime.SendBool(events, CocoaRuntime.sel_registerName("respondsToSelector:"), selector))
            return fallback;

        var seconds = CocoaRuntime.SendDouble(events, selector);
        return seconds > 0 ? (int)Math.Round(seconds * 1000) : fallback;
    }

    /// <summary>Whether the user asked the desktop for more contrast, which is an accessibility setting here.</summary>
    private static bool ReadHighContrast()
    {
        var workspace = CocoaRuntime.SendToClass("NSWorkspace", "sharedWorkspace");
        var selector = CocoaRuntime.sel_registerName("accessibilityDisplayShouldIncreaseContrast");
        return workspace != 0
            && CocoaRuntime.SendBool(workspace, CocoaRuntime.sel_registerName("respondsToSelector:"), selector)
            && CocoaRuntime.SendBool(workspace, selector);
    }
}
