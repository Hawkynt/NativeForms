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

    /// <summary>The buffered font override, for subclasses that paint their own text (the multiline placeholder).</summary>
    private protected Font? BufferedFont => _font;

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

        // A rectangle that lost its area means the child scrolled out of view entirely, which only
        // unmapping can express; one that regained it has to come back.
        this.ApplyVisibility();

        // A resize re-runs GTK's allocation, which sizes the widget to its own preference; clamp it
        // back so the new rectangle applies immediately rather than one frame later.
        this.ClampAllocation();
    }

    /// <summary>Guards the re-entrant <c>gtk_widget_size_allocate</c> in <see cref="ClampAllocation"/>.</summary>
    private bool _clamping;

    /// <summary>
    /// Whether GTK's allocation of this widget is forced back to <see cref="_bounds"/>. True for every
    /// widget the toolkit positions inside a container; a top-level window sizes itself against the
    /// window manager instead and opts out.
    /// </summary>
    private protected virtual bool ClampsAllocation => true;

    /// <summary>
    /// Forces this widget's allocation back to the rectangle the toolkit asked for, after GTK has
    /// sized it.
    ///
    /// A <c>GtkFixed</c> asks each child for its preferred size and allocates exactly that, and
    /// <c>gtk_widget_set_size_request</c> only raises the <em>minimum</em> — it never caps anything.
    /// A widget whose natural size exceeds its bounds is therefore allocated the natural size:
    /// measured on a real display, a <c>GtkButton</c> the toolkit sized 60x26 was allocated 87x34, a
    /// <c>GtkLabel</c> sized 70x20 got 187x20, a <c>GtkEntry</c> sized 50x18 got 168x34, and a panel
    /// sized 300x180 got its content's bounding box, 464x206. The children then draw across their
    /// neighbours. <see cref="Control.Bounds"/> is what the widget occupies, so the allocation is
    /// corrected rather than merely clipped: that also keeps the GDK window, the widget clip and
    /// hit-testing consistent with the toolkit's rectangle, which a cairo clip could not — GTK 3's
    /// draw marshaller brackets every handler in <c>cairo_save</c>/<c>cairo_restore</c> and discards
    /// a clip taken there before the container draws its children.
    ///
    /// Text that no longer fits is elided the way the platform would: the peers that own a caption
    /// put their <c>GtkLabel</c> into <c>PANGO_ELLIPSIZE_END</c>.
    /// </summary>
    private protected void ClampAllocation()
    {
        if (_clamping || _widget == 0 || !this.ClampsAllocation)
            return;

        // An empty rectangle means the toolkit has not sized this widget yet — there is nothing
        // authoritative to clamp to, and forcing 0x0 would blank it.
        if (_bounds.Width <= 0 || _bounds.Height <= 0)
            return;

        NativeMethods.gtk_widget_get_allocation(_widget, out var current);
        var corrected = new GdkRectangle
        {
            X = current.X,
            Y = current.Y,
            Width = _bounds.Width,
            Height = _bounds.Height,
        };

        if (current.Width != _bounds.Width || current.Height != _bounds.Height)
        {
            _clamping = true;
            try
            {
                NativeMethods.gtk_widget_size_allocate(_widget, ref corrected);
            }
            finally
            {
                _clamping = false;
            }
        }

        // The clip is pinned as well: GTK 3.20 and later derive a container's clip from the union of
        // its children's, so a scrolling panel claims its whole content bounding box however small
        // its allocation is, and the clip — not the allocation — is what bounds the children when the
        // hierarchy is drawn into one surface. Left alone, a child scrolled off the top is drawn
        // above the panel, across whatever sits there. Bounds are what the widget occupies, so the
        // clip has to say so too. GTK asserts the widget is visible (a hidden one has no clip to
        // set — a collapsed ribbon group or accordion pane), so a hidden widget is skipped: it
        // paints nothing until it is shown again, at which point size-allocate re-pins the clip.
        if (NativeMethods.gtk_widget_get_visible(_widget) != 0)
            NativeMethods.gtk_widget_set_clip(_widget, ref corrected);
    }

    /// <summary>
    /// Connects the "size-allocate" handler that re-applies <see cref="ClampAllocation"/> whenever GTK
    /// re-sizes the widget. Connected <em>after</em> the class closure, so the container has finished
    /// its own allocation pass before the correction lands.
    /// </summary>
    private protected void ConnectAllocationClamp()
    {
        if (_widget == 0)
            return;

        unsafe
        {
            var callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnSizeAllocate;
            NativeMethods.g_signal_connect_data(
                _widget, "size-allocate", callback, this.PinSelf(), 0, NativeMethods.G_CONNECT_AFTER);
        }
    }

    /// <summary>
    /// Adds the wheel to this widget's GDK event mask.
    ///
    /// Windows Forms delivers the wheel to the control under the pointer and lets it bubble to the
    /// nearest scrollable ancestor. A native leaf — a <c>GtkButton</c>, <c>GtkEntry</c>, … — selects
    /// only presses, motion and crossings by default (measured: mask 0x403310 on a button's event
    /// window, with neither scroll bit set), so the wheel is not among the events GDK asks the display
    /// for over that child. Selecting both the discrete and the smooth form puts the event on the
    /// child's own window; GTK then propagates it up the widget chain, because none of these widgets
    /// handles "scroll-event", until it reaches the hosting canvas — which is the scrollable ancestor.
    /// </summary>
    private void SelectScrollEvents()
        => NativeMethods.gtk_widget_add_events(
            _widget, NativeMethods.GDK_SCROLL_MASK | NativeMethods.GDK_SMOOTH_SCROLL_MASK);

    /// <summary>Native "size-allocate" handler: <c>void (GtkWidget*, GdkRectangle*, gpointer)</c>.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnSizeAllocate(nint widget, nint allocation, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkControlPeer peer)
            peer.ClampAllocation();
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
        this.ApplyVisibility();
    }

    /// <summary>
    /// Whether the widget should actually be mapped: what the toolkit asked for, and — for a widget
    /// the toolkit positions — only while the rectangle it was given still has an area.
    ///
    /// An empty rectangle is how a scrolling container says "this child is entirely out of view", and
    /// that cannot be expressed as an allocation. <c>gtk_widget_set_size_request</c> sets a
    /// <em>minimum</em>, so a widget asked for zero height simply falls back to its natural one, and
    /// <see cref="ClampAllocation"/> has nothing authoritative to clamp to either — the child
    /// reappears at full size outside the container, painting over whatever sits beyond it. Unmapping
    /// is the only faithful way to say it is not there.
    /// </summary>
    private bool IsEffectivelyVisible
        => _visible && (!this.ClampsAllocation || (_bounds.Width > 0 && _bounds.Height > 0));

    /// <summary>Pushes <see cref="IsEffectivelyVisible"/> to the widget.</summary>
    private void ApplyVisibility()
    {
        if (_widget == 0)
            return;

        var visible = this.IsEffectivelyVisible;

        // Unmapping a widget that holds the toplevel's focus strands GTK's focus pointer on it: later
        // clicks land but move no focus, and a grab_focus onto another widget only recovers it
        // intermittently. Clearing the window focus first releases the stranded widget so the core's
        // move to the next tab stop (or a later click) grabs focus reliably.
        if (!visible)
            this.SurrenderNativeFocusIfHeld();

        NativeMethods.gtk_widget_set_visible(_widget, Bool(visible));
    }

    /// <summary>Clears the toplevel focus when it rests on this widget or a descendant about to unmap.</summary>
    private void SurrenderNativeFocusIfHeld()
    {
        var toplevel = NativeMethods.gtk_widget_get_toplevel(_widget);
        if (toplevel == 0 || NativeMethods.gtk_widget_is_toplevel(toplevel) == 0)
            return;

        var focus = NativeMethods.gtk_window_get_focus(toplevel);
        if (focus == 0)
            return;

        if (focus == _widget || NativeMethods.gtk_widget_is_ancestor(focus, _widget) != 0)
            NativeMethods.gtk_window_set_focus(toplevel, 0);
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

        var display = NativeMethods.gtk_widget_get_display(_widget);
        if (cursor.Kind == CursorKind.Custom && cursor.Pixels is { } pixels)
        {
            var custom = CreateCustomCursor(display, pixels, cursor.Width, cursor.Height, cursor.HotspotX, cursor.HotspotY);
            NativeMethods.gdk_window_set_cursor(window, custom); // the window keeps its own reference
            if (custom != 0)
                NativeMethods.g_object_unref(custom);

            return;
        }

        NativeMethods.gdk_window_set_cursor(window, CursorFor(display, cursor.Kind));
    }

    /// <summary>Builds a native <c>GdkCursor</c> from ARGB pixels and a hotspot, via a temporary RGBA pixbuf.</summary>
    private static nint CreateCustomCursor(nint display, int[] argb, int width, int height, int hotspotX, int hotspotY)
    {
        if (width <= 0 || height <= 0 || display == 0)
            return 0;

        var pixbuf = NativeMethods.gdk_pixbuf_new(NativeMethods.GDK_COLORSPACE_RGB, 1, 8, width, height);
        if (pixbuf == 0)
            return 0;

        unsafe
        {
            var stride = NativeMethods.gdk_pixbuf_get_rowstride(pixbuf);
            var pixels = (byte*)NativeMethods.gdk_pixbuf_get_pixels(pixbuf);
            for (var y = 0; y < height; ++y)
            {
                var row = pixels + (y * stride);
                for (var x = 0; x < width; ++x)
                {
                    var source = unchecked((uint)argb[(y * width) + x]);
                    row[x * 4] = (byte)((source >> 16) & 0xFF);
                    row[(x * 4) + 1] = (byte)((source >> 8) & 0xFF);
                    row[(x * 4) + 2] = (byte)(source & 0xFF);
                    row[(x * 4) + 3] = (byte)(source >> 24);
                }
            }
        }

        var cursor = NativeMethods.gdk_cursor_new_from_pixbuf(display, pixbuf, hotspotX, hotspotY);
        NativeMethods.g_object_unref(pixbuf);
        return cursor;
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
            return this.UnmappedPointToScreen(clientPoint);

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

    /// <summary>
    /// Where the widget would map to if it were on screen, for the moment it is not: a widget an
    /// ancestor has scrolled entirely out of view is unmapped and so owns no <c>GdkWindow</c> to ask.
    ///
    /// The toolkit still knows the answer — the rectangle it assigned inside the parent surface —
    /// and callers depend on it: hit-testing, drag sources and tooltips all map client points to the
    /// screen, and the result must describe where the control is, not whether the peer happens to be
    /// mapped right now.
    /// </summary>
    private Point UnmappedPointToScreen(Point clientPoint)
    {
        if (_parentFixed == 0)
            return clientPoint;

        var parentWindow = NativeMethods.gtk_widget_get_window(_parentFixed);
        if (parentWindow == 0)
            return clientPoint;

        NativeMethods.gdk_window_get_origin(parentWindow, out var x, out var y);
        return new(x + _bounds.X + clientPoint.X, y + _bounds.Y + clientPoint.Y);
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
            ConnectAllocationClamp();
            SelectScrollEvents();
            ConnectPointerSignals();
        }

        _parentFixed = parentFixed;
        NativeMethods.gtk_fixed_put(parentFixed, _widget, _bounds.X, _bounds.Y);
        NativeMethods.gtk_widget_set_size_request(_widget, _bounds.Width, _bounds.Height);
        NativeMethods.gtk_widget_set_sensitive(_widget, Bool(_enabled));
        this.ApplyFont();
        if (!_foreColor.IsEmpty || !_backColor.IsEmpty)
            this.ApplyColors();

        this.ApplyCursor();
        this.ApplyVisibility();
        this.OnParented(); // the widget is now inside its container (and so its window)
        return _widget;
    }

    /// <summary>Runs after the widget has been placed into its parent container — the point at which
    /// operations that need the toplevel window (grabbing the default) are safe. The base does nothing.</summary>
    private protected virtual void OnParented() { }

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

    /// <summary>
    /// Whether this peer needs its own native motion/leave connection to report hover. A peer that
    /// already owns a full mouse pipeline — the canvas — overrides this to <see langword="false"/>
    /// and forwards its existing events instead; connecting a second pair of handlers on the same
    /// widget would double-deliver its input.
    /// </summary>
    private protected virtual bool NeedsPointerSignals => true;

    /// <summary>
    /// Wires pointer motion and leave on the widget so every native peer — not just the canvas —
    /// reports hover. Both handlers return 0 (unhandled) so the widget's own motion and crossing
    /// behavior, prelight included, is left completely intact; they only observe.
    /// </summary>
    private void ConnectPointerSignals()
    {
        if (_widget == 0 || !this.NeedsPointerSignals)
            return;

        NativeMethods.gtk_widget_add_events(
            _widget, NativeMethods.GDK_POINTER_MOTION_MASK | NativeMethods.GDK_LEAVE_NOTIFY_MASK);

        var data = this.PinSelf();
        unsafe
        {
            NativeMethods.g_signal_connect_data(
                _widget, "motion-notify-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnPointerMotionSignal, data, 0, 0);
            NativeMethods.g_signal_connect_data(
                _widget, "leave-notify-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnPointerLeaveSignal, data, 0, 0);
        }
    }

    /// <inheritdoc/>
    public event EventHandler<MouseEventArgs>? PointerMove;

    /// <inheritdoc/>
    public event EventHandler? PointerLeave;

    /// <summary>Raises <see cref="PointerMove"/>; used by peers that feed the channel from a mouse
    /// pipeline they already own rather than from a connection of their own.</summary>
    private protected void RaisePointerMove(int x, int y)
        => this.PointerMove?.Invoke(this, new MouseEventArgs(MouseButtons.None, x, y, 0));

    /// <summary>Raises <see cref="PointerLeave"/>; the counterpart of <see cref="RaisePointerMove"/>.</summary>
    private protected void RaisePointerLeave() => this.PointerLeave?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc/>
    public void ShowToolTip(string? text)
    {
        if (_widget == 0)
            return;

        if (string.IsNullOrEmpty(text))
        {
            NativeMethods.gtk_widget_set_has_tooltip(_widget, 0);
            return;
        }

        NativeMethods.gtk_widget_set_tooltip_text(_widget, text);
        NativeMethods.gtk_widget_trigger_tooltip_query(_widget);
    }

    /// <summary>Native "motion-notify-event" handler shared by every GTK peer.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnPointerMotionSignal(nint widget, nint eventPtr, nint userData)
    {
        if (userData == 0 || GCHandle.FromIntPtr(userData).Target is not GtkControlPeer peer)
            return 0;

        // The args are built only once someone is listening: motion arrives at pointer rate, and an
        // unsubscribed peer must not allocate for every step across it.
        if (peer.PointerMove is not { } handler)
            return 0;

        unsafe
        {
            ref var e = ref Unsafe.AsRef<GdkEventMotion>((void*)eventPtr);
            handler(peer, new MouseEventArgs(MouseButtons.None, (int)e.X, (int)e.Y, 0));
        }

        return 0;
    }

    /// <summary>Native "leave-notify-event" handler shared by every GTK peer.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnPointerLeaveSignal(nint widget, nint eventPtr, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkControlPeer peer)
            peer.PointerLeave?.Invoke(peer, EventArgs.Empty);

        return 0;
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
