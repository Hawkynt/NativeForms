using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// The palette, font and metrics an owner-drawn control uses so it matches the host desktop. Backends
/// populate it from the OS (uxtheme/<c>GetSysColor</c> on Windows, <c>GtkStyleContext</c> on GTK,
/// <c>NSColor</c> on macOS); <see cref="DefaultTheme"/> is the fallback for headless/test use. Painting
/// exclusively through these members is what lets a custom control look native in light or dark mode.
/// A theme is an immutable snapshot: after the desktop changes, the backend raises
/// <see cref="Backends.IPlatformBackend.ThemeChanged"/> and serves a fresh instance.
/// Pixel metrics (<see cref="RowHeight"/>, <see cref="ScrollBarSize"/>) are logical: they are sized
/// for the primary monitor's current scale, so painting them 1:1 there is correct; mapping onto
/// another monitor's scale goes through <see cref="Control.LogicalToDevice(int)"/>.
/// </summary>
public interface ITheme
{
    /// <summary>Background of a top-level window.</summary>
    Color WindowBackground { get; }

    /// <summary>Background of a control surface (panels, buttons at rest).</summary>
    Color ControlBackground { get; }

    /// <summary>Primary text color on a control.</summary>
    Color ControlText { get; }

    /// <summary>Text color for disabled elements.</summary>
    Color DisabledText { get; }

    /// <summary>Background of editable/list fields (text boxes, list boxes, grid cells).</summary>
    Color FieldBackground { get; }

    /// <summary>The system accent color (focus, checkmarks, selection tint).</summary>
    Color Accent { get; }

    /// <summary>Background of a selected item.</summary>
    Color SelectionBackground { get; }

    /// <summary>Text color on a selected item.</summary>
    Color SelectionText { get; }

    /// <summary>Border/separator color.</summary>
    Color Border { get; }

    /// <summary>Grid line color for tabular controls.</summary>
    Color GridLine { get; }

    /// <summary>Background of a column/row header.</summary>
    Color HeaderBackground { get; }

    /// <summary>Text color of a header.</summary>
    Color HeaderText { get; }

    /// <summary>
    /// Whether the desktop runs a high-contrast scheme (Windows: <c>SPI_GETHIGHCONTRAST</c>; GTK: a
    /// HighContrast theme name). Owner-drawn chrome that would blend colors should fall back to the
    /// plain palette entries while this is set.
    /// </summary>
    bool IsHighContrast { get; }

    /// <summary>The default UI font.</summary>
    Font DefaultFont { get; }

    /// <summary>The natural height of a list/grid row, in logical pixels (primary-monitor scale).</summary>
    int RowHeight { get; }

    /// <summary>The thickness of a scrollbar, in logical pixels (primary-monitor scale).</summary>
    int ScrollBarSize { get; }
}
