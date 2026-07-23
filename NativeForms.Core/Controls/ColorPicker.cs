using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A colour swatch that drops down a palette to pick from — the toolbar/ribbon/dialog colour chooser.
/// The face shows the current <see cref="SelectedColor"/> and a chevron; a click opens a light-dismiss
/// grid of standard colours, and choosing one sets the colour and raises <see cref="SelectedColorChanged"/>.
/// </summary>
public class ColorPicker : OwnerDrawnControl
{
    private const int _ArrowZone = 16;
    private const int _SwatchInset = 3;
    private const int _Columns = 8;
    private const int _CellSize = 18;

    private static readonly Color[] _Palette = BuildPalette();

    private IPopupPeer? _popup;
    private int _hotCell = -1;

    /// <summary>The chosen colour. Setting it repaints and raises <see cref="SelectedColorChanged"/>.</summary>
    public Color SelectedColor
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
            this.OnSelectedColorChanged(EventArgs.Empty);
        }
    } = Color.Black;

    /// <summary>The standard colours the drop-down offers, left to right, top to bottom.</summary>
    public static IReadOnlyList<Color> Palette => _Palette;

    /// <summary>Raised when <see cref="SelectedColor"/> changes.</summary>
    public event EventHandler? SelectedColorChanged;

    /// <summary>Whether the palette drop-down is currently open.</summary>
    public bool DroppedDown => this.OwnsOpenPopup;

    /// <summary>Raises <see cref="SelectedColorChanged"/>.</summary>
    protected virtual void OnSelectedColorChanged(EventArgs e) => this.SelectedColorChanged?.Invoke(this, e);

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Opens the palette drop-down below the swatch.</summary>
    public void OpenDropDown()
    {
        if (this.OwnsOpenPopup || this.Backend is not { } backend)
            return;

        var popup = _popup ??= this.CreatePopup(backend);
        this.OwnsOpenPopup = true;
        popup.ShowAt(this.PointToScreen(new Point(0, this.Height)), this.PopupSize());
        this.Invalidate();
    }

    /// <summary>Closes the palette drop-down.</summary>
    public void CloseDropDown()
    {
        if (!this.OwnsOpenPopup)
            return;

        this.OwnsOpenPopup = false;
        _popup?.Hide();
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        if (this.OwnsOpenPopup)
            this.CloseDropDown();
        else
            this.OpenDropDown();
    }

    /// <summary>Space/Enter opens the drop-down, so the field is keyboard-reachable.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            this.OpenDropDown();
            e.Handled = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, this.Width, this.Height));

        var swatch = new Rectangle(_SwatchInset, _SwatchInset, Math.Max(0, this.Width - _ArrowZone - (2 * _SwatchInset)), this.Height - (2 * _SwatchInset));
        g.FillRectangle(this.Enabled ? this.SelectedColor : theme.DisabledText, swatch);
        g.DrawRectangle(theme.Border, new Rectangle(swatch.X, swatch.Y, swatch.Width - 1, swatch.Height - 1));

        Glyphs.PaintTriangle(g, this.Enabled ? theme.ControlText : theme.DisabledText, new Rectangle(this.Width - _ArrowZone + 4, (this.Height / 2) - 2, 8, 5), GlyphDirection.Down);
        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
        if (this.Focused)
            GlyphRenderer.DrawFocusRing(g, theme, new Rectangle(2, 2, this.Width - 5, this.Height - 5));
    }

    private Size PopupSize()
    {
        var rows = (_Palette.Length + _Columns - 1) / _Columns;
        return new((_Columns * _CellSize) + 2, (rows * _CellSize) + 2);
    }

    private IPopupPeer CreatePopup(IPlatformBackend backend)
    {
        var popup = backend.CreatePopup(this.OwnerWindowPeer);
        popup.Paint += (_, e) => this.PaintPalette(e.Graphics);
        popup.MouseMove += (_, e) => this.OnPaletteMove(e);
        popup.MouseDown += (_, e) => this.OnPaletteDown(e);
        popup.Dismissed += (_, _) => this.CloseDropDown();
        return popup;
    }

    private void PaintPalette(IGraphics g)
    {
        var theme = this.Theme;
        var size = this.PopupSize();
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, size.Width, size.Height));
        for (var i = 0; i < _Palette.Length; ++i)
        {
            var cell = CellRect(i);
            g.FillRectangle(_Palette[i], cell);
            if (i == _hotCell)
                g.DrawRectangle(theme.Accent, new Rectangle(cell.X, cell.Y, cell.Width - 1, cell.Height - 1), 2);
            else
                g.DrawRectangle(theme.Border, new Rectangle(cell.X, cell.Y, cell.Width - 1, cell.Height - 1));
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, size.Width - 1, size.Height - 1));
    }

    private static Rectangle CellRect(int index)
        => new(1 + ((index % _Columns) * _CellSize), 1 + ((index / _Columns) * _CellSize), _CellSize, _CellSize);

    private static int CellAt(int x, int y)
    {
        for (var i = 0; i < _Palette.Length; ++i)
            if (CellRect(i).Contains(x, y))
                return i;

        return -1;
    }

    private void OnPaletteMove(MouseEventArgs e)
    {
        var cell = CellAt(e.X, e.Y);
        if (cell == _hotCell)
            return;

        _hotCell = cell;
        _popup?.InvalidateAll();
    }

    private void OnPaletteDown(MouseEventArgs e)
    {
        var cell = CellAt(e.X, e.Y);
        if (cell < 0)
            return;

        this.SelectedColor = _Palette[cell];
        this.CloseDropDown();
    }

    private static Color[] BuildPalette() =>
    [
        Color.Black, Color.FromArgb(64, 64, 64), Color.Gray, Color.FromArgb(128, 128, 128), Color.Silver, Color.FromArgb(192, 192, 192), Color.FromArgb(224, 224, 224), Color.White,
        Color.DarkRed, Color.Red, Color.OrangeRed, Color.Orange, Color.Gold, Color.Yellow, Color.YellowGreen, Color.GreenYellow,
        Color.DarkGreen, Color.Green, Color.SeaGreen, Color.LimeGreen, Color.Teal, Color.Cyan, Color.Turquoise, Color.Aquamarine,
        Color.Navy, Color.Blue, Color.RoyalBlue, Color.DodgerBlue, Color.SteelBlue, Color.SkyBlue, Color.SlateBlue, Color.MediumPurple,
        Color.Indigo, Color.Purple, Color.DarkViolet, Color.Magenta, Color.Orchid, Color.HotPink, Color.Pink, Color.SaddleBrown,
    ];
}
