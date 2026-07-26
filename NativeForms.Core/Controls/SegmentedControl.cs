using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A horizontal group of mutually-exclusive toggle segments — the button-styled radio group (an
/// iOS-style segmented picker): one rounded, bordered strip split into equal cells, the selected cell
/// filled with the accent. A click or the arrow keys move the selection and raise
/// <see cref="SelectedIndexChanged"/>. Owner-drawn, so it looks native in either theme.
/// </summary>
public class SegmentedControl : OwnerDrawnControl
{
    private const int _Radius = 4;

    private string[] _segments = [];
    private int _selectedIndex = -1;

    /// <summary>Replaces the segment captions, left to right; the selection lands on the first segment.</summary>
    public void SetSegments(params string[] labels)
    {
        _segments = labels ?? [];
        _selectedIndex = _segments.Length > 0 ? 0 : -1;
        this.Invalidate();
        this.OnSelectedIndexChanged(EventArgs.Empty);
    }

    /// <summary>The segment captions, left to right.</summary>
    public IReadOnlyList<string> Segments => _segments;

    /// <summary>The selected segment, or <c>-1</c> when there are none. Setting it repaints and raises
    /// <see cref="SelectedIndexChanged"/>.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            value = _segments.Length == 0 ? -1 : Math.Clamp(value, 0, _segments.Length - 1);
            if (_selectedIndex == value)
                return;

            _selectedIndex = value;
            this.Invalidate();
            this.OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    /// <summary>The caption of the selected segment, or <see langword="null"/>.</summary>
    public string? SelectedSegment => _selectedIndex >= 0 && _selectedIndex < _segments.Length ? _segments[_selectedIndex] : null;

    /// <summary>Raised when <see cref="SelectedIndex"/> changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Raises <see cref="SelectedIndexChanged"/>.</summary>
    protected virtual void OnSelectedIndexChanged(EventArgs e) => this.SelectedIndexChanged?.Invoke(this, e);

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button == MouseButtons.Left && _segments.Length > 0)
            this.SelectedIndex = Math.Clamp(e.X * _segments.Length / Math.Max(1, this.Width), 0, _segments.Length - 1);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left when this.SelectedIndex > 0:
                this.SelectedIndex--;
                e.Handled = true;
                break;
            case Keys.Right when this.SelectedIndex < _segments.Length - 1:
                this.SelectedIndex++;
                e.Handled = true;
                break;
        }
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var font = this.Font;
        g.FillRectangle(this.BackColor, new Rectangle(0, 0, this.Width, this.Height));

        var n = _segments.Length;
        if (n == 0)
            return;

        var outer = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
        g.FillRoundedRectangle(theme.FieldBackground, outer, _Radius);

        for (var i = 0; i < n; ++i)
        {
            var left = i * this.Width / n;
            var right = i == n - 1 ? outer.Right : (i + 1) * this.Width / n;
            var cell = new Rectangle(left, outer.Y, right - left, outer.Height);
            var selected = i == this.SelectedIndex;
            if (selected)
                this.FillSegment(g, this.Enabled ? theme.Accent : theme.Border, cell, roundLeft: i == 0, roundRight: i == n - 1);

            if (i > 0)
                g.DrawLine(theme.Border, left, 1, left, this.Height - 2); // divider between segments

            var ink = !this.Enabled ? theme.DisabledText : selected ? theme.SelectionText : this.ForeColor;
            g.DrawText(_segments[i], font, ink, cell, ContentAlignment.MiddleCenter);
        }

        g.DrawRoundedRectangle(theme.Border, outer, _Radius);
        if (this.Focused)
            GlyphRenderer.DrawFocusRing(g, theme, new Rectangle(2, 2, this.Width - 5, this.Height - 5));
    }

    /// <summary>Fills a segment cell rounding only the corners that sit on the strip's outer edge, so the
    /// accent never overhangs the rounded border (a square fill would leave the border arc floating in the
    /// corner). A middle cell squares off both sides; an end cell keeps its outer corners rounded.</summary>
    private void FillSegment(IGraphics g, Color color, Rectangle cell, bool roundLeft, bool roundRight)
    {
        if (!roundLeft && !roundRight)
        {
            g.FillRectangle(color, cell);
            return;
        }

        g.FillRoundedRectangle(color, cell, _Radius);
        if (!roundLeft)
            g.FillRectangle(color, new Rectangle(cell.X, cell.Y, _Radius, cell.Height));   // square the inner (left) side

        if (!roundRight)
            g.FillRectangle(color, new Rectangle(cell.Right - _Radius, cell.Y, _Radius, cell.Height)); // square the inner (right) side
    }
}
