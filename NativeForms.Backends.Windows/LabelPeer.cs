using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a label — a native <c>STATIC</c> text window. Alignment, border and mnemonic
/// handling are style bits fixed at window creation, so the peer buffers them and — matching the
/// buffered-then-flushed pattern — recreates the HWND in place when one changes after realization.
/// Win32 statics honor the horizontal alignment plus a coarse vertical centering
/// (<c>SS_CENTERIMAGE</c>, single-line) only.
/// </summary>
internal sealed class LabelPeer : Win32ChildPeer, ILabelPeer
{
    private ContentAlignment _textAlign;
    private BorderStyle _borderStyle;
    private bool _useMnemonic = true;
    private nint _parent;
    private int _controlId;

    /// <inheritdoc/>
    protected override string WindowClass => "STATIC";

    /// <inheritdoc/>
    protected override uint ExtraStyle
    {
        get
        {
            var style = _textAlign switch
            {
                ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter
                    => NativeMethods.SS_CENTER,
                ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
                    => NativeMethods.SS_RIGHT,
                _ => NativeMethods.SS_LEFT,
            };

            if (_textAlign is ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight)
                style |= NativeMethods.SS_CENTERIMAGE;

            if (_borderStyle != BorderStyle.None)
                style |= NativeMethods.WS_BORDER;

            if (!_useMnemonic)
                style |= NativeMethods.SS_NOPREFIX;

            return style;
        }
    }

    /// <inheritdoc/>
    internal override void CreateChildHandle(nint parent, int controlId)
    {
        _parent = parent;
        _controlId = controlId;
        base.CreateChildHandle(parent, controlId);
    }

    /// <inheritdoc/>
    public void SetTextAlign(ContentAlignment alignment)
    {
        if (_textAlign == alignment)
            return;

        _textAlign = alignment;
        this.RecreateHandle();
    }

    /// <inheritdoc/>
    public void SetBorderStyle(BorderStyle borderStyle)
    {
        if (_borderStyle == borderStyle)
            return;

        _borderStyle = borderStyle;
        this.RecreateHandle();
    }

    /// <inheritdoc/>
    public void SetUseMnemonic(bool useMnemonic)
    {
        if (_useMnemonic == useMnemonic)
            return;

        _useMnemonic = useMnemonic;
        this.RecreateHandle();
    }

    /// <summary>Rebuilds the HWND with the current style bits; buffered state is re-flushed by creation.</summary>
    private void RecreateHandle()
    {
        if (Handle == 0)
            return;

        NativeMethods.DestroyWindow(Handle);
        Handle = 0;
        this.CreateChildHandle(_parent, _controlId);
    }
}
