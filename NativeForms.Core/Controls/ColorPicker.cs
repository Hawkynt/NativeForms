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
    private int TabsY => this.CustomY + (_CustomRows * _Cell) + 8;
    private int ChannelsY => this.TabsY + this.Theme.RowHeight + 4;
    private int NumericWidth => this.PopupInnerWidth;
    private int PopupInnerWidth => this.RightColumnX + _PreviewW - _Pad;

    private Size PopupSize => new(this.RightColumnX + _PreviewW + _Pad, this.ChannelsY + (4 * this.Theme.RowHeight) + _Pad);

    private static readonly string[] _Spaces = ["RGB", "HSL", "HSV", "CMYK"];

    private Rectangle TabRect(int i)
    {
        var w = this.NumericWidth / 4;
        return new Rectangle(_Pad + (i * w), this.TabsY, w, this.Theme.RowHeight);
    }

    private Rectangle ChannelTrack(int i)
    {
        const int labelW = 26, valueW = 40;
        var y = this.ChannelsY + (i * this.Theme.RowHeight);
        return new Rectangle(_Pad + labelW, y + 4, this.NumericWidth - labelW - valueW, this.Theme.RowHeight - 8);
    }

    private Rectangle EyedropperRect => new(this.PopupSize.Width - _Pad - 22, this.HexY, 22, this.Theme.RowHeight);
    private Rectangle WheelToggleRect => new(this.EyedropperRect.X - 24, this.HexY, 22, this.Theme.RowHeight);

    // --- Popup wiring ------------------------------------------------------------------------------

    private IPopupPeer CreatePopup(IPlatformBackend backend)
    {
        var popup = backend.CreatePopup(this.OwnerWindowPeer);
        popup.Paint += (_, e) => this.PaintMixer(e.Graphics);
        popup.MouseDown += (_, e) => this.OnMixerDown(e);
        popup.MouseMove += (_, e) => this.OnMixerMove(e);
        popup.MouseUp += (_, _) => { if (_mixer is { } m) { m.Drag = DragTarget.None; m.Channel = -1; } };
        popup.OutsidePress = this.OnSampleClick; // the armed eyedropper turns the next outside click into a sample
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

        // The saturation/value square (or hue wheel), the hue ramp and (optionally) the alpha ramp, each a
        // cached ARGB bitmap blitted through DrawImage — IGraphics has no gradient primitive.
        this.PaintSvArea(g, backend, mixer);

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
            new Rectangle(_Pad, this.HexY, size.Width - (3 * _Pad) - 48, theme.RowHeight), ContentAlignment.MiddleLeft);
        this.PaintWheelToggle(g, mixer);
        this.PaintEyedropper(g, mixer);

        this.PaintSwatches(g, _Basic, _BasicCols, _BasicRows, this.BasicY);
        this.PaintSwatches(g, _custom, _CustomCols, _CustomRows, this.CustomY);
        this.PaintNumeric(g, mixer);

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

    private void PaintSvArea(IGraphics g, IPlatformBackend backend, Mixer mixer)
    {
        var theme = this.Theme;
        if (!mixer.Wheel)
        {
            g.DrawImage(mixer.SvImage(backend, this.SvRect.Size), this.SvRect);
            g.DrawRectangle(theme.Border, Border(this.SvRect));
            this.PaintReticle(g, this.SvRect, new Point(
                this.SvRect.X + (int)(mixer.S * (this.SvRect.Width - 1)),
                this.SvRect.Y + (int)((1 - mixer.V) * (this.SvRect.Height - 1))));
            return;
        }

        g.FillRectangle(theme.ControlBackground, this.SvRect);
        g.DrawImage(mixer.WheelImage(backend, this.SvRect.Size), this.SvRect);
        g.DrawRectangle(theme.Border, Border(this.SvRect));

        Mixer.WheelGeometry(this.SvRect.Size, out var cx, out var cy, out var outerR, out var innerR, out var inner);
        var ringR = (outerR + innerR) / 2.0;
        var angle = mixer.H * Math.PI / 180;
        var hx = this.SvRect.X + cx + (int)(Math.Cos(angle) * ringR);
        var hy = this.SvRect.Y + cy + (int)(Math.Sin(angle) * ringR);
        g.DrawEllipse(Color.White, new Rectangle(hx - 4, hy - 4, 8, 8));
        g.DrawEllipse(Color.Black, new Rectangle(hx - 3, hy - 3, 6, 6));

        var innerRect = new Rectangle(this.SvRect.X + inner.X, this.SvRect.Y + inner.Y, inner.Width, inner.Height);
        this.PaintReticle(g, innerRect, new Point(
            innerRect.X + (int)(mixer.S * (inner.Width - 1)),
            innerRect.Y + (int)((1 - mixer.V) * (inner.Height - 1))));
    }

    private void PaintWheelToggle(IGraphics g, Mixer mixer)
    {
        var theme = this.Theme;
        var r = this.WheelToggleRect;
        g.FillRectangle(mixer.Wheel ? theme.SelectionBackground : theme.HeaderBackground, r);
        g.DrawRectangle(mixer.Wheel ? theme.Accent : theme.Border, Border(r));

        // A ring glyph while the square is showing (tap to go to the wheel), a square glyph while it is.
        var ink = mixer.Wheel ? theme.SelectionText : theme.ControlText;
        if (mixer.Wheel)
            g.DrawRectangle(ink, new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12));
        else
            g.DrawEllipse(ink, new Rectangle(r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10));
    }

    private void WheelDown(Mixer mixer, MouseEventArgs e)
    {
        Mixer.WheelGeometry(this.SvRect.Size, out var cx, out var cy, out var outerR, out _, out var inner);
        var innerRect = new Rectangle(this.SvRect.X + inner.X, this.SvRect.Y + inner.Y, inner.Width, inner.Height);
        if (innerRect.Contains(e.X, e.Y))
        {
            mixer.Drag = DragTarget.WheelSquare;
            this.UpdateWheelSquare(mixer, e);
            return;
        }

        double dx = (e.X - this.SvRect.X) - cx, dy = (e.Y - this.SvRect.Y) - cy;
        if (Math.Sqrt((dx * dx) + (dy * dy)) <= outerR + 2)
        {
            mixer.Drag = DragTarget.WheelRing;
            this.UpdateWheelRing(mixer, e);
        }
    }

    private void UpdateWheelRing(Mixer mixer, MouseEventArgs e)
    {
        Mixer.WheelGeometry(this.SvRect.Size, out var cx, out var cy, out _, out _, out _);
        double dx = (e.X - this.SvRect.X) - cx, dy = (e.Y - this.SvRect.Y) - cy;
        mixer.H = ((Math.Atan2(dy, dx) * 180 / Math.PI) + 360) % 360;
        mixer.InvalidateSv();
        this.Apply(mixer);
    }

    private void UpdateWheelSquare(Mixer mixer, MouseEventArgs e)
    {
        Mixer.WheelGeometry(this.SvRect.Size, out _, out _, out _, out _, out var inner);
        var ix = this.SvRect.X + inner.X;
        var iy = this.SvRect.Y + inner.Y;
        mixer.S = Math.Clamp((e.X - ix) / (double)(inner.Width - 1), 0, 1);
        mixer.V = 1 - Math.Clamp((e.Y - iy) / (double)(inner.Height - 1), 0, 1);
        this.Apply(mixer);
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

    private void PaintEyedropper(IGraphics g, Mixer mixer)
    {
        var theme = this.Theme;
        var r = this.EyedropperRect;
        g.FillRectangle(mixer.Sampling ? theme.SelectionBackground : theme.HeaderBackground, r);
        g.DrawRectangle(mixer.Sampling ? theme.Accent : theme.Border, Border(r));

        // A small pipette: a diagonal body with a tip at the lower-left corner.
        var ink = mixer.Sampling ? theme.SelectionText : theme.ControlText;
        g.DrawLine(ink, r.X + 6, r.Bottom - 6, r.Right - 6, r.Y + 6, 2);
        g.FillEllipse(ink, new Rectangle(r.X + 4, r.Bottom - 9, 4, 4));
    }

    /// <summary>Offered the outside click that would normally dismiss the mixer: when the eyedropper is
    /// armed it becomes a screen colour sample instead, and the mixer stays open.</summary>
    private bool OnSampleClick(Point screen)
    {
        if (_mixer is not { Sampling: true } mixer || this.Backend is not { } backend)
            return false;

        var sampled = backend.SampleScreenPixel(screen);
        mixer.Sampling = false;
        if (!sampled.IsEmpty)
        {
            mixer.Set(this.HasAlpha ? Color.FromArgb(mixer.A, sampled) : sampled);
            this.Apply(mixer);
        }
        else
        {
            _popup?.InvalidateAll();
        }

        return true;
    }

    private void OnMixerDown(MouseEventArgs e)
    {
        if (_mixer is not { } mixer)
            return;

        if (this.EyedropperRect.Contains(e.X, e.Y))
        {
            mixer.Sampling = !mixer.Sampling; // arm/disarm the eyedropper
            _popup?.InvalidateAll();
            return;
        }

        if (this.WheelToggleRect.Contains(e.X, e.Y))
        {
            mixer.Wheel = !mixer.Wheel; // swap the SV square for the hue wheel
            _popup?.InvalidateAll();
            return;
        }

        if (this.SvRect.Contains(e.X, e.Y))
        {
            if (mixer.Wheel)
            {
                this.WheelDown(mixer, e);
            }
            else
            {
                mixer.Drag = DragTarget.Sv;
                this.UpdateSv(mixer, e);
            }

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
        {
            this.Commit(mixer, custom);
            return;
        }

        this.OnNumericDown(mixer, e);
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
            case DragTarget.Channel: this.UpdateChannel(mixer, e); break;
            case DragTarget.WheelRing: this.UpdateWheelRing(mixer, e); break;
            case DragTarget.WheelSquare: this.UpdateWheelSquare(mixer, e); break;
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

    // --- Numeric tabs ------------------------------------------------------------------------------

    private static int ChannelCount(ColorSpace space) => space == ColorSpace.Cmyk ? 4 : 3;

    private static (string Label, double Max) ChannelSpec(ColorSpace space, int i) => space switch
    {
        ColorSpace.Rgb => (("RGB"[i]).ToString(), 255),
        ColorSpace.Hsl => (i == 0 ? "H" : i == 1 ? "S" : "L", i == 0 ? 360 : 100),
        ColorSpace.Hsv => (i == 0 ? "H" : i == 1 ? "S" : "V", i == 0 ? 360 : 100),
        _ => (("CMYK"[i]).ToString(), 100),
    };

    private static double ChannelValue(Mixer mixer, int i)
    {
        var c = mixer.Color;
        switch (mixer.Space)
        {
            case ColorSpace.Rgb: return i == 0 ? c.R : i == 1 ? c.G : c.B;
            case ColorSpace.Hsv: return i == 0 ? mixer.H : i == 1 ? mixer.S * 100 : mixer.V * 100;
            case ColorSpace.Hsl:
                ColorMath.ColorToHsl(c, out var h, out var s, out var l);
                return i == 0 ? h : i == 1 ? s * 100 : l * 100;
            default:
                ColorMath.ColorToCmyk(c, out var cc, out var mm, out var yy, out var kk);
                return (i == 0 ? cc : i == 1 ? mm : i == 2 ? yy : kk) * 100;
        }
    }

    private static void SetChannel(Mixer mixer, int i, double value)
    {
        var c = mixer.Color;
        switch (mixer.Space)
        {
            case ColorSpace.Rgb:
                var r = i == 0 ? (int)Math.Round(value) : c.R;
                var g = i == 1 ? (int)Math.Round(value) : c.G;
                var b = i == 2 ? (int)Math.Round(value) : c.B;
                mixer.Set(Color.FromArgb(mixer.A, Clamp8(r), Clamp8(g), Clamp8(b)));
                break;
            case ColorSpace.Hsv:
                if (i == 0) mixer.H = value;
                else if (i == 1) mixer.S = value / 100;
                else mixer.V = value / 100;
                mixer.InvalidateSv();
                break;
            case ColorSpace.Hsl:
                ColorMath.ColorToHsl(c, out var h, out var s, out var l);
                if (i == 0) h = value; else if (i == 1) s = value / 100; else l = value / 100;
                mixer.Set(ColorMath.HslToColor(h, s, l, mixer.A));
                break;
            default:
                ColorMath.ColorToCmyk(c, out var cc, out var mm, out var yy, out var kk);
                var v = value / 100;
                if (i == 0) cc = v; else if (i == 1) mm = v; else if (i == 2) yy = v; else kk = v;
                mixer.Set(ColorMath.CmykToColor(cc, mm, yy, kk, mixer.A));
                break;
        }
    }

    private static int Clamp8(int x) => x < 0 ? 0 : x > 255 ? 255 : x;

    private void PaintNumeric(IGraphics g, Mixer mixer)
    {
        var theme = this.Theme;
        for (var i = 0; i < _Spaces.Length; ++i)
        {
            var tab = this.TabRect(i);
            var active = (int)mixer.Space == i;
            g.FillRectangle(active ? theme.ControlBackground : theme.HeaderBackground, tab);
            if (active)
                g.FillRectangle(theme.Accent, new Rectangle(tab.X, tab.Bottom - 2, tab.Width, 2));
            g.DrawRectangle(theme.Border, Border(tab));
            g.DrawText(_Spaces[i], theme.DefaultFont, active ? theme.ControlText : theme.HeaderText, tab, ContentAlignment.MiddleCenter);
        }

        var count = ChannelCount(mixer.Space);
        for (var i = 0; i < count; ++i)
        {
            var (label, max) = ChannelSpec(mixer.Space, i);
            var value = ChannelValue(mixer, i);
            var track = this.ChannelTrack(i);
            var labelRect = new Rectangle(_Pad, track.Y - 4, 24, this.Theme.RowHeight);
            g.DrawText(label, theme.DefaultFont, theme.ControlText, labelRect, ContentAlignment.MiddleLeft);

            g.FillRectangle(theme.FieldBackground, track);
            var t = max <= 0 ? 0 : Math.Clamp(value / max, 0, 1);
            g.FillRectangle(theme.Accent, new Rectangle(track.X, track.Y, (int)(t * track.Width), track.Height));
            g.DrawRectangle(theme.Border, Border(track));

            var valueRect = new Rectangle(track.Right + 4, track.Y - 4, 36, this.Theme.RowHeight);
            g.DrawText(((int)Math.Round(value)).ToString(), theme.DefaultFont, theme.ControlText, valueRect, ContentAlignment.MiddleRight);
        }
    }

    private bool OnNumericDown(Mixer mixer, MouseEventArgs e)
    {
        for (var i = 0; i < _Spaces.Length; ++i)
            if (this.TabRect(i).Contains(e.X, e.Y))
            {
                mixer.Space = (ColorSpace)i;
                _popup?.InvalidateAll();
                return true;
            }

        for (var i = 0; i < ChannelCount(mixer.Space); ++i)
        {
            var track = this.ChannelTrack(i);
            if (track.Contains(e.X, e.Y) || (e.Y >= track.Y - 4 && e.Y < track.Bottom + 4 && e.X >= track.X && e.X < track.Right + 40))
            {
                mixer.Drag = DragTarget.Channel;
                mixer.Channel = i;
                this.UpdateChannel(mixer, e);
                return true;
            }
        }

        return false;
    }

    private void UpdateChannel(Mixer mixer, MouseEventArgs e)
    {
        if (mixer.Channel < 0)
            return;

        var (_, max) = ChannelSpec(mixer.Space, mixer.Channel);
        var track = this.ChannelTrack(mixer.Channel);
        var t = Math.Clamp((e.X - track.X) / (double)Math.Max(1, track.Width - 1), 0, 1);
        SetChannel(mixer, mixer.Channel, t * max);
        this.Apply(mixer);
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
    private enum DragTarget { None, Sv, Hue, Alpha, Channel, WheelRing, WheelSquare }

    /// <summary>The numeric-tab colour space.</summary>
    private enum ColorSpace { Rgb, Hsl, Hsv, Cmyk }

    /// <summary>The mixer's transient state, alive only while the drop-down is open — the HSV(+A) working
    /// value, the colour the field held when opened, the active drag and the cached gradient bitmaps.</summary>
    private sealed class Mixer
    {
        internal double H, S, V;
        internal byte A;
        internal readonly Color Old;
        internal DragTarget Drag;
        internal ColorSpace Space;
        internal int Channel = -1; // the numeric row being dragged, or -1
        internal bool Sampling; // the eyedropper is armed: the next screen click is a colour sample
        internal bool Wheel;    // the SV area shows a hue ring + inner square instead of the SV square

        private IImage? _sv;
        private int _svHue = -1;
        private IImage? _hue;
        private IImage? _alpha;
        private int _alphaKey = -1;
        private IImage? _wheel;
        private int _wheelHue = -1;

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

        internal void InvalidateSv() { _svHue = -1; _wheelHue = -1; }

        // --- Hue-wheel geometry (a ring of hues around an inscribed saturation/value square) -----------
        internal const int RingThickness = 14;

        internal static void WheelGeometry(Size size, out int cx, out int cy, out int outerR, out int innerR, out Rectangle inner)
        {
            cx = size.Width / 2;
            cy = size.Height / 2;
            outerR = (Math.Min(size.Width, size.Height) / 2) - 1;
            innerR = outerR - RingThickness;
            var half = (int)(innerR / Math.Sqrt(2));
            inner = new Rectangle(cx - half, cy - half, 2 * half, 2 * half);
        }

        internal IImage WheelImage(IPlatformBackend backend, Size size)
        {
            var hue = (int)Math.Round(this.H);
            if (_wheel is not null && _wheelHue == hue)
                return _wheel;

            WheelGeometry(size, out var cx, out var cy, out var outerR, out var innerR, out var inner);
            var pixels = new int[size.Width * size.Height];
            for (var y = 0; y < size.Height; ++y)
            for (var x = 0; x < size.Width; ++x)
            {
                double dx = x - cx, dy = y - cy;
                var dist = Math.Sqrt((dx * dx) + (dy * dy));
                int argb;
                if (dist <= outerR && dist >= innerR)
                {
                    var angle = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                    argb = ColorMath.HsvToColor(angle, 1, 1).ToArgb();
                }
                else if (inner.Contains(x, y))
                {
                    var s = (x - inner.X) / (double)(inner.Width - 1);
                    var v = 1 - ((y - inner.Y) / (double)(inner.Height - 1));
                    argb = ColorMath.HsvToColor(hue, s, v).ToArgb();
                }
                else
                {
                    argb = 0; // transparent outside the ring and square
                }

                pixels[(y * size.Width) + x] = argb;
            }

            _wheel = backend.CreateImage(size.Width, size.Height, pixels);
            _wheelHue = hue;
            return _wheel;
        }

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
