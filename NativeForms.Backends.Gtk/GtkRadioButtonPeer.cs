using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a promoted <see cref="RadioButton"/>, wrapping a real <c>GtkRadioButton</c> (PRD §12)
/// so the desktop draws the ring, the accent dot and the focus outline.
/// </summary>
/// <remarks>
/// GTK refuses to leave a radio group with nothing selected: deactivating the only active member snaps it
/// straight back on. The core, however, allows a <see cref="RadioButton"/> to be cleared outright, and
/// owns grouping itself (siblings sharing a parent). Both are satisfied by giving every peer a private,
/// never-parented group partner — an anchor that is activated to represent "none of them". The visible
/// widget is therefore always in a two-member group, so it can be turned off, and cross-button exclusion
/// stays where the core already implements it.
/// </remarks>
internal sealed class GtkRadioButtonPeer : GtkControlPeer, IRadioButtonPeer {
  private nint _anchor;
  private bool _checked;
  private bool _suppressToggle;

  /// <inheritdoc />
  public event EventHandler? CheckedChanged;

  /// <inheritdoc />
  protected override nint CreateWidget() {
    // Owned outright (the constructor hands back a floating reference), and never shown.
    _anchor = NativeMethods.g_object_ref_sink(NativeMethods.gtk_radio_button_new(0));
    return NativeMethods.gtk_radio_button_new_with_label_from_widget(_anchor, _text);
  }

  /// <inheritdoc />
  protected override void ApplyText(string text) => NativeMethods.gtk_button_set_label(_widget, text);

  /// <inheritdoc />
  public void SetChecked(bool value) {
    _checked = value;
    if (_widget == 0)
      return;

    _suppressToggle = true;
    try {
      // Turning the anchor on is what turns the visible button off — a group always has a member.
      NativeMethods.gtk_toggle_button_set_active(value ? _widget : _anchor, 1);
    } finally {
      _suppressToggle = false;
    }
  }

  /// <inheritdoc />
  public bool GetChecked() => _widget == 0 ? _checked : NativeMethods.gtk_toggle_button_get_active(_widget) != 0;

  /// <inheritdoc />
  protected override void OnWidgetRealized() {
    this.SetChecked(_checked); // flush the state buffered before the widget existed

    var data = this.PinSelf();
    unsafe {
      var callback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnToggled;
      NativeMethods.g_signal_connect_data(_widget, "toggled", callback, data, 0, 0);
    }
  }

  /// <summary>
  /// Reports a user activation. GTK has already switched the widget on by the time this runs, so the
  /// core is told about the click and decides the state — including unchecking the siblings — exactly
  /// as it does for the owner-drawn button.
  /// </summary>
  private void RaiseToggled() {
    if (_suppressToggle || !this.GetChecked())
      return;

    _checked = true;
    CheckedChanged?.Invoke(this, EventArgs.Empty);
  }

  /// <inheritdoc />
  public override void Dispose() {
    if (_anchor != 0) {
      NativeMethods.g_object_unref(_anchor);
      _anchor = 0;
    }

    base.Dispose();
  }

  /// <summary>
  /// Native handler for the radio button's "toggled" signal, shaped as
  /// <c>void (GtkWidget *widget, gpointer user_data)</c>; recovers the peer from
  /// <paramref name="userData"/>.
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void OnToggled(nint widget, nint userData) {
    if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkRadioButtonPeer peer)
      peer.RaiseToggled();
  }
}
