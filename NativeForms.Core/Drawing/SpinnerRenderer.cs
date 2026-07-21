using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// The themed up/down button column shared by every spinner surface: <see cref="UpDownBase"/> (and
/// through it <see cref="NumericUpDown"/> and <see cref="DomainUpDown"/>) and
/// <see cref="TimePicker"/>. Geometry and painting live here once so the column is pixel-identical
/// wherever it appears, and so a control that cannot host a native editor — because it needs the
/// caret itself — still gets the classic spinner without copying its drawing code.
/// </summary>
internal static class SpinnerRenderer
{
    /// <summary>The number of stacked lines forming a spinner arrow glyph.</summary>
    private const int _ArrowRows = 3;

    /// <summary>The width of the button column at the right edge of a spinner field.</summary>
    public static int ColumnWidth(ITheme theme) => theme.ScrollBarSize + 1;

    /// <summary>The upper (increment) button of a spinner field of the given size.</summary>
    public static Rectangle UpButton(ITheme theme, int width, int height)
    {
        var columnWidth = ColumnWidth(theme);
        return new(width - columnWidth, 0, columnWidth, height / 2);
    }

    /// <summary>The lower (decrement) button of a spinner field of the given size.</summary>
    public static Rectangle DownButton(ITheme theme, int width, int height)
    {
        var columnWidth = ColumnWidth(theme);
        var top = height / 2;
        return new(width - columnWidth, top, columnWidth, height - top);
    }

    /// <summary>
    /// Paints the button column and the seams framing it over an already-filled field:
    /// <paramref name="pressedDirection"/> (+1 up, -1 down, 0 none) fills the held button with the
    /// accent color, and <paramref name="enabled"/> greys the resting arrows.
    /// </summary>
    public static void Paint(IGraphics g, ITheme theme, int width, int height, int pressedDirection, bool enabled)
    {
        var up = UpButton(theme, width, height);
        var down = DownButton(theme, width, height);
        if (pressedDirection > 0)
            g.FillRectangle(theme.Accent, up);
        else if (pressedDirection < 0)
            g.FillRectangle(theme.Accent, down);

        var restingColor = enabled ? theme.ControlText : theme.DisabledText;
        DrawArrow(g, pressedDirection > 0 ? theme.SelectionText : restingColor, up, pointsUp: true);
        DrawArrow(g, pressedDirection < 0 ? theme.SelectionText : restingColor, down, pointsUp: false);

        // Seams between the field, the buttons, and around the whole control.
        g.DrawLine(theme.Border, up.X, 0, up.X, height - 1);
        g.DrawLine(theme.Border, up.X, down.Y, width - 1, down.Y);
        g.DrawRectangle(theme.Border, new Rectangle(0, 0, width - 1, height - 1));
    }

    /// <summary>Draws a small spinner triangle centered in <paramref name="rect"/>.</summary>
    private static void DrawArrow(IGraphics g, Color color, Rectangle rect, bool pointsUp)
    {
        var centerX = rect.X + rect.Width / 2;
        var top = rect.Y + (rect.Height - _ArrowRows) / 2;
        for (var i = 0; i < _ArrowRows; ++i)
        {
            var half = pointsUp ? i : _ArrowRows - 1 - i;
            g.DrawLine(color, centerX - half, top + i, centerX + half, top + i);
        }
    }
}
