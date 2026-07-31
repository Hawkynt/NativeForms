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
/// <para>
/// The "toggled" signal fires for programmatic changes as well as user ones, so
/// <see cref="SetCheckState"/> suppresses the callback while it pushes a value in. Without that,
/// assigning <c>Checked</c> from a <c>CheckedChanged</c> handler would recurse through the widget.
/// </para>
/// <para>
/// GTK's indeterminate ("inconsistent") state is presentation only — the widget paints the dash but
/// will not cycle into it, unlike Win32's <c>BS_AUTO3STATE</c> and AppKit's mixed state. So this peer
/// runs the third step of the cycle itself, reporting the state the other two backends reach on their
/// own. Getting that wrong is invisible in a screenshot and obvious in use: the box would skip straight
/// from checked back to unchecked on one desktop out of three.
/// </para>
/// </remarks>
internal sealed class GtkCheckBoxPeer : GtkControlPeer, ICheckBoxPeer
{
    private CheckState _state;
    private bool _threeState;
    private bool _suppressToggle;

    /// <inheritdoc />
    public event EventHandler? CheckedChanged;

    /// <inheritdoc />
    protected override nint CreateWidget() => NativeMethods.gtk_check_button_new_with_label(_text);

    /// <inheritdoc />
    protected override void ApplyText(string text) => NativeMethods.gtk_button_set_label(_widget, text);

    /// <inheritdoc />
    public void SetChecked(bool value) => this.SetCheckState(value ? CheckState.Checked : CheckState.Unchecked);

    /// <inheritdoc />
    public bool GetChecked() => this.GetCheckState() is not CheckState.Unchecked;

    /// <inheritdoc />
    public void SetCheckState(CheckState value)
    {
        _state = value;
        if (_widget == 0)
            return;

        _suppressToggle = true;
        try
        {
            this.Apply(value);
        }
        finally
        {
            _suppressToggle = false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Answered from the peer's own field rather than from <c>gtk_toggle_button_get_active</c>, which
    /// cannot distinguish checked from indeterminate — both are active widgets.
    /// </remarks>
    public CheckState GetCheckState() => _state;

    /// <inheritdoc />
    public void SetThreeState(bool value) => _threeState = value;

    /// <summary>
    /// Pushes a state onto the widget. Indeterminate is drawn as an inconsistent <em>active</em> button:
    /// GTK dims the dash on an inactive one, which reads as disabled rather than as mixed.
    /// </summary>
    private void Apply(CheckState state)
    {
        NativeMethods.gtk_toggle_button_set_active(_widget, state is CheckState.Unchecked ? 0 : 1);
        NativeMethods.gtk_toggle_button_set_inconsistent(_widget, state is CheckState.Indeterminate ? 1 : 0);
    }

    /// <inheritdoc />
    protected override void OnWidgetRealized()
    {
        // Flush the state buffered before the widget existed, then start listening.
        this.Apply(_state);

        var data = this.PinSelf();
        unsafe
        {
            var callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnToggled;
            NativeMethods.g_signal_connect_data(_widget, "toggled", callback, data, 0, 0);
        }
    }

    /// <summary>Raises <see cref="CheckedChanged"/> unless the change came from <see cref="SetCheckState"/>.</summary>
    private void RaiseToggled()
    {
        if (_suppressToggle)
            return;

        // The widget has already flipped its own active flag, which for a two-state box is the answer.
        // For the third step it is not: GTK has no cycle through inconsistent, so the state the other
        // backends would have reached is worked out here and pushed back onto the widget.
        this.SetCheckState(_state switch
        {
            CheckState.Unchecked => CheckState.Checked,
            CheckState.Checked when _threeState => CheckState.Indeterminate,
            _ => CheckState.Unchecked,
        });

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
