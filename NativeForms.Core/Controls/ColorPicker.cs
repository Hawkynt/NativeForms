using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A colour swatch that drops down a full mixer: a saturation/value square, a hue bar and an alpha bar,
/// a hex field, new-versus-current preview and basic/custom swatch palettes — the toolbar/ribbon/dialog
/// colour chooser. The face shows the current <see cref="SelectedColor"/> and a chevron; a click opens the
/// light-dismiss mixer, and every edit sets the colour and raises <see cref="SelectedColorChanged"/>.
/// </summary>
public class ColorPicker : OwnerDrawnControl
{
    private const int _ArrowZone = 16;
    private const int _SwatchInset = 3;

    // Mixer layout (a fixed-size popup, laid out in pixels).
    private const int _Pad = 8;
    private const int _SvW = 180, _SvH = 160;
    private const int _BarW = 18, _BarGap = 6;
    private const int _PreviewW = 46;
    private const int _Cell = 16, _BasicCols = 8, _BasicRows = 5;
    private const int _CustomCols = 8, _CustomRows = 2;

    private static readonly Color[] _Basic = BuildPalette();

    private IPopupPeer? _popup;
    private Mixer? _mixer;
    private Color[]? _custom;

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

    /// <summary>Whether the mixer offers an alpha bar and the face shows a transparency checkerboard behind
    /// a translucent colour. Defaults to <see langword="false"/> (opaque colours only).</summary>
    public bool AlphaEnabled { get; set; }

    /// <summary>The standard colours the mixer offers, left to right, top to bottom.</summary>
    public static IReadOnlyList<Color> Palette => _Basic;

    /// <summary>The user-saved custom colours (empty until <see cref="AddCustomColor"/> is called). At most
    /// sixteen are kept, oldest dropped first.</summary>
    public IReadOnlyList<Color> CustomColors => (IReadOnlyList<Color>?)_custom ?? [];

    /// <summary>Adds a colour to the custom palette (a ring buffer of sixteen), for the mixer's own slots.</summary>
    public void AddCustomColor(Color color)
    {
        _custom ??= [];
        if (Array.IndexOf(_custom, color) >= 0)
            return;

        if (_custom.Length >= _CustomCols * _CustomRows)
            Array.Copy(_custom, 1, _custom, 0, _custom.Length - 1); // drop the oldest
        else
            Array.Resize(ref _custom, _custom.Length + 1);

        _custom[^1] = color;
        _popup?.InvalidateAll();
    }

    /// <summary>Raised when <see cref="SelectedColor"/> changes.</summary>
    public event EventHandler? SelectedColorChanged;

    /// <summary>Whether the mixer drop-down is currently open.</summary>
    public bool DroppedDown => this.OwnsOpenPopup;

    /// <summary>Raises <see cref="SelectedColorChanged"/>.</summary>
    protected virtual void OnSelectedColorChanged(EventArgs e) => this.SelectedColorChanged?.Invoke(this, e);

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Opens the mixer drop-down below the swatch.</summary>
    public void OpenDropDown()
    {
        if (this.OwnsOpenPopup || this.Backend is not { } backend)
            return;

        var popup = _popup ??= this.CreatePopup(backend);
        _mixer = new Mixer(this.SelectedColor);
        this.OwnsOpenPopup = true;
        popup.ShowAt(this.PointToScreen(new Point(0, this.Height)), PopupSize);
        this.Invalidate();
    }

    /// <summary>Closes the mixer drop-down.</summary>
    public void CloseDropDown()
    {
        if (!this.OwnsOpenPopup)
            return;

        this.OwnsOpenPopup = false;
        _mixer = null;
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
        if (this.Enabled && this.AlphaEnabled && this.SelectedColor.A < 255)
            PaintChecker(g, swatch);
        g.FillRectangle(this.Enabled ? this.SelectedColor : theme.DisabledText, swatch);
        g.DrawRectangle(theme.Border, new Rectangle(swatch.X, swatch.Y, swatch.Width - 1, swatch.Height - 1));

        Glyphs.PaintTriangle(g, this.Enabled ? theme.ControlText : theme.DisabledText, new Rectangle(this.Width - _ArrowZone + 4, (this.Height / 2) - 2, 8, 5), GlyphDirection.Down);
        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
        if (this.Focused)
            GlyphRenderer.DrawFocusRing(g, theme, new Rectangle(2, 2, this.Width - 5, this.Height - 5));
    }

    // --- Layout ------------------------------------------------------------------------------------

    private bool HasAlpha => this.AlphaEnabled;
    private Rectangle SvRect => new(_Pad, _Pad, _SvW, _SvH);
    private Rectangle HueRect => new(_Pad + _SvW + _BarGap, _Pad, _BarW, _SvH);
    private Rectangle AlphaRect => new(this.HueRect.Right + _BarGap, _Pad, _BarW, _SvH);
    private int RightColumnX => (this.HasAlpha ? this.AlphaRect.Right : this.HueRect.Right) + _BarGap;
    private Rectangle NewRect => new(this.RightColumnX, _Pad, _PreviewW, _SvH / 2);
    private Rectangle CurrentRect => new(this.RightColumnX, _Pad + (_SvH / 2), _PreviewW, _SvH / 2);
    private int HexY => _Pad + _SvH + 8;
    private int BasicY => this.HexY + this.Theme.RowHeight + 6;
    private int CustomY => this.BasicY + (_BasicRows * _Cell) + this.Theme.RowHeight + 4;

    private Size PopupSize => new(this.RightColumnX + _PreviewW + _Pad, this.CustomY + (_CustomRows * _Cell) + _Pad);

    // --- Popup wiring ------------------------------------------------------------------------------

    private IPopupPeer CreatePopup(IPlatformBackend backend)
    {
        var popup = backend.CreatePopup(this.OwnerWindowPeer);
        popup.Paint += (_, e) => this.PaintMixer(e.Graphics);
        popup.MouseDown += (_, e) => this.OnMixerDown(e);
        popup.MouseMove += (_, e) => this.OnMixerMove(e);
        popup.MouseUp += (_, _) => { if (_mixer is { } m) m.Drag = DragTarget.None; };
        popup.Dismissed += (_, _) => this.CloseDropDown();
        return popup;
    }

    private void PaintMixer(IGraphics g)
    {
        if (_mixer is not { } mixer || this.Backend is not { } backend)
            return;

        var theme = this.Theme;
        var size = this.PopupSize;
        g.FillRectangle(theme.ControlBackground, new Rectangle(0, 0, size.Width, size.Height));

        // The saturation/value square for the current hue, the hue ramp and (optionally) the alpha ramp,
        // each a cached ARGB bitmap blitted through DrawImage — IGraphics has no gradient primitive.
        g.DrawImage(mixer.SvImage(backend, this.SvRect.Size), this.SvRect);
        g.DrawRectangle(theme.Border, Border(this.SvRect));
        this.PaintReticle(g, this.SvRect, new Point(
            this.SvRect.X + (int)(mixer.S * (this.SvRect.Width - 1)),
            this.SvRect.Y + (int)((1 - mixer.V) * (this.SvRect.Height - 1))));

        g.DrawImage(mixer.HueImage(backend, this.HueRect.Size), this.HueRect);
        g.DrawRectangle(theme.Border, Border(this.HueRect));
        this.PaintBarMarker(g, this.HueRect, mixer.H / 360.0);

        if (this.HasAlpha)
        {
            PaintChecker(g, this.AlphaRect);
            g.DrawImage(mixer.AlphaImage(backend, this.AlphaRect.Size), this.AlphaRect);
            g.DrawRectangle(theme.Border, Border(this.AlphaRect));
            this.PaintBarMarker(g, this.AlphaRect, 1 - (mixer.A / 255.0));
        }

        // New (top) over Current (bottom), each over a checker so translucency reads.
        PaintChecker(g, this.NewRect);
        g.FillRectangle(mixer.Color, this.NewRect);
        g.DrawRectangle(theme.Border, Border(this.NewRect));
        PaintChecker(g, this.CurrentRect);
        g.FillRectangle(mixer.Old, this.CurrentRect);
        g.DrawRectangle(theme.Border, Border(this.CurrentRect));

        g.DrawText(ColorMath.ToHex(mixer.Color, this.HasAlpha), theme.DefaultFont, theme.ControlText,
            new Rectangle(_Pad, this.HexY, size.Width - (2 * _Pad), theme.RowHeight), ContentAlignment.MiddleLeft);

        this.PaintSwatches(g, _Basic, _BasicCols, _BasicRows, this.BasicY);
        this.PaintSwatches(g, _custom, _CustomCols, _CustomRows, this.CustomY);

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, size.Width - 1, size.Height - 1));
    }

    private void PaintSwatches(IGraphics g, Color[]? colors, int cols, int rows, int top)
    {
        var theme = this.Theme;
        for (var i = 0; i < cols * rows; ++i)
        {
            var cell = new Rectangle(_Pad + ((i % cols) * _Cell), top + ((i / cols) * _Cell), _Cell, _Cell);
            if (colors is not null && i < colors.Length)
            {
                g.FillRectangle(colors[i], cell);
                g.DrawRectangle(theme.Border, Border(cell));
            }
            else
            {
                PaintChecker(g, cell); // an empty custom slot reads as a hole, not a black swatch
                g.DrawRectangle(theme.Border, Border(cell));
            }
        }
    }

    private void PaintReticle(IGraphics g, Rectangle area, Point at)
    {
        var x = Math.Clamp(at.X, area.X, area.Right - 1);
        var y = Math.Clamp(at.Y, area.Y, area.Bottom - 1);
        g.DrawEllipse(Color.White, new Rectangle(x - 4, y - 4, 8, 8));
        g.DrawEllipse(Color.Black, new Rectangle(x - 3, y - 3, 6, 6));
    }

    private void PaintBarMarker(IGraphics g, Rectangle bar, double t)
    {
        var y = bar.Y + (int)(Math.Clamp(t, 0, 1) * (bar.Height - 1));
        g.DrawLine(Color.White, bar.X, y, bar.Right - 1, y, 2);
        g.DrawLine(Color.Black, bar.X, y - 1, bar.Right - 1, y - 1);
    }

    // --- Mixer input -------------------------------------------------------------------------------

    private void OnMixerDown(MouseEventArgs e)
    {
        if (_mixer is not { } mixer)
            return;

        if (this.SvRect.Contains(e.X, e.Y))
        {
            mixer.Drag = DragTarget.Sv;
            this.UpdateSv(mixer, e);
            return;
        }

        if (this.HueRect.Contains(e.X, e.Y))
        {
            mixer.Drag = DragTarget.Hue;
            this.UpdateHue(mixer, e);
            return;
        }

        if (this.HasAlpha && this.AlphaRect.Contains(e.X, e.Y))
        {
            mixer.Drag = DragTarget.Alpha;
            this.UpdateAlpha(mixer, e);
            return;
        }

        if (this.SwatchAt(_Basic, _BasicCols, _BasicRows, this.BasicY, e, out var basic))
        {
            this.Commit(mixer, basic);
            return;
        }

        if (this.SwatchAt(_custom, _CustomCols, _CustomRows, this.CustomY, e, out var custom))
            this.Commit(mixer, custom);
    }

    private void OnMixerMove(MouseEventArgs e)
    {
        if (_mixer is not { } mixer)
            return;

        switch (mixer.Drag)
        {
            case DragTarget.Sv: this.UpdateSv(mixer, e); break;
            case DragTarget.Hue: this.UpdateHue(mixer, e); break;
            case DragTarget.Alpha: this.UpdateAlpha(mixer, e); break;
        }
    }

    private void UpdateSv(Mixer mixer, MouseEventArgs e)
    {
        mixer.S = Math.Clamp((e.X - this.SvRect.X) / (double)(this.SvRect.Width - 1), 0, 1);
        mixer.V = 1 - Math.Clamp((e.Y - this.SvRect.Y) / (double)(this.SvRect.Height - 1), 0, 1);
        this.Apply(mixer);
    }

    private void UpdateHue(Mixer mixer, MouseEventArgs e)
    {
        mixer.H = Math.Clamp((e.Y - this.HueRect.Y) / (double)(this.HueRect.Height - 1), 0, 1) * 360;
        mixer.InvalidateSv();
        this.Apply(mixer);
    }

    private void UpdateAlpha(Mixer mixer, MouseEventArgs e)
    {
        mixer.A = (byte)Math.Round((1 - Math.Clamp((e.Y - this.AlphaRect.Y) / (double)(this.AlphaRect.Height - 1), 0, 1)) * 255);
        this.Apply(mixer);
    }

    /// <summary>Recomputes the colour from the mixer's HSV(+A), pushes it out and repaints the popup.</summary>
    private void Apply(Mixer mixer)
    {
        this.SelectedColor = mixer.Color;
        _popup?.InvalidateAll();
    }

    /// <summary>Seeds the mixer from a swatch and commits it, so a swatch click behaves like a full edit.</summary>
    private void Commit(Mixer mixer, Color color)
    {
        mixer.Set(this.HasAlpha ? color : Color.FromArgb(255, color));
        this.Apply(mixer);
    }

    private bool SwatchAt(Color[]? colors, int cols, int rows, int top, MouseEventArgs e, out Color color)
    {
        color = Color.Black;
        for (var i = 0; i < cols * rows; ++i)
        {
            if (colors is null || i >= colors.Length)
                continue;

            var cell = new Rectangle(_Pad + ((i % cols) * _Cell), top + ((i / cols) * _Cell), _Cell, _Cell);
            if (cell.Contains(e.X, e.Y))
            {
                color = colors[i];
                return true;
            }
        }

        return false;
    }

    // --- Shared painting helpers -------------------------------------------------------------------

    private static Rectangle Border(Rectangle r) => new(r.X, r.Y, r.Width - 1, r.Height - 1);

    /// <summary>Fills a rectangle with a small grey/white checker, the standard "transparent" ground.</summary>
    private static void PaintChecker(IGraphics g, Rectangle area)
    {
        const int tile = 6;
        g.FillRectangle(Color.White, area);
        for (var y = 0; y < area.Height; y += tile)
        for (var x = 0; x < area.Width; x += tile)
            if (((x / tile) + (y / tile)) % 2 == 1)
                g.FillRectangle(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC),
                    new Rectangle(area.X + x, area.Y + y, Math.Min(tile, area.Width - x), Math.Min(tile, area.Height - y)));
    }

    private static Color[] BuildPalette() =>
    [
        Color.Black, Color.FromArgb(64, 64, 64), Color.Gray, Color.FromArgb(128, 128, 128), Color.Silver, Color.FromArgb(192, 192, 192), Color.FromArgb(224, 224, 224), Color.White,
        Color.DarkRed, Color.Red, Color.OrangeRed, Color.Orange, Color.Gold, Color.Yellow, Color.YellowGreen, Color.GreenYellow,
        Color.DarkGreen, Color.Green, Color.SeaGreen, Color.LimeGreen, Color.Teal, Color.Cyan, Color.Turquoise, Color.Aquamarine,
        Color.Navy, Color.Blue, Color.RoyalBlue, Color.DodgerBlue, Color.SteelBlue, Color.SkyBlue, Color.SlateBlue, Color.MediumPurple,
        Color.Indigo, Color.Purple, Color.DarkViolet, Color.Magenta, Color.Orchid, Color.HotPink, Color.Pink, Color.SaddleBrown,
    ];

    /// <summary>Which slider a mixer drag is tracking.</summary>
    private enum DragTarget { None, Sv, Hue, Alpha }

    /// <summary>The mixer's transient state, alive only while the drop-down is open — the HSV(+A) working
    /// value, the colour the field held when opened, the active drag and the cached gradient bitmaps.</summary>
    private sealed class Mixer
    {
        internal double H, S, V;
        internal byte A;
        internal readonly Color Old;
        internal DragTarget Drag;

        private IImage? _sv;
        private int _svHue = -1;
        private IImage? _hue;
        private IImage? _alpha;
        private int _alphaKey = -1;

        internal Mixer(Color color)
        {
            this.Old = color;
            this.Set(color);
        }

        internal Color Color => ColorMath.HsvToColor(this.H, this.S, this.V, this.A);

        internal void Set(Color color)
        {
            ColorMath.ColorToHsv(color, out this.H, out this.S, out this.V);
            this.A = color.A;
            this.InvalidateSv();
        }

        internal void InvalidateSv() => _svHue = -1;

        internal IImage SvImage(IPlatformBackend backend, Size size)
        {
            var hue = (int)Math.Round(this.H);
            if (_sv is not null && _svHue == hue)
                return _sv;

            var pixels = new int[size.Width * size.Height];
            for (var y = 0; y < size.Height; ++y)
            for (var x = 0; x < size.Width; ++x)
                pixels[(y * size.Width) + x] =
                    ColorMath.HsvToColor(hue, x / (double)(size.Width - 1), 1 - (y / (double)(size.Height - 1))).ToArgb();

            _sv = backend.CreateImage(size.Width, size.Height, pixels);
            _svHue = hue;
            return _sv;
        }

        internal IImage HueImage(IPlatformBackend backend, Size size)
        {
            if (_hue is not null)
                return _hue;

            var pixels = new int[size.Width * size.Height];
            for (var y = 0; y < size.Height; ++y)
            {
                var argb = ColorMath.HsvToColor(y / (double)(size.Height - 1) * 360, 1, 1).ToArgb();
                for (var x = 0; x < size.Width; ++x)
                    pixels[(y * size.Width) + x] = argb;
            }

            _hue = backend.CreateImage(size.Width, size.Height, pixels);
            return _hue;
        }

        internal IImage AlphaImage(IPlatformBackend backend, Size size)
        {
            var opaque = ColorMath.HsvToColor(this.H, this.S, this.V);
            var key = opaque.ToArgb();
            if (_alpha is not null && _alphaKey == key)
                return _alpha;

            var pixels = new int[size.Width * size.Height];
            for (var y = 0; y < size.Height; ++y)
            {
                var a = (int)Math.Round((1 - (y / (double)(size.Height - 1))) * 255);
                var argb = Color.FromArgb(a, opaque.R, opaque.G, opaque.B).ToArgb();
                for (var x = 0; x < size.Width; ++x)
                    pixels[(y * size.Width) + x] = argb;
            }

            _alpha = backend.CreateImage(size.Width, size.Height, pixels);
            _alphaKey = key;
            return _alpha;
        }
    }
}
