using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// Shared base for GTK control peers. Buffers the control's text, bounds, visibility and enabled
/// state until the native widget exists, then applies every subsequent write to the live widget.
/// </summary>
/// <remarks>
/// Child peers (buttons, labels, canvases) are created lazily: the core sets their buffered state
/// during realization, and the owning container — <see cref="GtkWindowPeer.AddChild"/> or
/// <see cref="GtkCanvasPeer.AddChild"/> — calls <see cref="Realize"/> to create the widget, flush
/// the buffer and drop it into that container's <c>GtkFixed</c>. The window peer creates its own
/// widget eagerly and overrides the pieces that differ (title, sizing).
/// </remarks>
internal abstract class GtkControlPeer : IControlPeer
{
    /// <summary>The native widget handle, or 0 before the widget is created.</summary>
    protected nint _widget;

    /// <summary>The native widget handle for backend-side consumers (dialog transient parents).</summary>
    internal nint WidgetHandle => _widget;

    /// <summary>The parent <c>GtkFixed</c> this widget lives in, or 0 while unparented.</summary>
    protected nint _parentFixed;

    /// <summary>Buffered caption/label text, applied on realization and forwarded live thereafter.</summary>
    protected string _text = string.Empty;

    /// <summary>Buffered bounds in parent-client pixels.</summary>
    protected Rectangle _bounds;

    /// <summary>Buffered visibility.</summary>
    protected bool _visible = true;

    /// <summary>Buffered enabled state.</summary>
    protected bool _enabled = true;

    /// <summary>Buffered font override, or null while the theme font applies.</summary>
    private Font? _font;

    /// <summary>Buffered text color; <see cref="Color.Empty"/> while the theme color applies.</summary>
    private Color _foreColor;

    /// <summary>Buffered background color; <see cref="Color.Empty"/> while the theme color applies.</summary>
    private Color _backColor;

    /// <summary>Buffered cursor, or null for the default pointer.</summary>
    private Cursor? _cursor;

    /// <summary>Whether the "realize" signal is already connected (for late GDK-window state).</summary>
    private bool _realizeHooked;

    /// <summary>One shared <c>GdkCursor</c> per CSS name, created lazily and kept for the process.</summary>
    private static readonly Dictionary<string, nint> _cursors = new();

    /// <summary>Pinning handle that keeps this peer reachable from native signal callbacks.</summary>
    protected GCHandle _selfHandle;

    /// <summary>Allocates the pinning handle on first use and returns it as callback user data.</summary>
    protected nint PinSelf()
    {
        if (!_selfHandle.IsAllocated)
            _selfHandle = GCHandle.Alloc(this);

        return GCHandle.ToIntPtr(_selfHandle);
    }

    /// <summary>Converts a managed bool to the 1/0 GLib expects for a <c>gboolean</c>.</summary>
    protected static int Bool(bool value) => value ? 1 : 0;

    /// <inheritdoc />
    public event EventHandler? GotFocus;

    /// <inheritdoc />
    public event EventHandler? LostFocus;

    /// <summary>
    /// The widget that actually takes keyboard focus: the widget itself unless a composite peer (a
    /// multiline text box's scrolled window) hosts an inner editor and overrides this.
    /// </summary>
    private protected virtual nint FocusWidget => _widget;

    /// <inheritdoc />
    public void Focus()
    {
        var widget = FocusWidget;
        if (widget != 0)
            NativeMethods.gtk_widget_grab_focus(widget);
    }

    /// <inheritdoc />
    public virtual void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        if (_widget == 0)
            return;

        NativeMethods.gtk_widget_set_size_request(_widget, bounds.Width, bounds.Height);
        if (_parentFixed != 0)
            NativeMethods.gtk_fixed_move(_parentFixed, _widget, bounds.X, bounds.Y);
    }

    /// <inheritdoc />
    public void SetText(string text)
    {
        _text = text ?? string.Empty;
        if (_widget != 0)
            ApplyText(_text);
    }

    /// <inheritdoc />
    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_widget != 0)
            NativeMethods.gtk_widget_set_visible(_widget, Bool(visible));
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (_widget != 0)
            NativeMethods.gtk_widget_set_sensitive(_widget, Bool(enabled));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Applied through <c>gtk_widget_override_font</c> — deprecated since GTK 3.16 in favor of
    /// per-widget CSS providers, but fully functional through GTK 3.24 and free of a provider
    /// object per widget, which matches this toolkit's footprint rules.
    /// </remarks>
    public void SetFont(Font font)
    {
        _font = font;
        if (_widget != 0)
            this.ApplyFont();
    }

    /// <inheritdoc />
    /// <remarks>Applied through <c>gtk_widget_override_color</c>/<c>_background_color</c> — the same
    /// deprecated-but-functional GTK 3 override family as <see cref="SetFont"/>.</remarks>
    public void SetColors(Color foreColor, Color backColor)
    {
        _foreColor = foreColor;
        _backColor = backColor;
        if (_widget != 0)
            this.ApplyColors();
    }

    /// <inheritdoc />
    public void SetCursor(Cursor cursor)
    {
        _cursor = cursor;
        if (_widget != 0)
            this.ApplyCursor();
    }

    /// <summary>Pushes the buffered font override onto the live widget.</summary>
    private void ApplyFont()
    {
        if (_font is not { } font)
            return;

        var description = GtkGraphics.CreateFontDescription(font);
        NativeMethods.gtk_widget_override_font(_widget, description);
        NativeMethods.pango_font_description_free(description);
    }

    /// <summary>Pushes the buffered color overrides onto the live widget (empty clears an override).</summary>
    private void ApplyColors()
    {
        if (_foreColor.IsEmpty)
            NativeMethods.gtk_widget_override_color(_widget, NativeMethods.GTK_STATE_FLAG_NORMAL, 0);
        else
            NativeMethods.gtk_widget_override_color(_widget, NativeMethods.GTK_STATE_FLAG_NORMAL, ToRgba(_foreColor));

        if (_backColor.IsEmpty)
            NativeMethods.gtk_widget_override_background_color(_widget, NativeMethods.GTK_STATE_FLAG_NORMAL, 0);
        else
            NativeMethods.gtk_widget_override_background_color(_widget, NativeMethods.GTK_STATE_FLAG_NORMAL, ToRgba(_backColor));
    }

    /// <summary>
    /// Sets the buffered cursor on the widget's <c>GdkWindow</c>. Before the widget is realized (no
    /// GDK window yet — GTK creates it on show), a one-shot "realize" signal hook re-applies it.
    /// </summary>
    private void ApplyCursor()
    {
        if (_cursor is not { } cursor)
            return;

        var window = NativeMethods.gtk_widget_get_window(_widget);
        if (window == 0)
        {
            this.HookRealize();
            return;
        }

        NativeMethods.gdk_window_set_cursor(window, CursorFor(NativeMethods.gtk_widget_get_display(_widget), cursor.Kind));
    }

    /// <summary>Connects the "realize" signal once so late GDK-window state (the cursor) is applied on show.</summary>
    private void HookRealize()
    {
        if (_realizeHooked)
            return;

        _realizeHooked = true;
        unsafe
        {
            var callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnWidgetGdkRealized;
            NativeMethods.g_signal_connect_data(_widget, "realize", callback, this.PinSelf(), 0, 0);
        }
    }

    /// <summary>
    /// Native "realize" handler, shaped as <c>void (GtkWidget*, gpointer)</c>: the GDK window now
    /// exists, so the buffered cursor can finally land on it.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnWidgetGdkRealized(nint widget, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkControlPeer peer)
            peer.ApplyCursor();
    }

    /// <summary>The shared <c>GdkCursor</c> for a stock shape, created from its CSS name on first use.</summary>
    private static nint CursorFor(nint display, CursorKind kind)
    {
        var name = kind switch
        {
            CursorKind.Hand => "pointer",
            CursorKind.IBeam => "text",
            CursorKind.Wait => "wait",
            CursorKind.Cross => "crosshair",
            CursorKind.SizeWE => "ew-resize",
            CursorKind.SizeNS => "ns-resize",
            CursorKind.SizeNWSE => "nwse-resize",
            CursorKind.SizeNESW => "nesw-resize",
            CursorKind.No => "not-allowed",
            CursorKind.SizeAll => "move",
            CursorKind.Help => "help",
            CursorKind.AppStarting => "progress",
            CursorKind.VSplit => "col-resize",
            CursorKind.HSplit => "row-resize",
            _ => "default",
        };

        if (_cursors.TryGetValue(name, out var cursor))
            return cursor;

        cursor = NativeMethods.gdk_cursor_new_from_name(display, name);
        if (cursor != 0)
            _cursors[name] = cursor;

        return cursor;
    }

    /// <summary>Converts a managed color to the 0..1 doubles of a <c>GdkRGBA</c>.</summary>
    private static GdkRGBA ToRgba(Color color) => new()
    {
        Red = color.R / 255.0,
        Green = color.G / 255.0,
        Blue = color.B / 255.0,
        Alpha = color.A / 255.0,
    };

    /// <inheritdoc />
    public Point PointToScreen(Point clientPoint)
    {
        if (_widget == 0)
            return clientPoint;

        var window = NativeMethods.gtk_widget_get_window(_widget);
        if (window == 0)
            return clientPoint;

        NativeMethods.gdk_window_get_origin(window, out var x, out var y);

        // A window-less widget shares its parent's GdkWindow; its own client origin sits at the
        // allocation offset inside that window.
        if (NativeMethods.gtk_widget_get_has_window(_widget) == 0)
        {
            NativeMethods.gtk_widget_get_allocation(_widget, out var allocation);
            x += allocation.X;
            y += allocation.Y;
        }

        return new(x + clientPoint.X, y + clientPoint.Y);
    }

    /// <summary>Creates the concrete native widget, using the buffered state where relevant.</summary>
    protected abstract nint CreateWidget();

    /// <summary>Pushes text into the live widget with the widget-specific setter.</summary>
    protected virtual void ApplyText(string text) { }

    /// <summary>Hook invoked once, right after the widget is first created (for wiring signals).</summary>
    protected virtual void OnWidgetRealized() { }

    /// <summary>
    /// Creates the widget (if needed), flushes the buffered state, and places it into
    /// <paramref name="parentFixed"/> at the buffered bounds. Called by the owning window.
    /// </summary>
    internal nint Realize(nint parentFixed)
    {
        if (_widget == 0)
        {
            _widget = CreateWidget();
            OnWidgetRealized();
            ConnectFocusSignals();
        }

        _parentFixed = parentFixed;
        NativeMethods.gtk_fixed_put(parentFixed, _widget, _bounds.X, _bounds.Y);
        NativeMethods.gtk_widget_set_size_request(_widget, _bounds.Width, _bounds.Height);
        NativeMethods.gtk_widget_set_sensitive(_widget, Bool(_enabled));
        this.ApplyFont();
        if (!_foreColor.IsEmpty || !_backColor.IsEmpty)
            this.ApplyColors();

        this.ApplyCursor();
        NativeMethods.gtk_widget_set_visible(_widget, Bool(_visible));
        return _widget;
    }

    /// <summary>
    /// Wires the shared focus-in/out signals on the focus-taking widget. Called once per widget
    /// creation — a peer that recreates its widget (the multiline flip) re-enters through
    /// <see cref="Realize"/> and rewires the fresh widget; the eagerly built popup surface calls it
    /// from its constructor.
    /// </summary>
    private protected void ConnectFocusSignals()
    {
        var widget = FocusWidget;
        if (widget == 0)
            return;

        if (!_selfHandle.IsAllocated)
            _selfHandle = GCHandle.Alloc(this);

        unsafe
        {
            var data = GCHandle.ToIntPtr(_selfHandle);
            NativeMethods.g_signal_connect_data(
                widget, "focus-in-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnFocusInSignal, data, 0, 0);
            NativeMethods.g_signal_connect_data(
                widget, "focus-out-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnFocusOutSignal, data, 0, 0);
        }
    }

    /// <summary>Native "focus-in-event" handler shared by every GTK peer.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnFocusInSignal(nint widget, nint eventPtr, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkControlPeer peer)
            peer.GotFocus?.Invoke(peer, EventArgs.Empty);

        return 0;
    }

    /// <summary>Native "focus-out-event" handler shared by every GTK peer.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnFocusOutSignal(nint widget, nint eventPtr, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkControlPeer peer)
            peer.LostFocus?.Invoke(peer, EventArgs.Empty);

        return 0;
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
        // Destroy before freeing the pinning handle: destroying can emit signals ("destroy",
        // "focus-out-event" …) whose callbacks recover the peer through that handle.
        if (_widget != 0)
        {
            NativeMethods.gtk_widget_destroy(_widget);
            _widget = 0;
        }

        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }
}
