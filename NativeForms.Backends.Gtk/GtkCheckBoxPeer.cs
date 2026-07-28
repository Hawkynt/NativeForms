using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a promoted <see cref="CheckBox"/>, wrapping a real <c>GtkCheckButton</c> (PRD §12).
/// The desktop therefore draws the indicator, animates the hover and press, and exposes the control to
/// assistive technology, none of which the owner-drawn fallback gets.
/// </summary>
/// <remarks>
/// The "toggled" signal fires for programmatic changes as well as user ones, so
/// <see cref="SetChecked"/> suppresses the callback while it pushes a value in. Without that, assigning
/// <c>Checked</c> from a <c>CheckedChanged</c> handler would recurse through the widget.
/// </remarks>
internal sealed class GtkCheckBoxPeer : GtkControlPeer, ICheckBoxPeer
{
    private bool _checked;
    private bool _suppressToggle;

    /// <inheritdoc />
    public event EventHandler? CheckedChanged;

    /// <inheritdoc />
    protected override nint CreateWidget() => NativeMethods.gtk_check_button_new_with_label(_text);

    /// <inheritdoc />
    protected override void ApplyText(string text) => NativeMethods.gtk_button_set_label(_widget, text);

    /// <inheritdoc />
    public void SetChecked(bool value)
    {
        _checked = value;
        if (_widget == 0)
            return;

        _suppressToggle = true;
        try
        {
            NativeMethods.gtk_toggle_button_set_active(_widget, value ? 1 : 0);
        }
        finally
        {
            _suppressToggle = false;
        }
    }

    /// <inheritdoc />
    public bool GetChecked() => _widget == 0 ? _checked : NativeMethods.gtk_toggle_button_get_active(_widget) != 0;

    /// <inheritdoc />
    protected override void OnWidgetRealized()
    {
        // Flush the state buffered before the widget existed, then start listening.
        NativeMethods.gtk_toggle_button_set_active(_widget, _checked ? 1 : 0);

        var data = this.PinSelf();
        unsafe
        {
            var callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnToggled;
            NativeMethods.g_signal_connect_data(_widget, "toggled", callback, data, 0, 0);
        }
    }

    /// <summary>Raises <see cref="CheckedChanged"/> unless the change came from <see cref="SetChecked"/>.</summary>
    private void RaiseToggled()
    {
        if (_suppressToggle)
            return;

        _checked = this.GetChecked();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Native handler for the check button's "toggled" signal, shaped as
    /// <c>void (GtkWidget *widget, gpointer user_data)</c>; recovers the peer from
    /// <paramref name="userData"/>.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnToggled(nint widget, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkCheckBoxPeer peer)
            peer.RaiseToggled();
    }
}
