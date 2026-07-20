using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    /// <summary>The most recently constructed backend — the instance the static settings callback
    /// notifies when the desktop theme changes (an app runs exactly one backend).</summary>
    private static GtkBackend? _current;

    private GtkTheme? _theme;

    /// <summary>Registers this instance as the receiver of desktop theme-change notifications.</summary>
    public GtkBackend() => _current = this;

    /// <summary>Calls <c>gtk_init</c> exactly once, before any widget is created or the loop runs,
    /// and hooks the <c>GtkSettings</c> notifications that announce a desktop theme change.</summary>
    private static void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (_initGate)
        {
            if (_initialized)
                return;

            NativeMethods.gtk_init(0, 0);
            HookThemeNotifications();
            _initialized = true;
        }
    }

    /// <summary>
    /// Subscribes to <c>notify::gtk-theme-name</c> and <c>notify::gtk-application-prefer-dark-theme</c>
    /// on the default <c>GtkSettings</c>, so a theme or dark-mode switch invalidates the cached theme
    /// and repaints every owner-drawn control.
    /// </summary>
    private static void HookThemeNotifications()
    {
        var settings = NativeMethods.gtk_settings_get_default();
        if (settings == 0)
            return;

        unsafe
        {
            var handler = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnSettingsChanged;
            NativeMethods.g_signal_connect_data(settings, "notify::gtk-theme-name", handler, 0, 0, 0);
            NativeMethods.g_signal_connect_data(settings, "notify::gtk-application-prefer-dark-theme", handler, 0, 0, 0);
        }
    }

    /// <summary>Native <c>notify::…</c> handler: <c>void (GObject*, GParamSpec*, gpointer)</c>.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnSettingsChanged(nint settings, nint pspec, nint userData)
    {
        var backend = _current;
        if (backend is null)
            return;

        backend._theme = null;
        backend.ThemeChanged?.Invoke(backend, EventArgs.Empty);
    }

    /// <inheritdoc />
    public string Name => "Gtk";

    /// <inheritdoc />
    public bool IsSupported => OperatingSystem.IsLinux();

    /// <inheritdoc />
    // The cache is dropped when GtkSettings announces a theme change, so the next read snapshots
    // fresh style-context values.
    public ITheme Theme
    {
        get
        {
            EnsureInitialized();
            return _theme ??= new GtkTheme();
        }
    }

    /// <inheritdoc />
    public event EventHandler? ThemeChanged;

    /// <inheritdoc />
    public double GetDpiScale()
    {
        EnsureInitialized();
        var display = NativeMethods.gdk_display_get_default();
        if (display == 0)
            return 1.0;

        var monitor = NativeMethods.gdk_display_get_primary_monitor(display);
        if (monitor == 0)
            monitor = NativeMethods.gdk_display_get_monitor(display, 0);

        if (monitor == 0)
            return 1.0;

        var scale = NativeMethods.gdk_monitor_get_scale_factor(monitor);
        return scale > 0 ? scale : 1.0;
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
    public void SetClipboardText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        EnsureInitialized();
        var clipboard = NativeMethods.gtk_clipboard_get(NativeMethods.gdk_atom_intern("CLIPBOARD", 0));
        NativeMethods.gtk_clipboard_set_text(clipboard, text, -1);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Queues through <c>g_idle_add_full</c>, which is thread-safe by GLib contract, so posting works
    /// from any thread. The action travels as a normal <see cref="GCHandle"/> threaded through
    /// <c>user_data</c> — the same state-recovery pattern every signal handler uses — and the
    /// <see cref="UnmanagedCallersOnlyAttribute"/> callback frees it after the single invocation.
    /// </remarks>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureInitialized();
        var handle = GCHandle.Alloc(action);
        unsafe
        {
            var callback = (nint)(delegate* unmanaged[Cdecl]<nint, int>)&OnIdle;
            NativeMethods.g_idle_add_full(NativeMethods.G_PRIORITY_DEFAULT, callback, GCHandle.ToIntPtr(handle), 0);
        }
    }

    /// <summary>
    /// Native <c>GSourceFunc</c> handler shaped as <c>gboolean (gpointer user_data)</c>; recovers the
    /// posted action from <paramref name="userData"/>, frees the handle, runs the action once and
    /// returns 0 (<c>G_SOURCE_REMOVE</c>) so the idle source retires.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnIdle(nint userData)
    {
        if (userData == 0)
            return 0;

        var handle = GCHandle.FromIntPtr(userData);
        var action = handle.Target as Action;
        handle.Free();
        action?.Invoke();
        return 0;
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
