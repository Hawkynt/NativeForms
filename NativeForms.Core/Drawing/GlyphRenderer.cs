using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// Shared owner-draw primitives for the themed visuals several controls need: the check glyph
/// (<see cref="CheckBox"/> and check cells), the progress fill (<see cref="ProgressBar"/> and progress
/// cells), a button face, sort arrows and the current-row marker. Everything here is stroke/fill only —
/// no allocation — so callers can use it on the paint path freely.
/// </summary>
internal static class GlyphRenderer
{
    /// <summary>The standard edge length of the themed check glyph in pixels.</summary>
    public const int CheckBoxSize = 14;

    /// <summary>
    /// The alert red shared by the controls that flag a problem — a path that names nothing, a drive
    /// that is nearly full. Deliberately not an <see cref="ITheme"/> member: no desktop exposes an
    /// "error" colour to query, so inventing a themed one would be a fiction. Controls that paint it
    /// expose it as a property so an application can match its own palette.
    /// </summary>
    public static readonly Color Warning = Color.FromArgb(0xFF, 0xE8, 0x11, 0x23);

    /// <summary>Draws a themed check box (field-colored box, themed border, accent checkmark when
    /// checked) scaled into <paramref name="box"/>.</summary>
    public static void DrawCheckBox(IGraphics g, ITheme theme, Rectangle box, bool isChecked)
    {
        g.FillRectangle(theme.FieldBackground, box);
        g.DrawRectangle(isChecked ? theme.Accent : theme.Border, box);
        if (!isChecked)
            return;

        // A check mark: two strokes from the lower-left to the upper-right, in 14ths of the box.
        var w = box.Width;
        var h = box.Height;
        var thickness = Math.Max(1, w / 7);
        g.DrawLine(theme.Accent, box.X + (3 * w / 14), box.Y + (7 * h / 14), box.X + (6 * w / 14), box.Y + (10 * h / 14), thickness);
        g.DrawLine(theme.Accent, box.X + (6 * w / 14), box.Y + (10 * h / 14), box.X + (11 * w / 14), box.Y + (3 * h / 14), thickness);
    }

    /// <summary>Draws a themed progress bar (field-colored track, accent fill proportional to
    /// <paramref name="value"/> within [<paramref name="minimum"/>, <paramref name="maximum"/>],
    /// themed border) into <paramref name="bounds"/>.</summary>
    public static void DrawProgressBar(IGraphics g, ITheme theme, Rectangle bounds, int value, int minimum, int maximum)
        => DrawProgressBar(g, theme, bounds, value, minimum, maximum, theme.Accent);

    /// <summary>Draws the themed progress bar with an explicit fill colour — the seam a tile needs to
    /// turn its bar red past a threshold without restating the track, the clamp or the border.</summary>
    public static void DrawProgressBar(IGraphics g, ITheme theme, Rectangle bounds, int value, int minimum, int maximum, Color fill)
    {
        g.FillRectangle(theme.FieldBackground, bounds);

        var range = maximum - minimum;
        if (range > 0 && bounds.Width > 2 && bounds.Height > 2)
        {
            var track = bounds.Width - 2;
            var filled = (int)((long)track * (Math.Clamp(value, minimum, maximum) - minimum) / range);
            if (filled > 0)
                g.FillRectangle(fill, new(bounds.X + 1, bounds.Y + 1, filled, bounds.Height - 2));
        }

        g.DrawRectangle(theme.Border, new(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1));
    }

    /// <summary>Draws a themed push-button face (control-colored fill, themed border, centered text;
    /// greyed text when disabled) into <paramref name="bounds"/>.</summary>
    public static void DrawButtonFace(IGraphics g, ITheme theme, Rectangle bounds, string text, bool enabled)
    {
        g.FillRectangle(theme.ControlBackground, bounds);
        g.DrawRectangle(theme.Border, bounds);
        g.DrawText(text, theme.DefaultFont, enabled ? theme.ControlText : theme.DisabledText, bounds, ContentAlignment.MiddleCenter);
    }

    /// <summary>Draws a small sort-direction triangle centered in <paramref name="bounds"/>, pointing
    /// up for ascending and down for descending.</summary>
    public static void DrawSortArrow(IGraphics g, Color color, Rectangle bounds, bool ascending)
    {
        var cx = bounds.X + (bounds.Width / 2);
        var top = bounds.Y + ((bounds.Height - 4) / 2);
        for (var i = 0; i < 4; ++i)
        {
            var y = ascending ? top + i : top + 3 - i;
            g.DrawLine(color, cx - i, y, cx + i + 1, y);
        }
    }

    /// <summary>Draws a small right-pointing triangle (the current-row marker) centered in
    /// <paramref name="bounds"/>.</summary>
    public static void DrawRowMarker(IGraphics g, Color color, Rectangle bounds)
    {
        var left = bounds.X + ((bounds.Width - 4) / 2);
        var cy = bounds.Y + (bounds.Height / 2);
        for (var i = 0; i < 4; ++i)
            g.DrawLine(color, left + i, cy - 3 + i, left + i, cy + 4 - i);
    }

    /// <summary>The number of stacked-line rows in the themed drop-down arrow.</summary>
    private const int _ComboArrowRows = 5;

    /// <summary>Draws the drop-down arrow every combo-style field shows: a downward triangle of five
    /// stacked lines, centered in <paramref name="bounds"/>.</summary>
    public static void DrawComboArrow(IGraphics g, Color color, Rectangle bounds)
    {
        var centerX = bounds.X + (bounds.Width / 2);
        var top = bounds.Y + ((bounds.Height - _ComboArrowRows) / 2);
        for (var i = 0; i < _ComboArrowRows; ++i)
            g.DrawLine(color, centerX - _ComboArrowRows + 1 + i, top + i, centerX + _ComboArrowRows - 1 - i, top + i);
    }

    /// <summary>Draws one column-header cell: header-colored face, the caption clipped and padded
    /// inside it, and optionally the separator after its trailing edge.</summary>
    public static void DrawHeaderCell(
        IGraphics g,
        ITheme theme,
        Rectangle bounds,
        string text,
        ContentAlignment alignment,
        int textPadding,
        bool separator)
    {
        g.FillRectangle(theme.HeaderBackground, bounds);
        g.PushClip(bounds);
        var textRect = new Rectangle(bounds.X + textPadding, bounds.Y, Math.Max(0, bounds.Width - (2 * textPadding)), bounds.Height);
        g.DrawText(text, theme.DefaultFont, theme.HeaderText, textRect, alignment);
        g.PopClip();

        if (separator)
            g.DrawLine(theme.Border, bounds.Right, bounds.Y, bounds.Right, bounds.Bottom);
    }

    /// <summary>Draws the keyboard-focus ring: a one-pixel rectangle in a faint accent (accent blended
    /// halfway toward the control background) — the modern take on the classic dotted marquee, and
    /// arithmetic rather than alpha so it renders identically on GDI. Under high contrast the blend
    /// is skipped and the full accent is used.</summary>
    public static void DrawFocusRing(IGraphics g, ITheme theme, Rectangle bounds)
    {
        var accent = theme.Accent;
        var color = theme.IsHighContrast ? accent : Blend(accent, theme.ControlBackground);
        g.DrawRectangle(color, bounds);
    }

    /// <summary>Fills the themed selected-item highlight behind an item, row or cell.</summary>
    public static void FillSelection(IGraphics g, ITheme theme, Rectangle bounds)
        => g.FillRectangle(theme.SelectionBackground, bounds);

    /// <summary>Mixes two opaque colors 50:50, channel-wise.</summary>
    private static Color Blend(Color a, Color b)
        => Color.FromArgb(0xFF, (a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2);
}
