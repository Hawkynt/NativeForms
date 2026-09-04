using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a promoted <see cref="CheckBox"/> — a native <c>BUTTON</c> window with
/// <c>BS_AUTOCHECKBOX</c> (PRD §12), so the OS draws the indicator, animates the hover and press, and
/// exposes the control to UI Automation.
/// </summary>
/// <remarks>
/// <para>
/// <c>BS_AUTOCHECKBOX</c> toggles itself before it notifies, so the state is read back from the control
/// on <c>BN_CLICKED</c> rather than inferred. <c>BM_SETCHECK</c> does not raise a notification, which is
/// what lets <see cref="SetChecked"/> push a value in silently.
/// </para>
/// <para>
/// A three-state box wears <c>BS_AUTO3STATE</c> instead, whose own click cycle — unchecked → checked →
/// indeterminate — is exactly the one <see cref="CheckBox"/> runs, so the read-back keeps agreeing with
/// the core rather than needing a second opinion. The style is fixed at creation, so a control that
/// gains or loses the third state afterwards has it swapped in place on the live window.
/// </para>
/// </remarks>
internal sealed class CheckBoxPeer : Win32ChildPeer, ICheckBoxPeer {
  private CheckState _state;
  private bool _threeState;

  /// <inheritdoc/>
  public event EventHandler? CheckedChanged;

  /// <inheritdoc/>
  protected override string WindowClass => "BUTTON";

  /// <inheritdoc/>
  protected override uint ExtraStyle
      => (_threeState ? NativeMethods.BS_AUTO3STATE : NativeMethods.BS_AUTOCHECKBOX) | NativeMethods.WS_TABSTOP;

  /// <inheritdoc/>
  public void SetChecked(bool value) => this.SetCheckState(value ? CheckState.Checked : CheckState.Unchecked);

  /// <inheritdoc/>
  public bool GetChecked() => this.GetCheckState() is not CheckState.Unchecked;

  /// <inheritdoc/>
  public void SetCheckState(CheckState value) {
    _state = value;
    if (Handle != 0)
      NativeMethods.SendMessageW(Handle, NativeMethods.BM_SETCHECK, ToNative(value), 0);
  }

  /// <inheritdoc/>
  public CheckState GetCheckState()
      => Handle == 0
          ? _state
          : NativeMethods.SendMessageW(Handle, NativeMethods.BM_GETCHECK, 0, 0) switch {
            NativeMethods.BST_CHECKED => CheckState.Checked,
            NativeMethods.BST_INDETERMINATE => CheckState.Indeterminate,
            _ => CheckState.Unchecked,
          };

  /// <inheritdoc/>
  public unsafe void SetThreeState(bool value) {
    if (_threeState == value)
      return;

    _threeState = value;
    if (Handle == 0)
      return; // the style is read at creation, which has not happened yet

    // Swapping the button type on a live window: clear the type bits and write the other one back.
    // The check state does not necessarily survive the swap, so it is pushed again afterwards.
    var style = (uint)NativeMethods.GetWindowLongPtrW(Handle, NativeMethods.GWL_STYLE);
    style = (style & ~NativeMethods.BS_TYPEMASK) | (value ? NativeMethods.BS_AUTO3STATE : NativeMethods.BS_AUTOCHECKBOX);
    NativeMethods.SetWindowLongPtrW(Handle, NativeMethods.GWL_STYLE, (nint)style);
    this.SetCheckState(_state);
    NativeMethods.InvalidateRect(Handle, null, true);
  }

  /// <inheritdoc/>
  internal override void CreateChildHandle(nint parent, int controlId) {
    base.CreateChildHandle(parent, controlId);
    this.SetCheckState(_state); // flush the state buffered before the window existed
  }

  /// <inheritdoc/>
  internal override void OnCommand(int notifyCode) {
    switch (notifyCode) {
      case NativeMethods.BN_CLICKED:
        _state = this.GetCheckState();
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

  /// <summary>Maps a state onto the <c>BM_SETCHECK</c> value carrying it.</summary>
  private static nint ToNative(CheckState state)
      => state switch {
        CheckState.Checked => NativeMethods.BST_CHECKED,
        CheckState.Indeterminate => NativeMethods.BST_INDETERMINATE,
        _ => NativeMethods.BST_UNCHECKED,
      };
}
