using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>The Win32 peer for a push button — a native <c>BUTTON</c> window with <c>BS_PUSHBUTTON</c>.</summary>
internal sealed class ButtonPeer : Win32ChildPeer, IButtonPeer
{
    /// <inheritdoc/>
    protected override string WindowClass => "BUTTON";

    /// <inheritdoc/>
    protected override uint ExtraStyle => NativeMethods.BS_PUSHBUTTON | NativeMethods.WS_TABSTOP;

    /// <inheritdoc/>
    public event EventHandler? Clicked;

    /// <inheritdoc/>
    internal override void OnCommand(int notifyCode)
    {
        if (notifyCode == NativeMethods.BN_CLICKED)
            Clicked?.Invoke(this, EventArgs.Empty);
    }
}
