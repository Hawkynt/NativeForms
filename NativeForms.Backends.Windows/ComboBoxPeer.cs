using System.Drawing;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a promoted <see cref="ComboBox"/> — a native <c>COMBOBOX</c> with
/// <c>CBS_DROPDOWNLIST</c> (PRD §12), so the OS supplies the field, the arrow, the list, its placement
/// and the type-ahead search.
/// </summary>
/// <remarks>
/// A combo box sizes its <em>closed</em> field from the window height and drops the list into whatever is
/// left, so the window is created tall enough for the list the core asked for; the control clips itself to
/// the field when closed, which is why the child bounds still match the control's.
/// </remarks>
internal sealed class ComboBoxPeer : Win32ChildPeer, IComboBoxPeer
{
    /// <summary>How far below the field the list is allowed to drop, in pixels.</summary>
    private const int _DropDownHeight = 240;

    private string[] _items = [];
    private int _selectedIndex = -1;

    /// <inheritdoc/>
    public event EventHandler? SelectionChanged;

    /// <inheritdoc/>
    public event EventHandler? DropDownOpened;

    /// <inheritdoc/>
    public event EventHandler? DropDownClosed;

    /// <inheritdoc/>
    protected override string WindowClass => "COMBOBOX";

    /// <inheritdoc/>
    protected override uint ExtraStyle
        => NativeMethods.CBS_DROPDOWNLIST | NativeMethods.CBS_HASSTRINGS | NativeMethods.WS_TABSTOP | NativeMethods.WS_VSCROLL;

    /// <inheritdoc/>
    public void SetItems(ReadOnlySpan<string> items, int selectedIndex)
    {
        _items = items.ToArray();
        _selectedIndex = selectedIndex;
        if (Handle == 0)
            return;

        NativeMethods.SendMessageW(Handle, NativeMethods.CB_RESETCONTENT, 0, 0);
        foreach (var item in _items)
            NativeMethods.SendMessageStringW(Handle, NativeMethods.CB_ADDSTRING, 0, item);

        NativeMethods.SendMessageW(Handle, NativeMethods.CB_SETCURSEL, selectedIndex, 0);
    }

    /// <inheritdoc/>
    public void SetSelectedIndex(int index)
    {
        _selectedIndex = index;
        if (Handle != 0)
            NativeMethods.SendMessageW(Handle, NativeMethods.CB_SETCURSEL, index, 0);
    }

    /// <inheritdoc/>
    public int GetSelectedIndex()
        => Handle == 0 ? _selectedIndex : (int)NativeMethods.SendMessageW(Handle, NativeMethods.CB_GETCURSEL, 0, 0);

    /// <inheritdoc/>
    public void SetDroppedDown(bool droppedDown)
    {
        if (Handle != 0)
            NativeMethods.SendMessageW(Handle, NativeMethods.CB_SHOWDROPDOWN, droppedDown ? 1 : 0, 0);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The drop-down list lives inside the window rectangle, so the window is made taller than the
    /// control asked for. A closed combo paints only the field regardless, so the two still look the
    /// same size; without the slack the list would have no room and never appear.
    /// </remarks>
    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        if (Handle != 0)
            NativeMethods.MoveWindow(Handle, bounds.X, bounds.Y, bounds.Width, bounds.Height + _DropDownHeight, true);
    }

    /// <inheritdoc/>
    internal override void CreateChildHandle(nint parent, int controlId)
    {
        base.CreateChildHandle(parent, controlId);
        this.SetItems(_items, _selectedIndex); // flush the list buffered before the window existed
        this.SetBounds(_bounds);                // and re-apply the slack the base flush just undid
    }

    /// <inheritdoc/>
    internal override void OnCommand(int notifyCode)
    {
        switch (notifyCode)
        {
            case NativeMethods.CBN_SELCHANGE:
                var index = this.GetSelectedIndex();
                if (index == _selectedIndex)
                    break;

                _selectedIndex = index;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                break;

            case NativeMethods.CBN_DROPDOWN:
                DropDownOpened?.Invoke(this, EventArgs.Empty);
                break;

            case NativeMethods.CBN_CLOSEUP:
                DropDownClosed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
