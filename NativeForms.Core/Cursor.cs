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
}

/// <summary>
/// A pointer shape, the moral equivalent of <c>System.Windows.Forms.Cursor</c> reduced to the stock
/// set every platform ships: it names a <see cref="CursorKind"/> and the backend resolves it to the
/// native cursor (<c>LoadCursorW(IDC_*)</c> on Win32, <c>gdk_cursor_new_from_name</c> on GTK).
/// Instances come from <see cref="Cursors"/>; custom cursor bitmaps are not modeled.
/// </summary>
public sealed class Cursor
{
    /// <summary>Creates the cursor for a stock shape. Use the shared <see cref="Cursors"/> instances.</summary>
    internal Cursor(CursorKind kind) => this.Kind = kind;

    /// <summary>The stock shape this cursor renders as.</summary>
    public CursorKind Kind { get; }
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
}
