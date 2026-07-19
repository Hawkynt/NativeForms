using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A search field: a hosted native <see cref="TextBox"/> (placeholder "Search" by default) framed by
/// an owner-drawn surface that paints a magnifier glyph at the left and, while text is present, a
/// clear (×) zone at the right — clicking it empties the box and raises <see cref="SearchCleared"/>.
/// Built like <see cref="UpDownBase"/>: the native editor fills the field so caret, selection,
/// clipboard and IME stay platform-native.
/// </summary>
/// <remarks>
/// <see cref="SearchCommitted"/> fires for Enter on the owner-drawn surface. Enter typed inside the
/// hosted native editor is not observable from the core — <see cref="ITextBoxPeer"/> exposes text
/// changes but no key events — so committing from within the editor needs a key seam on the peer
/// first, like every native-widget control (tracked in <c>docs/PRD.md</c>).
/// </remarks>
public class SearchBox : OwnerDrawnControl
{
    /// <summary>The width of the leading zone carrying the magnifier glyph.</summary>
    internal const int GlyphZoneWidth = 20;

    /// <summary>The width of the trailing zone carrying the clear (×) glyph.</summary>
    internal const int ClearZoneWidth = 20;

    /// <summary>The stroke half-length of the × glyph.</summary>
    private const int _ClearArm = 3;

    private readonly TextBox _editor;

    /// <summary>Creates the search field and its hosted editor.</summary>
    public SearchBox()
    {
        _editor = new() { PlaceholderText = "Search" };
        _editor.TextChanged += (_, _) => this.OnTextChanged(EventArgs.Empty);
        this.Controls.Add(_editor);
    }

    /// <summary>The search text, forwarded to the hosted editor.</summary>
    public override string Text
    {
        get => _editor.Text;
        set => _editor.Text = value;
    }

    /// <summary>The greyed hint shown while the box is empty. Defaults to "Search".</summary>
    public string PlaceholderText
    {
        get => _editor.PlaceholderText;
        set => _editor.PlaceholderText = value;
    }

    /// <summary>Raised after the clear (×) zone emptied the box.</summary>
    public event EventHandler? SearchCleared;

    /// <summary>Raised when Enter commits the search (see the class remarks for the reach).</summary>
    public event EventHandler? SearchCommitted;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Raises <see cref="SearchCleared"/>.</summary>
    protected virtual void OnSearchCleared(EventArgs e) => this.SearchCleared?.Invoke(this, e);

    /// <summary>Raises <see cref="SearchCommitted"/>.</summary>
    protected virtual void OnSearchCommitted(EventArgs e) => this.SearchCommitted?.Invoke(this, e);

    /// <inheritdoc/>
    private protected override void OnRealized(IControlPeer peer)
    {
        base.OnRealized(peer);
        _editor.Bounds = new(GlyphZoneWidth, 0, Math.Max(0, this.Width - GlyphZoneWidth - ClearZoneWidth), this.Height);
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var width = this.Width;
        var height = this.Height;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, width, height));

        // The magnifier: a stroked lens circle with a short handle toward the lower right.
        var glyphColor = this.Enabled ? theme.ControlText : theme.DisabledText;
        var mid = height / 2;
        g.DrawEllipse(glyphColor, new(5, mid - 6, 8, 8));
        g.DrawLine(glyphColor, 12, mid + 1, 15, mid + 4);

        // The clear ×: two crossing strokes, only while there is something to clear.
        if (this.Text.Length > 0)
        {
            var cx = width - (ClearZoneWidth / 2);
            g.DrawLine(glyphColor, cx - _ClearArm, mid - _ClearArm, cx + _ClearArm, mid + _ClearArm);
            g.DrawLine(glyphColor, cx - _ClearArm, mid + _ClearArm, cx + _ClearArm, mid - _ClearArm);
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, width - 1, height - 1));
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!this.Enabled || e.Button != MouseButtons.Left)
            return;

        if (e.X < this.Width - ClearZoneWidth || this.Text.Length == 0)
            return;

        this.Text = string.Empty;
        this.OnSearchCleared(EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!this.Enabled || e.KeyCode is not Keys.Enter)
            return;

        this.OnSearchCommitted(EventArgs.Empty);
        e.Handled = true;
    }
}
