using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// A neutral light-mode theme used when no native theme is available (headless tests) and as the base
/// a backend can selectively override. Values approximate the classic Windows/Fluent light palette.
/// </summary>
public sealed class DefaultTheme : ITheme
{
    /// <summary>The shared instance.</summary>
    public static DefaultTheme Instance { get; } = new();

    /// <inheritdoc/>
    public Color WindowBackground => Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3);

    /// <inheritdoc/>
    public Color ControlBackground => Color.FromArgb(0xFF, 0xFD, 0xFD, 0xFD);

    /// <inheritdoc/>
    public Color ControlText => Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A);

    /// <inheritdoc/>
    public Color DisabledText => Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A);

    /// <inheritdoc/>
    public Color FieldBackground => Color.White;

    /// <inheritdoc/>
    public Color Accent => Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);

    /// <inheritdoc/>
    public Color SelectionBackground => Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);

    /// <inheritdoc/>
    public Color SelectionText => Color.White;

    /// <inheritdoc/>
    public Color Border => Color.FromArgb(0xFF, 0xC8, 0xC8, 0xC8);

    /// <inheritdoc/>
    public Color GridLine => Color.FromArgb(0xFF, 0xE1, 0xE1, 0xE1);

    /// <inheritdoc/>
    public Color HeaderBackground => Color.FromArgb(0xFF, 0xEC, 0xEC, 0xEC);

    /// <inheritdoc/>
    public Color HeaderText => Color.FromArgb(0xFF, 0x30, 0x30, 0x30);

    /// <inheritdoc/>
    public bool IsHighContrast => false;

    /// <inheritdoc/>
    public Font DefaultFont { get; } = new("Segoe UI", 9f);

    /// <inheritdoc/>
    public int RowHeight => 22;

    /// <inheritdoc/>
    public int ScrollBarSize => 16;
}
