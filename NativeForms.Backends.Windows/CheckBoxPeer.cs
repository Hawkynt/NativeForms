using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a promoted <see cref="CheckBox"/> — a native <c>BUTTON</c> window with
/// <c>BS_AUTOCHECKBOX</c> (PRD §12), so the OS draws the indicator, animates the hover and press, and
/// exposes the control to UI Automation.
/// </summary>
/// <remarks>
/// <c>BS_AUTOCHECKBOX</c> toggles itself before it notifies, so the state is read back from the control
/// on <c>BN_CLICKED</c> rather than inferred. <c>BM_SETCHECK</c> does not raise a notification, which is
/// what lets <see cref="SetChecked"/> push a value in silently.
/// </remarks>
internal sealed class CheckBoxPeer : Win32ChildPeer, ICheckBoxPeer
{
    private bool _checked;

    /// <inheritdoc/>
    public event EventHandler? CheckedChanged;

    /// <inheritdoc/>
    protected override string WindowClass => "BUTTON";

    /// <inheritdoc/>
    protected override uint ExtraStyle => NativeMethods.BS_AUTOCHECKBOX | NativeMethods.WS_TABSTOP;

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
                _checked = this.GetChecked();
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
