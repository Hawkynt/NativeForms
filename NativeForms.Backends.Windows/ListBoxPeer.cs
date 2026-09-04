using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a promoted <see cref="ListBox"/> — a native <c>LISTBOX</c> (PRD §12), so the OS
/// supplies the rows, the selection, the scroll bar, the wheel handling and the type-ahead search.
/// </summary>
/// <remarks>
/// <c>LBS_NOTIFY</c> is what makes the control report selections and double clicks through
/// <c>WM_COMMAND</c> at all; without it a list box is silent.
/// </remarks>
internal sealed class ListBoxPeer : Win32ChildPeer, IListBoxPeer {
  private string[] _items = [];
  private int _selectedIndex = -1;

  /// <inheritdoc/>
  public event EventHandler? SelectionChanged;

  /// <inheritdoc/>
  public event EventHandler? ItemActivated;

  /// <inheritdoc/>
  protected override string WindowClass => "LISTBOX";

  /// <inheritdoc/>
  protected override uint ExtraStyle
      => NativeMethods.LBS_NOTIFY | NativeMethods.WS_TABSTOP | NativeMethods.WS_VSCROLL | NativeMethods.WS_BORDER;

  /// <inheritdoc/>
  public void SetItems(ReadOnlySpan<string> items, int selectedIndex) {
    _items = items.ToArray();
    _selectedIndex = selectedIndex;
    if (Handle == 0)
      return;

    NativeMethods.SendMessageW(Handle, NativeMethods.LB_RESETCONTENT, 0, 0);
    foreach (var item in _items)
      NativeMethods.SendMessageStringW(Handle, NativeMethods.LB_ADDSTRING, 0, item);

    NativeMethods.SendMessageW(Handle, NativeMethods.LB_SETCURSEL, selectedIndex, 0);
  }

  /// <inheritdoc/>
  public void SetSelectedIndex(int index) {
    _selectedIndex = index;
    if (Handle != 0)
      NativeMethods.SendMessageW(Handle, NativeMethods.LB_SETCURSEL, index, 0);
  }

  /// <inheritdoc/>
  public int GetSelectedIndex()
      => Handle == 0 ? _selectedIndex : (int)NativeMethods.SendMessageW(Handle, NativeMethods.LB_GETCURSEL, 0, 0);

  /// <inheritdoc/>
  public void ScrollIntoView(int index) {
    if (Handle != 0 && index >= 0)
      NativeMethods.SendMessageW(Handle, NativeMethods.LB_SETTOPINDEX, index, 0);
  }

  /// <inheritdoc/>
  public int GetTopIndex()
      => Handle == 0 ? 0 : (int)NativeMethods.SendMessageW(Handle, NativeMethods.LB_GETTOPINDEX, 0, 0);

  /// <inheritdoc/>
  public int IndexFromPoint(int x, int y) {
    if (Handle == 0)
      return -1;

    // The result packs the nearest index in the low word and a miss flag in the high word, so a
    // point below the last row still answers with that row unless the flag is set.
    var packed = NativeMethods.SendMessageW(Handle, NativeMethods.LB_ITEMFROMPOINT, 0, (y << 16) | (x & 0xFFFF));
    return ((packed >> 16) & 0xFFFF) != 0 ? -1 : (int)(packed & 0xFFFF);
  }

  /// <inheritdoc/>
  internal override void CreateChildHandle(nint parent, int controlId) {
    base.CreateChildHandle(parent, controlId);
    this.SetItems(_items, _selectedIndex); // flush the list buffered before the window existed
  }

  /// <inheritdoc/>
  internal override void OnCommand(int notifyCode) {
    switch (notifyCode) {
      case NativeMethods.LBN_SELCHANGE:
        var index = this.GetSelectedIndex();
        if (index == _selectedIndex)
          break;

        _selectedIndex = index;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        break;

      case NativeMethods.LBN_DBLCLK:
        ItemActivated?.Invoke(this, EventArgs.Empty);
        break;
    }
  }
}
