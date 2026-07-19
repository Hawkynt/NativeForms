using System.Text;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a static-text label, wrapping a <c>GtkLabel</c>. Alignment maps onto the label's
/// x/y-alignment; mnemonic text is translated from the WinForms <c>&amp;</c> convention to GTK's
/// <c>_</c> convention before it reaches the widget. GTK has no native frame on a label, so
/// <see cref="SetBorderStyle"/> is not rendered here.
/// </summary>
internal sealed class GtkLabelPeer : GtkControlPeer, ILabelPeer
{
    private ContentAlignment _textAlign;
    private bool _useMnemonic = true;

    /// <inheritdoc />
    protected override nint CreateWidget() => NativeMethods.gtk_label_new(string.Empty);

    /// <inheritdoc />
    protected override void OnWidgetRealized()
    {
        this.ApplyText(_text);
        this.ApplyAlignment();
    }

    /// <inheritdoc />
    protected override void ApplyText(string text)
    {
        if (_useMnemonic)
            NativeMethods.gtk_label_set_text_with_mnemonic(_widget, TranslateMnemonics(text));
        else
            NativeMethods.gtk_label_set_text(_widget, text);
    }

    /// <inheritdoc />
    public void SetTextAlign(ContentAlignment alignment)
    {
        if (_textAlign == alignment)
            return;

        _textAlign = alignment;
        if (_widget != 0)
            this.ApplyAlignment();
    }

    /// <inheritdoc />
    public void SetBorderStyle(BorderStyle borderStyle) { }

    /// <inheritdoc />
    public void SetUseMnemonic(bool useMnemonic)
    {
        if (_useMnemonic == useMnemonic)
            return;

        _useMnemonic = useMnemonic;
        if (_widget != 0)
            this.ApplyText(_text);
    }

    /// <summary>Pushes the buffered alignment onto the live widget as x/y fractions.</summary>
    private void ApplyAlignment()
    {
        var x = _textAlign switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => 0.5f,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => 1f,
            _ => 0f,
        };
        var y = _textAlign switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight => 0.5f,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => 1f,
            _ => 0f,
        };

        NativeMethods.gtk_label_set_xalign(_widget, x);
        NativeMethods.gtk_label_set_yalign(_widget, y);
    }

    /// <summary>
    /// Translates WinForms mnemonic text to GTK's: <c>&amp;x</c> becomes <c>_x</c>, <c>&amp;&amp;</c>
    /// a literal <c>&amp;</c>, and a literal <c>_</c> is escaped as <c>__</c>.
    /// </summary>
    private static string TranslateMnemonics(string text)
    {
        if (text.IndexOf('&') < 0 && text.IndexOf('_') < 0)
            return text;

        var result = new StringBuilder(text.Length + 4);
        for (var i = 0; i < text.Length; ++i)
        {
            var c = text[i];
            if (c == '_')
            {
                result.Append("__");
                continue;
            }

            if (c != '&')
            {
                result.Append(c);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '&')
            {
                result.Append('&');
                ++i;
                continue;
            }

            result.Append('_');
        }

        return result.ToString();
    }
}
