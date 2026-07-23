using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>The stock pointer shapes every platform provides.</summary>
public enum CursorKind
{
    /// <summary>The default arrow pointer.</summary>
    Arrow,

    /// <summary>The pointing hand shown over links.</summary>
    Hand,

    /// <summary>The text-insertion I-beam.</summary>
    IBeam,

    /// <summary>The busy/wait indicator.</summary>
    Wait,

    /// <summary>The crosshair.</summary>
    Cross,

    /// <summary>The horizontal (west-east) resize arrows.</summary>
    SizeWE,

    /// <summary>The vertical (north-south) resize arrows.</summary>
    SizeNS,

    /// <summary>The diagonal (northwest-southeast) resize arrows.</summary>
    SizeNWSE,

    /// <summary>The diagonal (northeast-southwest) resize arrows.</summary>
    SizeNESW,

    /// <summary>The "not allowed" slashed circle.</summary>
    No,

    /// <summary>The four-headed move arrows.</summary>
    SizeAll,

    /// <summary>The arrow with a question mark shown for context help.</summary>
    Help,

    /// <summary>The arrow with a busy indicator — working in the background.</summary>
    AppStarting,

    /// <summary>The vertical-splitter cursor: parallel bars with east-west arrows.</summary>
    VSplit,

    /// <summary>The horizontal-splitter cursor: parallel bars with north-south arrows.</summary>
    HSplit,

    /// <summary>A custom bitmap cursor built from image bytes (a <c>.cur</c>/<c>.ani</c>/icon).</summary>
    Custom,
}

/// <summary>
/// A pointer shape, the moral equivalent of <c>System.Windows.Forms.Cursor</c> reduced to the stock
/// set every platform ships: it names a <see cref="CursorKind"/> and the backend resolves it to the
/// native cursor (<c>LoadCursorW(IDC_*)</c> on Win32, <c>gdk_cursor_new_from_name</c> on GTK). Stock
/// instances come from <see cref="Cursors"/>; a custom bitmap pointer comes from
/// <see cref="FromBytes(System.ReadOnlySpan{byte})"/> — a decoded <c>.cur</c>/<c>.ani</c>/icon with its
/// hotspot — which the backend turns into a native cursor (<c>gdk_cursor_new_from_pixbuf</c> on GTK,
/// <c>CreateIconIndirect</c> on Win32).
/// </summary>
public sealed class Cursor
{
    /// <summary>Creates the cursor for a stock shape. Use the shared <see cref="Cursors"/> instances.</summary>
    internal Cursor(CursorKind kind) => this.Kind = kind;

    /// <summary>Creates a custom bitmap cursor from ARGB pixels and a hotspot.</summary>
    internal Cursor(int[] pixels, int width, int height, int hotspotX, int hotspotY)
    {
        this.Kind = CursorKind.Custom;
        this.Pixels = pixels;
        this.Width = width;
        this.Height = height;
        this.HotspotX = hotspotX;
        this.HotspotY = hotspotY;
    }

    /// <summary>The stock shape this cursor renders as, or <see cref="CursorKind.Custom"/> for a bitmap.</summary>
    public CursorKind Kind { get; }

    /// <summary>The custom cursor's row-major ARGB pixels, or <see langword="null"/> for a stock cursor.</summary>
    internal int[]? Pixels { get; }

    /// <summary>The custom cursor's pixel width.</summary>
    internal int Width { get; }

    /// <summary>The custom cursor's pixel height.</summary>
    internal int Height { get; }

    /// <summary>The custom cursor's hotspot x — the pixel the click aligns to.</summary>
    internal int HotspotX { get; }

    /// <summary>The custom cursor's hotspot y.</summary>
    internal int HotspotY { get; }

    /// <summary>
    /// Builds a custom cursor from image bytes: a <c>.cur</c> (its hotspot is honoured), an animated
    /// <c>.ani</c> (its first frame), an <c>.ico</c> or any still image (hotspot at the top-left).
    /// Assign the result to <see cref="Control.Cursor"/> or <see cref="Cursors"/>-style usage.
    /// </summary>
    /// <exception cref="System.FormatException">The bytes are not a recognized image format.</exception>
    public static Cursor FromBytes(System.ReadOnlySpan<byte> data)
    {
        var (width, height, argb, hotspotX, hotspotY) = ImageDecoder.DecodeCursor(data);
        return new Cursor(argb, width, height, hotspotX, hotspotY);
    }
}

/// <summary>The shared instances of the stock cursors, mirroring <c>System.Windows.Forms.Cursors</c>.</summary>
public static class Cursors
{
    /// <summary>The default arrow pointer.</summary>
    public static Cursor Arrow { get; } = new(CursorKind.Arrow);

    /// <summary>The pointing hand shown over links.</summary>
    public static Cursor Hand { get; } = new(CursorKind.Hand);

    /// <summary>The text-insertion I-beam.</summary>
    public static Cursor IBeam { get; } = new(CursorKind.IBeam);

    /// <summary>The busy/wait indicator.</summary>
    public static Cursor Wait { get; } = new(CursorKind.Wait);

    /// <summary>The crosshair.</summary>
    public static Cursor Cross { get; } = new(CursorKind.Cross);

    /// <summary>The horizontal (west-east) resize arrows.</summary>
    public static Cursor SizeWE { get; } = new(CursorKind.SizeWE);

    /// <summary>The vertical (north-south) resize arrows.</summary>
    public static Cursor SizeNS { get; } = new(CursorKind.SizeNS);

    /// <summary>The diagonal (northwest-southeast) resize arrows.</summary>
    public static Cursor SizeNWSE { get; } = new(CursorKind.SizeNWSE);

    /// <summary>The diagonal (northeast-southwest) resize arrows.</summary>
    public static Cursor SizeNESW { get; } = new(CursorKind.SizeNESW);

    /// <summary>The "not allowed" slashed circle.</summary>
    public static Cursor No { get; } = new(CursorKind.No);

    /// <summary>The default arrow pointer — the Windows Forms alias for <see cref="Arrow"/>.</summary>
    public static Cursor Default => Arrow;

    /// <summary>The busy/wait indicator — the Windows Forms alias for <see cref="Wait"/>.</summary>
    public static Cursor WaitCursor => Wait;

    /// <summary>The four-headed move arrows.</summary>
    public static Cursor SizeAll { get; } = new(CursorKind.SizeAll);

    /// <summary>The arrow with a question mark shown for context help.</summary>
    public static Cursor Help { get; } = new(CursorKind.Help);

    /// <summary>The arrow with a busy indicator — working in the background.</summary>
    public static Cursor AppStarting { get; } = new(CursorKind.AppStarting);

    /// <summary>The vertical-splitter cursor.</summary>
    public static Cursor VSplit { get; } = new(CursorKind.VSplit);

    /// <summary>The horizontal-splitter cursor.</summary>
    public static Cursor HSplit { get; } = new(CursorKind.HSplit);
}
