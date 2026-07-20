using System.Drawing;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// A theme resolved from the running GTK desktop. Colors are read once from an offscreen widget's
/// <c>GtkStyleContext</c> (named theme colors plus the foreground) so owner-drawn controls match the
/// user's light/dark scheme; any color or metric GTK does not expose falls back to
/// <see cref="DefaultTheme"/>. The default font is parsed from the <c>gtk-font-name</c> setting.
/// </summary>
internal sealed class GtkTheme : ITheme
{
    private static readonly DefaultTheme _fallback = DefaultTheme.Instance;

    /// <summary>Snapshots the desktop palette and font at construction.</summary>
    internal GtkTheme()
    {
        // An unparented widget still resolves the default screen's theme via its style context.
        var widget = NativeMethods.gtk_label_new(string.Empty);
        var context = NativeMethods.gtk_widget_get_style_context(widget);

        var foreground = ReadForeground(context, _fallback.ControlText);
        var background = ReadColor(context, "theme_bg_color", _fallback.ControlBackground);
        var baseColor = ReadColor(context, "theme_base_color", _fallback.FieldBackground);
        var selectionBg = ReadColor(context, "theme_selected_bg_color", _fallback.SelectionBackground);
        var selectionFg = ReadColor(context, "theme_selected_fg_color", _fallback.SelectionText);
        var border = ReadColor(context, "borders", _fallback.Border);

        this.WindowBackground = ReadColor(context, "theme_bg_color", _fallback.WindowBackground);
        this.ControlBackground = background;
        this.ControlText = foreground;
        this.DisabledText = ReadColor(context, "insensitive_fg_color", _fallback.DisabledText);
        this.FieldBackground = baseColor;
        this.Accent = selectionBg;
        this.SelectionBackground = selectionBg;
        this.SelectionText = selectionFg;
        this.Border = border;
        this.GridLine = border;
        this.HeaderBackground = background;
        this.HeaderText = foreground;
        this.DefaultFont = ReadDefaultFont();
        this.IsHighContrast = ReadIsHighContrast();

        NativeMethods.gtk_widget_destroy(widget);
    }

    /// <inheritdoc />
    public bool IsHighContrast { get; }

    /// <inheritdoc />
    public Color WindowBackground { get; }

    /// <inheritdoc />
    public Color ControlBackground { get; }

    /// <inheritdoc />
    public Color ControlText { get; }

    /// <inheritdoc />
    public Color DisabledText { get; }

    /// <inheritdoc />
    public Color FieldBackground { get; }

    /// <inheritdoc />
    public Color Accent { get; }

    /// <inheritdoc />
    public Color SelectionBackground { get; }

    /// <inheritdoc />
    public Color SelectionText { get; }

    /// <inheritdoc />
    public Color Border { get; }

    /// <inheritdoc />
    public Color GridLine { get; }

    /// <inheritdoc />
    public Color HeaderBackground { get; }

    /// <inheritdoc />
    public Color HeaderText { get; }

    /// <inheritdoc />
    public Font DefaultFont { get; }

    /// <inheritdoc />
    public int RowHeight => _fallback.RowHeight;

    /// <inheritdoc />
    public int ScrollBarSize => _fallback.ScrollBarSize;

    /// <summary>Reads a named theme color, or returns <paramref name="fallback"/> if the name is unknown.</summary>
    private static Color ReadColor(nint context, string name, Color fallback)
        => NativeMethods.gtk_style_context_lookup_color(context, name, out var rgba) != 0 ? ToColor(rgba) : fallback;

    /// <summary>Reads the style context's foreground (text) color for the normal state.</summary>
    private static Color ReadForeground(nint context, Color fallback)
    {
        NativeMethods.gtk_style_context_get_color(context, NativeMethods.GTK_STATE_FLAG_NORMAL, out var rgba);

        // A fully transparent result means the context had nothing to offer; use the fallback.
        return rgba.Alpha <= 0 ? fallback : ToColor(rgba);
    }

    /// <summary>Converts a GDK 0..1 RGBA to a 0..255 <see cref="Color"/>.</summary>
    private static Color ToColor(GdkRGBA rgba)
        => Color.FromArgb(Channel(rgba.Alpha), Channel(rgba.Red), Channel(rgba.Green), Channel(rgba.Blue));

    /// <summary>Clamps a 0..1 component and scales it to a 0..255 byte.</summary>
    private static int Channel(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);

    /// <summary>Whether <c>gtk-theme-name</c> names a high-contrast theme (GNOME ships "HighContrast").</summary>
    private static bool ReadIsHighContrast()
        => ReadSettingsString("gtk-theme-name")?.Contains("HighContrast", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Reads a string property from the default <c>GtkSettings</c>, or <see langword="null"/>.</summary>
    private static string? ReadSettingsString(string property)
    {
        var settings = NativeMethods.gtk_settings_get_default();
        if (settings == 0)
            return null;

        NativeMethods.g_object_get(settings, property, out var valuePtr, 0);
        if (valuePtr == 0)
            return null;

        var value = Marshal.PtrToStringUTF8(valuePtr);
        NativeMethods.g_free(valuePtr);
        return value;
    }

    /// <summary>Reads and parses <c>gtk-font-name</c> (for example "Cantarell 11"), else the fallback font.</summary>
    private static Font ReadDefaultFont()
        => ReadSettingsString("gtk-font-name") is { } name ? ParseFont(name) : _fallback.DefaultFont;

    /// <summary>Parses a Pango font string of the form "Family[ Style] Size" into a <see cref="Font"/>.</summary>
    private static Font ParseFont(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return _fallback.DefaultFont;

        var trimmed = description.Trim();
        var lastSpace = trimmed.LastIndexOf(' ');
        if (lastSpace > 0
            && float.TryParse(
                trimmed[(lastSpace + 1)..],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var size)
            && size > 0)
        {
            var family = trimmed[..lastSpace].Trim();
            if (family.Length > 0)
                return new Font(family, size);
        }

        return new Font(trimmed, _fallback.DefaultFont.SizeInPoints);
    }
}
