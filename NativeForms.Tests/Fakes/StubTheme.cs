using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests.Fakes;

/// <summary>
/// A theme whose every member starts at the <see cref="DefaultTheme"/> value but is settable, so
/// tests can script a desktop theme change (a different accent, a high-contrast flag) and assert
/// that controls repaint with the new values.
/// </summary>
internal sealed class StubTheme : ITheme
{
    private static readonly DefaultTheme _defaults = DefaultTheme.Instance;

    public Color WindowBackground { get; set; } = _defaults.WindowBackground;
    public Color ControlBackground { get; set; } = _defaults.ControlBackground;
    public Color ControlText { get; set; } = _defaults.ControlText;
    public Color DisabledText { get; set; } = _defaults.DisabledText;
    public Color FieldBackground { get; set; } = _defaults.FieldBackground;
    public Color Accent { get; set; } = _defaults.Accent;
    public Color SelectionBackground { get; set; } = _defaults.SelectionBackground;
    public Color SelectionText { get; set; } = _defaults.SelectionText;
    public Color Border { get; set; } = _defaults.Border;
    public Color GridLine { get; set; } = _defaults.GridLine;
    public Color HeaderBackground { get; set; } = _defaults.HeaderBackground;
    public Color HeaderText { get; set; } = _defaults.HeaderText;
    public bool IsHighContrast { get; set; }
    public Font DefaultFont { get; set; } = _defaults.DefaultFont;
    public int RowHeight { get; set; } = _defaults.RowHeight;
    public int ScrollBarSize { get; set; } = _defaults.ScrollBarSize;
    public int DoubleClickTime { get; set; } = _defaults.DoubleClickTime;
}
