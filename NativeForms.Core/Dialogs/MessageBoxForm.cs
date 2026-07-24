using System.Drawing;
using System.Text;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// The owner-drawn message box the framework falls back to when the native dialog cannot express the
/// request — a custom icon image (which may be an <see cref="AnimatedImage"/>) or arbitrary button
/// labels. It lays an icon column, a wrapped text block and a right-aligned button row inside a
/// fixed-size dialog frame; each button records its index and closes the dialog.
/// </summary>
internal sealed class MessageBoxForm : Form
{
    private const int _Pad = 16;
    private const int _Gap = 12;
    private const int _IconSize = 32;
    private const int _ButtonHeight = 26;
    private const int _ButtonGap = 8;
    private const int _MinButtonWidth = 88;
    private const int _ButtonTextPad = 40; // the native button's own frame + padding around the caption
    private const int _MaxTextWidth = 380;

    private readonly Button[] _buttons;

    /// <summary>The buttons in order, so a test can drive a click without the modal loop.</summary>
    internal IReadOnlyList<Button> Buttons => _buttons;

    /// <summary>The index of the button the user pressed, or -1 if the dialog was closed another way.</summary>
    internal int ClickedIndex { get; private set; } = -1;

    internal MessageBoxForm(
        IPlatformBackend backend,
        string text,
        string caption,
        IImage? iconImage,
        MessageBoxIcon standardIcon,
        IReadOnlyList<string> labels,
        int defaultIndex,
        int cancelIndex)
    {
        this.Text = caption;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MinimizeBox = false;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;

        var theme = backend.Theme;
        var font = theme.DefaultFont;
        var lineHeight = Math.Max(backend.MeasureText("Ag", font).Height, theme.RowHeight - 4);

        var hasIcon = iconImage is not null || standardIcon != MessageBoxIcon.None;
        var iconColumn = hasIcon ? _IconSize + _Gap : 0;

        var wrapped = Wrap(backend, text ?? string.Empty, font, _MaxTextWidth);
        var textWidth = 0;
        foreach (var line in wrapped)
            textWidth = Math.Max(textWidth, backend.MeasureText(line, font).Width);
        var textHeight = Math.Max(wrapped.Count * lineHeight, hasIcon ? _IconSize : lineHeight);

        // Button strip: one uniform width, wide enough for the longest caption, WinForms-style.
        _buttons = new Button[labels.Count];
        var buttonWidth = _MinButtonWidth;
        foreach (var label in labels)
            buttonWidth = Math.Max(buttonWidth, backend.MeasureText(label, font).Width + _ButtonTextPad);
        var buttonsWidth = (labels.Count * buttonWidth) + ((labels.Count - 1) * _ButtonGap);

        var contentWidth = iconColumn + textWidth;
        var clientWidth = (2 * _Pad) + Math.Max(contentWidth, buttonsWidth);
        var clientHeight = (2 * _Pad) + textHeight + _Gap + _ButtonHeight;
        this.ClientSize = new(clientWidth, clientHeight);

        // Icon column: a PictureBox for a custom image (still or animated), or a themed severity glyph.
        if (iconImage is not null)
            this.Controls.Add(new PictureBox
            {
                Bounds = new(_Pad, _Pad, _IconSize, _IconSize),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = iconImage,
            });
        else if (standardIcon != MessageBoxIcon.None)
            this.Controls.Add(new MessageBoxIconGlyph(standardIcon) { Bounds = new(_Pad, _Pad, _IconSize, _IconSize) });

        this.Controls.Add(new Label
        {
            Bounds = new(_Pad + iconColumn, _Pad, textWidth, textHeight),
            Text = string.Join('\n', wrapped),
        });

        // Button row, right-aligned along the bottom, in declaration order.
        var x = clientWidth - _Pad - buttonsWidth;
        var y = clientHeight - _Pad - _ButtonHeight;
        for (var i = 0; i < labels.Count; ++i)
        {
            var index = i;
            var button = new Button { Bounds = new(x, y, buttonWidth, _ButtonHeight), Text = labels[i] };
            button.Click += (_, _) =>
            {
                this.ClickedIndex = index;
                this.Close();
            };
            _buttons[i] = button;
            this.Controls.Add(button);
            x += buttonWidth + _ButtonGap;
        }

        if (defaultIndex >= 0 && defaultIndex < _buttons.Length)
            this.AcceptButton = _buttons[defaultIndex];
        if (cancelIndex >= 0 && cancelIndex < _buttons.Length)
            this.CancelButton = _buttons[cancelIndex];
    }

    /// <summary>Greedily word-wraps <paramref name="text"/> to <paramref name="maxWidth"/>, honoring any
    /// explicit newlines, so a long message grows in height rather than off the screen's edge.</summary>
    private static List<string> Wrap(IPlatformBackend backend, string text, Font font, int maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var current = new StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && backend.MeasureText(candidate, font).Width > maxWidth)
                {
                    lines.Add(current.ToString());
                    current.Clear().Append(word);
                }
                else
                {
                    current.Clear().Append(candidate);
                }
            }

            lines.Add(current.ToString());
        }

        return lines;
    }
}

/// <summary>A themed severity glyph — a filled disc carrying the ×/!/i/? symbol — drawn for a standard
/// <see cref="MessageBoxIcon"/> in the owner-drawn message box.</summary>
internal sealed class MessageBoxIconGlyph(MessageBoxIcon icon) : OwnerDrawnControl
{
    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var (fill, symbol) = icon switch
        {
            MessageBoxIcon.Error => (Color.FromArgb(0xFF, 0xD1, 0x34, 0x38), "×"),
            MessageBoxIcon.Warning => (Color.FromArgb(0xFF, 0xE8, 0xA0, 0x0B), "!"),
            MessageBoxIcon.Question => (Color.FromArgb(0xFF, 0x2A, 0x7A, 0xD4), "?"),
            _ => (Color.FromArgb(0xFF, 0x2A, 0x7A, 0xD4), "i"),
        };

        var side = Math.Min(this.Width, this.Height);
        var disc = new Rectangle((this.Width - side) / 2, (this.Height - side) / 2, side, side);
        g.FillEllipse(fill, disc);
        g.DrawText(symbol, this.Theme.DefaultFont, Color.White, disc, ContentAlignment.MiddleCenter);
    }
}
