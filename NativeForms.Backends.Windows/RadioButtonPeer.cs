using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a promoted <see cref="RadioButton"/> — a native <c>BUTTON</c> window with
/// <c>BS_RADIOBUTTON</c> (PRD §12), so the OS draws the ring, the dot and the themed focus rectangle.
/// </summary>
/// <remarks>
/// Deliberately <em>not</em> <c>BS_AUTORADIOBUTTON</c>: the automatic style defines its own group from the
/// <c>WS_GROUP</c> runs of the tab order, which is a different notion of "group" from the core's (the
/// controls sharing a parent) and would have the two fighting over the selection. The plain style only
/// reports the click; the core checks the button and clears its siblings, as it does when owner-drawn.
/// </remarks>
internal sealed class RadioButtonPeer : Win32ChildPeer, IRadioButtonPeer
{
    /// <summary>A radio button that reports clicks and leaves the state to its host.</summary>
    private const uint _BS_RADIOBUTTON = 0x00000004;

    private bool _checked;

    /// <inheritdoc/>
    public event EventHandler? CheckedChanged;

    /// <inheritdoc/>
    protected override string WindowClass => "BUTTON";

    /// <inheritdoc/>
    protected override uint ExtraStyle => _BS_RADIOBUTTON | NativeMethods.WS_TABSTOP;

    /// <inheritdoc/>
    public void SetChecked(bool value)
    {
        _checked = value;
        if (Handle != 0)
            NativeMethods.SendMessageW(Handle, NativeMethods.BM_SETCHECK, value ? NativeMethods.BST_CHECKED : NativeMethods.BST_UNCHECKED, 0);
    }

    /// <inheritdoc/>
    public bool GetChecked()
        => Handle == 0 ? _checked : NativeMethods.SendMessageW(Handle, NativeMethods.BM_GETCHECK, 0, 0) == NativeMethods.BST_CHECKED;

    /// <inheritdoc/>
    internal override void CreateChildHandle(nint parent, int controlId)
    {
        base.CreateChildHandle(parent, controlId);
        this.SetChecked(_checked); // flush the state buffered before the window existed
    }

    /// <inheritdoc/>
    internal override void OnCommand(int notifyCode)
    {
        switch (notifyCode)
        {
            case NativeMethods.BN_CLICKED:
                CheckedChanged?.Invoke(this, EventArgs.Empty);
                break;

            case NativeMethods.BN_SETFOCUS:
                RaiseGotFocus();
                break;

            case NativeMethods.BN_KILLFOCUS:
                RaiseLostFocus();
                break;
        }
    }
}
