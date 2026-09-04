using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a promoted <see cref="ComboBox"/>, wrapping a real <c>GtkComboBoxText</c> (PRD §12)
/// so the desktop supplies the field, the arrow, the list and its placement, scrolling and key search.
/// </summary>
/// <remarks>
/// GTK reports the list opening and closing through the <c>popup-shown</c> property rather than a pair of
/// signals, so both edges arrive on one <c>notify</c> callback and are told apart by reading the property
/// back.
/// </remarks>
internal sealed class GtkComboBoxPeer : GtkControlPeer, IComboBoxPeer {
  private string[] _items = [];
  private int _selectedIndex = -1;
  private bool _suppress;
  private bool _shown;

  /// <inheritdoc />
  public event EventHandler? SelectionChanged;

  /// <inheritdoc />
  public event EventHandler? DropDownOpened;

  /// <inheritdoc />
  public event EventHandler? DropDownClosed;

  /// <inheritdoc />
  protected override nint CreateWidget() => NativeMethods.gtk_combo_box_text_new();

  /// <inheritdoc />
  protected override void ApplyText(string text) { }

  /// <inheritdoc />
  public void SetItems(ReadOnlySpan<string> items, int selectedIndex) {
    _items = items.ToArray();
    _selectedIndex = selectedIndex;
    if (_widget == 0)
      return;

    _suppress = true;
    try {
      NativeMethods.gtk_combo_box_text_remove_all(_widget);
      foreach (var item in _items)
        NativeMethods.gtk_combo_box_text_append_text(_widget, item);

      NativeMethods.gtk_combo_box_set_active(_widget, selectedIndex);
    } finally {
      _suppress = false;
    }
  }

  /// <inheritdoc />
  public void SetSelectedIndex(int index) {
    _selectedIndex = index;
    if (_widget == 0)
      return;

    _suppress = true;
    try {
      NativeMethods.gtk_combo_box_set_active(_widget, index);
    } finally {
      _suppress = false;
    }
  }

  /// <inheritdoc />
  public int GetSelectedIndex() => _widget == 0 ? _selectedIndex : NativeMethods.gtk_combo_box_get_active(_widget);

  /// <inheritdoc />
  public void SetDroppedDown(bool droppedDown) {
    if (_widget == 0)
      return;

    if (droppedDown)
      NativeMethods.gtk_combo_box_popup(_widget);
    else
      NativeMethods.gtk_combo_box_popdown(_widget);
  }

  /// <inheritdoc />
  protected override void OnWidgetRealized() {
    this.SetItems(_items, _selectedIndex); // flush the list buffered before the widget existed

    var data = this.PinSelf();
    unsafe {
      var changed = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnChanged;
      NativeMethods.g_signal_connect_data(_widget, "changed", changed, data, 0, 0);

      var popup = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnPopupShown;
      NativeMethods.g_signal_connect_data(_widget, "notify::popup-shown", popup, data, 0, 0);
    }
  }

  /// <summary>Reports a user selection.</summary>
  private void RaiseChanged() {
    if (_suppress)
      return;

    var index = this.GetSelectedIndex();
    if (index == _selectedIndex)
      return;

    _selectedIndex = index;
    SelectionChanged?.Invoke(this, EventArgs.Empty);
  }

  /// <summary>Turns the property notification into the edge it represents.</summary>
  private void RaisePopupShown() {
    var shown = NativeMethods.g_object_get_bool(_widget, "popup-shown");
    if (shown == _shown)
      return;

    _shown = shown;
    if (shown)
      DropDownOpened?.Invoke(this, EventArgs.Empty);
    else
      DropDownClosed?.Invoke(this, EventArgs.Empty);
  }

  /// <summary>Native handler for "changed", shaped as <c>void (GtkComboBox *, gpointer)</c>.</summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void OnChanged(nint widget, nint userData) {
    if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkComboBoxPeer peer)
      peer.RaiseChanged();
  }

  /// <summary>
  /// Native handler for "notify::popup-shown", shaped as
  /// <c>void (GObject *, GParamSpec *, gpointer)</c>.
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void OnPopupShown(nint widget, nint pspec, nint userData) {
    if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkComboBoxPeer peer)
      peer.RaisePopupShown();
  }
}
