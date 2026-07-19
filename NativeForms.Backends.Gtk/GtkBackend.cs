using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The Linux platform backend. Realizes NativeForms controls as native GTK 3 widgets and drives the
/// GTK main loop. Reflection-free and AOT-safe: every native call is a source-generated P/Invoke and
/// every signal callback is an unmanaged function pointer.
/// </summary>
public sealed partial class GtkBackend : IPlatformBackend
{
    private static readonly object _initGate = new();
    private static bool _initialized;

    /// <summary>Calls <c>gtk_init</c> exactly once, before any widget is created or the loop runs.</summary>
    private static void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (_initGate)
        {
            if (_initialized)
                return;

            NativeMethods.gtk_init(0, 0);
            _initialized = true;
        }
    }

    /// <inheritdoc />
    public string Name => "Gtk";

    /// <inheritdoc />
    public bool IsSupported => OperatingSystem.IsLinux();

    /// <inheritdoc />
    public ITheme Theme
    {
        get
        {
            EnsureInitialized();
            return field ??= new GtkTheme();
        }
    }

    /// <inheritdoc />
    public ICanvasPeer CreateCanvas()
    {
        EnsureInitialized();
        return new GtkCanvasPeer();
    }

    /// <inheritdoc />
    public IPopupPeer CreatePopup()
    {
        EnsureInitialized();
        return new GtkPopupPeer();
    }

    /// <inheritdoc />
    public IImage CreateImage(int width, int height, ReadOnlySpan<int> argb)
    {
        EnsureInitialized();
        return new GtkImage(width, height, argb);
    }

    /// <inheritdoc />
    public ITimerPeer CreateTimer()
    {
        EnsureInitialized();
        return new GtkTimerPeer();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Not implemented: <c>GtkStatusIcon</c> has been deprecated since GTK 3.14 and is absent from
    /// many desktops, and the replacement (the StatusNotifier D-Bus protocol) is a separate
    /// integration tracked in <c>docs/PRD.md</c> §7.7. Failing here is more honest than adding an
    /// icon no shell will show.
    /// </remarks>
    public INotifyIconPeer CreateNotifyIcon()
        => throw new NotSupportedException(
            "Tray icons are not supported by the GTK backend yet: GtkStatusIcon is deprecated and the "
            + "StatusNotifier (D-Bus) integration is tracked in docs/PRD.md §7.7.");

    /// <inheritdoc />
    public Size GetScreenSize()
    {
        EnsureInitialized();
        var display = NativeMethods.gdk_display_get_default();
        if (display == 0)
            return Size.Empty;

        // The primary monitor when the desktop marks one, otherwise the first.
        var monitor = NativeMethods.gdk_display_get_primary_monitor(display);
        if (monitor == 0)
            monitor = NativeMethods.gdk_display_get_monitor(display, 0);

        if (monitor == 0)
            return Size.Empty;

        NativeMethods.gdk_monitor_get_geometry(monitor, out var geometry);
        return new(geometry.Width, geometry.Height);
    }

    /// <inheritdoc />
    public Size MeasureText(string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
            return Size.Empty;

        EnsureInitialized();

        // A layout on a context from the default font map measures identically to the per-paint
        // PangoCairo layout, but needs no Cairo surface — so it works before anything is realized.
        var context = NativeMethods.pango_font_map_create_context(NativeMethods.pango_cairo_font_map_get_default());
        try
        {
            var layout = NativeMethods.pango_layout_new(context);
            try
            {
                GtkGraphics.ConfigureLayout(layout, text, font);
                NativeMethods.pango_layout_get_pixel_size(layout, out var width, out var height);
                return new Size(width, height);
            }
            finally
            {
                NativeMethods.g_object_unref(layout);
            }
        }
        finally
        {
            NativeMethods.g_object_unref(context);
        }
    }

    /// <inheritdoc />
    public IWindowPeer CreateWindow()
    {
        EnsureInitialized();
        return new GtkWindowPeer();
    }

    /// <inheritdoc />
    public IButtonPeer CreateButton()
    {
        EnsureInitialized();
        return new GtkButtonPeer();
    }

    /// <inheritdoc />
    public ILabelPeer CreateLabel()
    {
        EnsureInitialized();
        return new GtkLabelPeer();
    }

    /// <inheritdoc />
    public ITextBoxPeer CreateTextBox()
    {
        EnsureInitialized();
        return new GtkTextBoxPeer();
    }

    /// <inheritdoc />
    public IRichTextBoxPeer CreateRichTextBox()
    {
        EnsureInitialized();
        return new GtkRichTextBoxPeer();
    }

    /// <inheritdoc />
    public void Run(IWindowPeer mainWindow)
    {
        EnsureInitialized();
        NativeMethods.gtk_main();
    }

    /// <inheritdoc />
    public void Quit() => NativeMethods.gtk_main_quit();
}
