using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A collapsible container (§7.9): a themed header row — triangle glyph plus <see cref="Control.Text"/> —
/// over a content area of ordinary child controls. Collapsing shrinks the control to the header row,
/// remembers the expanded height and hides the child peers; the children's logical
/// <see cref="Control.Visible"/> stays untouched, so expanding restores exactly what was there.
/// A header click or the Space key toggles.
/// </summary>
public class Expander : OwnerDrawnControl
{
    private const int _GlyphSize = 8;
    private const int _GlyphInset = 6;
    private const int _TextGap = 6;

    private int _expandedHeight;

    /// <summary>Whether the content area is shown. Defaults to <see langword="true"/>.</summary>
    public bool Expanded
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;

            // Height first, visibility second: the children's peers are moved into their restored
            // places before they are shown again, so expanding never flashes them at the collapsed
            // geometry.
            if (value)
                this.Height = Math.Max(_expandedHeight, this.HeaderHeight);
            else
            {
                _expandedHeight = this.Height;
                this.Height = this.HeaderHeight;
            }

            // The whole subtree, not just the direct children: a grandchild's peer is vetoed by its
            // own parent, and nothing else recomputes that when the expander reopens.
            if (this.ChildrenOrNull is { } children)
                for (var i = 0; i < children.Count; ++i)
                    children[i].PushPeerVisibleTree();

            this.Invalidate();
            this.OnExpandedChanged(EventArgs.Empty);
        }
    } = true;

    /// <summary>Raised after <see cref="Expanded"/> changes.</summary>
    public event EventHandler? ExpandedChanged;

    /// <summary>The pixel height of the header row — the whole control while collapsed.</summary>
    public int HeaderHeight => this.Theme.RowHeight;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Raises <see cref="ExpandedChanged"/>.</summary>
    protected virtual void OnExpandedChanged(EventArgs e) => this.ExpandedChanged?.Invoke(this, e);

    /// <summary>
    /// A collapsed expander hides its content wholesale. The child's <em>own</em> flag is what the
    /// veto combines with, never the effective <see cref="Control.Visible"/>: that getter walks the
    /// ancestor chain, so folding it in here would let a hidden ancestor latch a child's peer off
    /// and leave it off once the ancestor came back — the chain is the native nesting's job, not
    /// this veto's.
    /// </summary>
    private protected override bool GetChildPeerVisible(Control child) => this.Expanded && child.IsVisibleLocal;

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e) => this.Focus();

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && e.Y < this.HeaderHeight)
            this.Expanded = !this.Expanded;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Space)
            return;

        this.Expanded = !this.Expanded;
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var headerHeight = this.HeaderHeight;
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));
        g.FillRectangle(theme.HeaderBackground, new Rectangle(0, 0, this.Width, headerHeight));
        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));

        var glyphTop = (headerHeight - _GlyphSize) / 2;
        Glyphs.PaintTriangle(
            g,
            theme.ControlText,
            new Rectangle(_GlyphInset, glyphTop, _GlyphSize, _GlyphSize),
            this.Expanded ? GlyphDirection.Down : GlyphDirection.Right);

        var textLeft = _GlyphInset + _GlyphSize + _TextGap;
        var textRect = new Rectangle(textLeft, 0, Math.Max(0, this.Width - textLeft), headerHeight);
        g.DrawText(this.Text, theme.DefaultFont, theme.ControlText, textRect, ContentAlignment.MiddleLeft);
    }
}
