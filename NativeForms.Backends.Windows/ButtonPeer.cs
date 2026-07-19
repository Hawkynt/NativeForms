using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a push button — a native <c>BUTTON</c> window with <c>BS_PUSHBUTTON</c>. An
/// image is attached via <c>BM_SETIMAGE</c>; while the caption is empty the button renders the bitmap
/// alone (<c>BS_BITMAP</c>, chosen from the state at handle creation). A classic (non-visual-styles)
/// BUTTON cannot render image and text together — themed common controls draw both, classic rendering
/// keeps the text — and it offers no image placement, so alignment and relation are not rendered.
/// </summary>
internal sealed class ButtonPeer : Win32ChildPeer, IButtonPeer
{
    private Win32Image? _image;
    private nint _parent;
    private int _controlId;

    /// <inheritdoc/>
    protected override string WindowClass => "BUTTON";

    /// <inheritdoc/>
    protected override uint ExtraStyle
    {
        get
        {
            var style = NativeMethods.BS_PUSHBUTTON | NativeMethods.WS_TABSTOP;
            if (_image is not null && _text.Length == 0)
                style |= NativeMethods.BS_BITMAP;

            return style;
        }
    }

    /// <inheritdoc/>
    public event EventHandler? Clicked;

    /// <inheritdoc/>
    internal override void CreateChildHandle(nint parent, int controlId)
    {
        _parent = parent;
        _controlId = controlId;
        base.CreateChildHandle(parent, controlId);

        if (_image is { Handle: not 0 } image)
            NativeMethods.SendMessageW(Handle, NativeMethods.BM_SETIMAGE, NativeMethods.IMAGE_BITMAP, image.Handle);
    }

    /// <inheritdoc/>
    public void SetImage(IImage? image, ContentAlignment imageAlign, TextImageRelation relation)
    {
        var native = image as Win32Image;
        if (ReferenceEquals(_image, native))
            return;

        _image = native;
        this.RecreateHandle();
    }

    /// <inheritdoc/>
    internal override void OnCommand(int notifyCode)
    {
        if (notifyCode == NativeMethods.BN_CLICKED)
            Clicked?.Invoke(this, EventArgs.Empty);
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
