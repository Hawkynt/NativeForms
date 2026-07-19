using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Text;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a rich text box — a native <c>RICHEDIT50W</c> child window from msftedit.dll
/// (loaded once, on first use). It rides on the plain <see cref="TextBoxPeer"/> for the text-box
/// surface and adds selection formatting via <c>EM_SETCHARFORMAT</c>/<c>EM_SETPARAFORMAT</c>, RTF
/// via the control's native <c>EM_STREAMIN</c>/<c>EM_STREAMOUT</c>, zoom via <c>EM_SETZOOM</c> and
/// auto-detected links via <c>EM_AUTOURLDETECT</c> + <c>EN_LINK</c> through the parent's
/// <c>WM_NOTIFY</c> routing — the structured sibling of the <c>WM_COMMAND</c> path.
/// </summary>
/// <remarks>
/// Rich edits stay silent unless asked: <c>EM_SETEVENTMASK</c> arms <c>EN_CHANGE</c> (which plain
/// EDITs send for free) and <c>EN_LINK</c> on every fresh HWND. The stream callbacks are
/// <see cref="UnmanagedCallersOnlyAttribute"/> statics; the managed buffer travels through the
/// <c>EDITSTREAM</c> cookie as a <see cref="GCHandle"/> — the sanctioned static-recovery pattern,
/// no marshalled delegates.
/// </remarks>
internal sealed unsafe class RichTextBoxPeer : TextBoxPeer, IRichTextBoxPeer
{
    private static int _msfteditLoaded;

    private bool _detectUrls;
    private float _zoom = 1f;
    private string? _rtf;

    /// <inheritdoc/>
    public event EventHandler<string>? LinkClicked;

    /// <inheritdoc/>
    protected override string WindowClass => "RICHEDIT50W";

    /// <inheritdoc/>
    internal override void CreateChildHandle(nint parent, int controlId)
    {
        EnsureMsfteditLoaded();
        base.CreateChildHandle(parent, controlId);
        this.FlushRichState();
    }

    /// <inheritdoc/>
    public void SetSelectionStyle(FontStyle style, bool enabled)
    {
        if (Handle == 0)
            return;

        var mask = 0u;
        if ((style & FontStyle.Bold) != 0)
            mask |= NativeMethods.CFM_BOLD;
        if ((style & FontStyle.Italic) != 0)
            mask |= NativeMethods.CFM_ITALIC;
        if ((style & FontStyle.Underline) != 0)
            mask |= NativeMethods.CFM_UNDERLINE;
        if ((style & FontStyle.Strikeout) != 0)
            mask |= NativeMethods.CFM_STRIKEOUT;

        // The CFE_* effect bits share the CFM_* values for these four styles.
        var format = new NativeMethods.CHARFORMAT2W
        {
            cbSize = (uint)sizeof(NativeMethods.CHARFORMAT2W),
            dwMask = mask,
            dwEffects = enabled ? mask : 0,
        };
        NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETCHARFORMAT, NativeMethods.SCF_SELECTION, (nint)(&format));
    }

    /// <inheritdoc/>
    public void SetSelectionColor(Color color)
    {
        if (Handle == 0)
            return;

        var format = new NativeMethods.CHARFORMAT2W
        {
            cbSize = (uint)sizeof(NativeMethods.CHARFORMAT2W),
            dwMask = NativeMethods.CFM_COLOR,
            dwEffects = color.IsEmpty ? NativeMethods.CFE_AUTOCOLOR : 0,
            crTextColor = (uint)(color.R | color.G << 8 | color.B << 16),
        };
        NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETCHARFORMAT, NativeMethods.SCF_SELECTION, (nint)(&format));
    }

    /// <inheritdoc/>
    public void SetSelectionFontSize(float sizeInPoints)
    {
        if (Handle == 0)
            return;

        var format = new NativeMethods.CHARFORMAT2W
        {
            cbSize = (uint)sizeof(NativeMethods.CHARFORMAT2W),
            dwMask = NativeMethods.CFM_SIZE,
            yHeight = (int)MathF.Round(sizeInPoints * 20), // twips
        };
        NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETCHARFORMAT, NativeMethods.SCF_SELECTION, (nint)(&format));
    }

    /// <inheritdoc/>
    public void SetSelectionAlignment(ContentAlignment alignment)
    {
        if (Handle == 0)
            return;

        var format = new NativeMethods.PARAFORMAT2
        {
            cbSize = (uint)sizeof(NativeMethods.PARAFORMAT2),
            dwMask = NativeMethods.PFM_ALIGNMENT,
            wAlignment = alignment switch
            {
                ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => NativeMethods.PFA_CENTER,
                ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => NativeMethods.PFA_RIGHT,
                _ => NativeMethods.PFA_LEFT,
            },
        };
        NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETPARAFORMAT, 0, (nint)(&format));
    }

    /// <inheritdoc/>
    public void SetSelectionBullet(bool bullet)
    {
        if (Handle == 0)
            return;

        var format = new NativeMethods.PARAFORMAT2
        {
            cbSize = (uint)sizeof(NativeMethods.PARAFORMAT2),
            dwMask = NativeMethods.PFM_NUMBERING,
            wNumbering = bullet ? NativeMethods.PFN_BULLET : (ushort)0,
        };
        NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETPARAFORMAT, 0, (nint)(&format));
    }

    /// <inheritdoc/>
    public void SetDetectUrls(bool detectUrls)
    {
        _detectUrls = detectUrls;
        if (Handle != 0)
            NativeMethods.SendMessageW(Handle, NativeMethods.EM_AUTOURLDETECT, detectUrls ? 1 : 0, 0);
    }

    /// <inheritdoc/>
    public void SetZoom(float factor)
    {
        _zoom = factor;
        if (Handle != 0)
            NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETZOOM, (int)MathF.Round(factor * 1000), 1000);
    }

    /// <inheritdoc/>
    public string GetRtf()
    {
        if (Handle == 0)
            return _rtf ?? RtfSerializer.Write(RichDocument.FromPlainText(this.GetText()));

        var buffer = new MemoryStream();
        var cookie = GCHandle.Alloc(buffer);
        try
        {
            var stream = new NativeMethods.EDITSTREAM
            {
                dwCookie = GCHandle.ToIntPtr(cookie),
                pfnCallback = (nint)(delegate* unmanaged<nint, nint, int, int*, uint>)&StreamOutCallback,
            };
            NativeMethods.SendMessageW(Handle, NativeMethods.EM_STREAMOUT, NativeMethods.SF_RTF, (nint)(&stream));
        }
        finally
        {
            cookie.Free();
        }

        // Rich edit RTF is 7-bit ASCII with escapes; Latin-1 maps any stray high byte 1:1.
        return Encoding.Latin1.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <inheritdoc/>
    public void SetRtf(string rtf)
    {
        _rtf = rtf;
        if (Handle == 0)
            return;

        var state = new StreamInState(Encoding.Latin1.GetBytes(rtf));
        var cookie = GCHandle.Alloc(state);
        try
        {
            var stream = new NativeMethods.EDITSTREAM
            {
                dwCookie = GCHandle.ToIntPtr(cookie),
                pfnCallback = (nint)(delegate* unmanaged<nint, nint, int, int*, uint>)&StreamInCallback,
            };
            NativeMethods.SendMessageW(Handle, NativeMethods.EM_STREAMIN, NativeMethods.SF_RTF, (nint)(&stream));
        }
        finally
        {
            cookie.Free();
        }
    }

    /// <inheritdoc/>
    internal override void OnNotify(int code, nint lParam)
    {
        if (code != NativeMethods.EN_LINK)
            return;

        var link = (NativeMethods.ENLINK*)lParam;
        if (link->msg != NativeMethods.WM_LBUTTONUP)
            return;

        var length = link->chrg.cpMax - link->chrg.cpMin;
        if (length <= 0)
            return;

        var buffer = new char[length + 1];
        fixed (char* text = buffer)
        {
            var range = new NativeMethods.TEXTRANGE { chrg = link->chrg, lpstrText = (nint)text };
            var copied = (int)NativeMethods.SendMessageW(Handle, NativeMethods.EM_GETTEXTRANGE, 0, (nint)(&range));
            LinkClicked?.Invoke(this, new string(buffer, 0, Math.Clamp(copied, 0, length)));
        }
    }

    /// <summary>Pushes the rich-specific buffered state onto a freshly created HWND.</summary>
    private void FlushRichState()
    {
        if (Handle == 0)
            return;

        // Arm the notifications first: EN_CHANGE (rich edits are silent by default) and EN_LINK,
        // so the RTF streamed in below already reports its text change to the core.
        NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETEVENTMASK, 0, (nint)(NativeMethods.ENM_CHANGE | NativeMethods.ENM_LINK));
        NativeMethods.SendMessageW(Handle, NativeMethods.EM_AUTOURLDETECT, _detectUrls ? 1 : 0, 0);
        if (_zoom != 1f)
            NativeMethods.SendMessageW(Handle, NativeMethods.EM_SETZOOM, (int)MathF.Round(_zoom * 1000), 1000);

        if (_rtf is not null)
            this.SetRtf(_rtf);
    }

    /// <summary>Loads msftedit.dll (which registers the <c>RICHEDIT50W</c> class) exactly once per process.</summary>
    private static void EnsureMsfteditLoaded()
    {
        if (Interlocked.CompareExchange(ref _msfteditLoaded, 1, 0) != 0)
            return;

        NativeMethods.LoadLibraryW("msftedit.dll");
    }

    /// <summary>The read cursor for <see cref="StreamInCallback"/>.</summary>
    private sealed class StreamInState(byte[] data)
    {
        /// <summary>The RTF bytes being fed to the control.</summary>
        public byte[] Data { get; } = data;

        /// <summary>How many bytes the control has consumed so far.</summary>
        public int Position;
    }

    /// <summary>
    /// The <c>EDITSTREAMCALLBACK</c> for <c>EM_STREAMOUT</c>: appends each chunk the control hands
    /// out to the <see cref="MemoryStream"/> recovered from the cookie.
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint StreamOutCallback(nint cookie, nint buffer, int count, int* written)
    {
        if (GCHandle.FromIntPtr(cookie).Target is not MemoryStream output)
            return 1;

        output.Write(new ReadOnlySpan<byte>((void*)buffer, count));
        *written = count;
        return 0;
    }

    /// <summary>
    /// The <c>EDITSTREAMCALLBACK</c> for <c>EM_STREAMIN</c>: copies the next chunk of the buffered
    /// RTF (recovered from the cookie) into the control's buffer until the data runs out.
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint StreamInCallback(nint cookie, nint buffer, int count, int* read)
    {
        if (GCHandle.FromIntPtr(cookie).Target is not StreamInState state)
            return 1;

        var remaining = state.Data.Length - state.Position;
        var chunk = Math.Min(count, remaining);
        state.Data.AsSpan(state.Position, chunk).CopyTo(new Span<byte>((void*)buffer, count));
        state.Position += chunk;
        *read = chunk;
        return 0;
    }
}
