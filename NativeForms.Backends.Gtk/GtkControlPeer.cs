using System.Drawing;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

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

    /// <summary>Pinning handle that keeps this peer reachable from native signal callbacks.</summary>
    protected GCHandle _selfHandle;

    /// <summary>Converts a managed bool to the 1/0 GLib expects for a <c>gboolean</c>.</summary>
    protected static int Bool(bool value) => value ? 1 : 0;

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
        }

        _parentFixed = parentFixed;
        NativeMethods.gtk_fixed_put(parentFixed, _widget, _bounds.X, _bounds.Y);
        NativeMethods.gtk_widget_set_size_request(_widget, _bounds.Width, _bounds.Height);
        NativeMethods.gtk_widget_set_sensitive(_widget, Bool(_enabled));
        NativeMethods.gtk_widget_set_visible(_widget, Bool(_visible));
        return _widget;
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();

        if (_widget != 0)
        {
            NativeMethods.gtk_widget_destroy(_widget);
            _widget = 0;
        }
    }
}
