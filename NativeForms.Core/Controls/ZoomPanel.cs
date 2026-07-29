using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>Arguments for <see cref="ZoomPanel.PaintContent"/>: the surface plus the current content→view
/// mapping, so a host can draw its own scaled/panned content (vector shapes, selection handles) over the
/// panel. View = content × <see cref="Zoom"/> + <see cref="Origin"/>.</summary>
public sealed class ZoomPanelPaintEventArgs(IGraphics graphics, double zoom, PointF origin, Rectangle viewport) : EventArgs
{
    /// <summary>The drawing surface, already clipped to the content viewport.</summary>
    public IGraphics Graphics { get; } = graphics;

    /// <summary>The current scale factor.</summary>
    public double Zoom { get; } = zoom;

    /// <summary>The view-space position of content coordinate (0, 0).</summary>
    public PointF Origin { get; } = origin;

    /// <summary>The client rectangle content is drawn into (inside any rulers).</summary>
    public Rectangle Viewport { get; } = viewport;

    /// <summary>Maps a content-space point to its view-space (client) position.</summary>
    public PointF ToView(PointF content)
        => new((float)(content.X * this.Zoom) + this.Origin.X, (float)(content.Y * this.Zoom) + this.Origin.Y);
}

/// <summary>
/// A zoomable, pannable viewport. Displays an <see cref="Image"/> (and/or host-drawn
/// <see cref="PaintContent"/>) scaled by <see cref="Zoom"/> and offset by a pan the user drives with a
/// left-drag; the mouse wheel zooms about the cursor. <see cref="FitToWindow"/> and
/// <see cref="ActualSize"/> frame the content, and optional <see cref="ShowRulers"/> draw tick bands along
/// the top and left. The working surface for an image editor and a document / media viewport.
/// </summary>
public class ZoomPanel : OwnerDrawnControl
{
    private const int _RulerSize = 16;
    private const double _WheelStep = 1.1;   // one wheel notch multiplies the zoom by this

    private double _zoom = 1.0;
    private double _offX, _offY;             // view-space (viewport-local) position of content (0,0)
    private Size _contentSize;
    private bool _panning;
    private bool _draggingZoom;
    private int _panLastX, _panLastY;

    /// <summary>The image shown as the content, or <see langword="null"/>. Setting it adopts its size as
    /// the <see cref="ContentSize"/> and fits it to the window.</summary>
    public IImage? Image
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            if (value is not null)
                _contentSize = new Size(value.Width, value.Height);

            this.FitToWindow();
            this.Invalidate();
        }
    }

    /// <summary>The size of the content in its own pixels; set explicitly when there is no
    /// <see cref="Image"/> but a <see cref="PaintContent"/> host draws the content.</summary>
    public Size ContentSize
    {
        get => _contentSize;
        set
        {
            _contentSize = new Size(Math.Max(0, value.Width), Math.Max(0, value.Height));
            this.Invalidate();
        }
    }

    /// <summary>The smallest allowed zoom. Defaults to 0.05 (5%).</summary>
    public double MinZoom { get; set; } = 0.05;

    /// <summary>The largest allowed zoom. Defaults to 20 (2000%).</summary>
    public double MaxZoom { get; set; } = 20.0;

    /// <summary>The current scale factor (1.0 = actual size). Clamped to [<see cref="MinZoom"/>,
    /// <see cref="MaxZoom"/>]; setting it zooms about the viewport centre.</summary>
    public double Zoom
    {
        get => _zoom;
        set => this.ZoomTo(value, this.Viewport.Width / 2, this.Viewport.Height / 2);
    }

    /// <summary>Whether tick-mark rulers are drawn along the top and left. Defaults to <see langword="false"/>.</summary>
    public bool ShowRulers
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    }

    /// <summary>Whether a bottom-right zoom control (−, a slider, +, and a percentage read-out) is drawn.
    /// Defaults to <see langword="true"/>.</summary>
    public bool ShowZoomControl
    {
        get => field;
        set { if (field != value) { field = value; this.Invalidate(); } }
    } = true;

    /// <summary>The content-space spacing of an overlaid grid, in pixels; <c>0</c> (the default) draws no
    /// grid. The grid is only drawn once a cell is at least a few device pixels wide.</summary>
    public int GridSize
    {
        get => field;
        set { value = Math.Max(0, value); if (field != value) { field = value; this.Invalidate(); } }
    }

    /// <summary>The grid line colour. Defaults to the theme grid line.</summary>
    public Color GridColor
    {
        get => _gridColor ?? this.Theme.GridLine;
        set { _gridColor = value; this.Invalidate(); }
    }

    private Color? _gridColor;

    /// <summary>Raised whenever <see cref="Zoom"/> changes.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>Raised while painting so a host can draw scaled/panned content over the panel.</summary>
    public event EventHandler<ZoomPanelPaintEventArgs>? PaintContent;

    /// <summary>Raises <see cref="ZoomChanged"/>.</summary>
    protected virtual void OnZoomChanged(EventArgs e) => this.ZoomChanged?.Invoke(this, e);

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <inheritdoc/>
    private protected override Color FallbackBackColor => this.Theme.ControlBackground;

    /// <summary>The client rectangle content occupies, inside the rulers when shown.</summary>
    public Rectangle Viewport => this.ShowRulers
        ? new Rectangle(_RulerSize, _RulerSize, Math.Max(0, this.Width - _RulerSize), Math.Max(0, this.Height - _RulerSize))
        : new Rectangle(0, 0, this.Width, this.Height);

    /// <summary>Scales the content to fit entirely within the viewport and centres it.</summary>
    public void FitToWindow()
    {
        var view = this.Viewport;
        if (_contentSize.Width <= 0 || _contentSize.Height <= 0 || view.Width <= 0 || view.Height <= 0)
            return;

        var fit = Math.Min((double)view.Width / _contentSize.Width, (double)view.Height / _contentSize.Height);
        this.SetZoom(Math.Clamp(fit, this.MinZoom, this.MaxZoom));
        this.CenterContent();
        this.Invalidate();
    }

    /// <summary>Resets the zoom to 1.0 and centres the content.</summary>
    public void ActualSize()
    {
        this.SetZoom(1.0);
        this.CenterContent();
        this.Invalidate();
    }

    /// <summary>Zooms to <paramref name="zoom"/> keeping the content point under the given viewport-local
    /// pixel fixed under it.</summary>
    public void ZoomTo(double zoom, int anchorX, int anchorY)
    {
        zoom = Math.Clamp(zoom, this.MinZoom, this.MaxZoom);
        if (zoom == _zoom)
            return;

        // The content point currently under the anchor must stay under it after the scale change.
        var contentX = (anchorX - _offX) / _zoom;
        var contentY = (anchorY - _offY) / _zoom;
        this.SetZoom(zoom);
        _offX = anchorX - (contentX * _zoom);
        _offY = anchorY - (contentY * _zoom);
        this.Invalidate();
    }

    private void SetZoom(double zoom)
    {
        if (zoom == _zoom)
            return;

        _zoom = zoom;
        this.OnZoomChanged(EventArgs.Empty);
    }

    private void CenterContent()
    {
        var view = this.Viewport;
        _offX = (view.Width - (_contentSize.Width * _zoom)) / 2;
        _offY = (view.Height - (_contentSize.Height * _zoom)) / 2;
    }

    /// <inheritdoc/>
    private protected override void OnBoundsChanged()
    {
        base.OnBoundsChanged();
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(this.BackColor, new Rectangle(0, 0, this.Width, this.Height));

        var view = this.Viewport;
        g.PushClip(view);

        // The content, scaled and panned. Origin is the view-space position of content (0,0).
        var origin = new PointF(view.X + (float)_offX, view.Y + (float)_offY);
        if (this.Image is { } image && _contentSize.Width > 0 && _contentSize.Height > 0)
        {
            var dst = new Rectangle(
                (int)Math.Round(origin.X), (int)Math.Round(origin.Y),
                Math.Max(1, (int)Math.Round(_contentSize.Width * _zoom)),
                Math.Max(1, (int)Math.Round(_contentSize.Height * _zoom)));
            g.DrawImage(image, dst);
        }

        if (this.GridSize > 0)
            this.PaintGrid(g, view, origin);

        this.PaintContent?.Invoke(this, new ZoomPanelPaintEventArgs(g, _zoom, origin, view));
        g.PopClip();

        if (this.ShowRulers)
            this.PaintRulers(g, theme, view);

        if (this.ShowZoomControl)
            this.PaintZoomControl(g, theme, view);

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    private void PaintGrid(IGraphics g, Rectangle view, PointF origin)
    {
        var spacing = this.GridSize * _zoom;
        if (spacing < 4)
            return; // too dense to be legible

        var color = this.GridColor;
        var firstX = origin.X - ((float)Math.Floor((origin.X - view.X) / spacing) * spacing);
        for (var x = firstX; x < view.Right; x += (float)spacing)
            g.DrawLine(color, (int)Math.Round(x), view.Y, (int)Math.Round(x), view.Bottom);

        var firstY = origin.Y - ((float)Math.Floor((origin.Y - view.Y) / spacing) * spacing);
        for (var y = firstY; y < view.Bottom; y += (float)spacing)
            g.DrawLine(color, view.X, (int)Math.Round(y), view.Right, (int)Math.Round(y));
    }

    private void PaintRulers(IGraphics g, ITheme theme, Rectangle view)
    {
        var band = theme.HeaderBackground;
        g.FillRectangle(band, new Rectangle(0, 0, this.Width, _RulerSize));
        g.FillRectangle(band, new Rectangle(0, 0, _RulerSize, this.Height));

        var step = NiceStep(50.0 / _zoom); // aim for ticks roughly 50 px apart
        var tick = theme.Border;
        var label = theme.HeaderText;

        // Top ruler: content x = k·step maps to view x.
        var firstX = (int)Math.Floor((-_offX / _zoom) / step) * (int)step;
        for (var cx = firstX; ; cx += (int)step)
        {
            var vx = (int)Math.Round(view.X + _offX + (cx * _zoom));
            if (vx > this.Width)
                break;
            if (vx < _RulerSize)
                continue;

            g.DrawLine(tick, vx, _RulerSize - 5, vx, _RulerSize);
            g.DrawText(cx.ToString(), this.Font, label, new Rectangle(vx + 1, 0, 40, _RulerSize - 4), ContentAlignment.TopLeft);
        }

        // Left ruler: content y = k·step maps to view y; the number is drawn stacked one digit per line
        // (there is no rotated text) so the vertical ruler carries values too. The line is what the font
        // measures rather than a fixed guess, because the three text engines disagree about a box the
        // glyph does not fit in: GDI clips it, Cairo and CoreText let it spill over the digit below, and
        // the desktop's own UI font is taller than any guess on at least one of them.
        var lineHeight = Math.Max(1, g.MeasureText("0", this.Font).Height);
        var firstY = (int)Math.Floor((-_offY / _zoom) / step) * (int)step;
        for (var cy = firstY; ; cy += (int)step)
        {
            var vy = (int)Math.Round(view.Y + _offY + (cy * _zoom));
            if (vy > this.Height)
                break;
            if (vy < _RulerSize)
                continue;

            g.DrawLine(tick, _RulerSize - 5, vy, _RulerSize, vy);
            var digits = cy.ToString();
            for (var d = 0; d < digits.Length; ++d)
                g.DrawText(digits[d].ToString(), this.Font, label, new Rectangle(1, vy + 1 + (d * lineHeight), _RulerSize, lineHeight), ContentAlignment.TopCenter);
        }
    }

    // The bottom-right zoom control: [ − ][ slider ][ + ]  NNN%. The slider maps [MinZoom, MaxZoom] on a
    // log scale so equal pixel steps multiply the zoom by a constant factor.
    private const int _ZoomBarWidth = 120;
    private const int _ZoomBarHeight = 18;
    private const int _ZoomButton = 18;
    private const int _ZoomMargin = 8;

    private Rectangle ZoomControlRect
    {
        get
        {
            var view = this.Viewport;
            var width = (2 * _ZoomButton) + _ZoomBarWidth + 44; // buttons + slider + "NNN%"
            return new Rectangle(view.Right - width - _ZoomMargin, view.Bottom - _ZoomBarHeight - _ZoomMargin, width, _ZoomBarHeight);
        }
    }

    private Rectangle ZoomSliderRect
    {
        get { var r = this.ZoomControlRect; return new Rectangle(r.X + _ZoomButton, r.Y, _ZoomBarWidth, r.Height); }
    }

    private Rectangle ZoomReadoutRect
    {
        get { var r = this.ZoomControlRect; return new Rectangle(r.Right - 42, r.Y, 42, r.Height); }
    }

    private void PaintZoomControl(IGraphics g, ITheme theme, Rectangle view)
    {
        if (view.Width < 260 || view.Height < 60)
            return;

        var r = this.ZoomControlRect;
        g.FillRoundedRectangle(theme.ControlBackground, r, 4);
        g.DrawRoundedRectangle(theme.Border, r, 4);

        // − and + buttons.
        var minus = new Rectangle(r.X, r.Y, _ZoomButton, r.Height);
        var plus = new Rectangle(r.Right - 44 - _ZoomButton, r.Y, _ZoomButton, r.Height);
        g.DrawText("−", this.Font, theme.ControlText, minus, ContentAlignment.MiddleCenter);
        g.DrawText("+", this.Font, theme.ControlText, plus, ContentAlignment.MiddleCenter);

        // The slider groove and thumb.
        var slider = this.ZoomSliderRect;
        var grooveY = slider.Y + (slider.Height / 2);
        g.DrawLine(theme.Border, slider.X, grooveY, slider.Right, grooveY, 2);
        var t = this.ZoomToFraction(_zoom);
        var thumbX = slider.X + (int)Math.Round(t * slider.Width);
        g.FillRectangle(theme.Accent, new Rectangle(thumbX - 2, slider.Y + 2, 4, slider.Height - 4));

        // The percentage read-out (click to type an exact level; hidden while its editor is open).
        if (_zoomEditor is not { Visible: true })
            g.DrawText($"{Math.Round(_zoom * 100)}%", this.Font, theme.ControlText, this.ZoomReadoutRect, ContentAlignment.MiddleLeft);
    }

    // A tiny hosted editor over the percentage read-out: click it, type a level, Enter applies it.
    private TextBox? _zoomEditor;

    private void OpenZoomEditor()
    {
        if (_zoomEditor is null)
        {
            _zoomEditor = new FramelessTextBox { TabStop = false };
            _zoomEditor.KeyDown += this.OnZoomEditorKeyDown;
            this.Controls.Add(_zoomEditor);
        }

        _zoomEditor.Bounds = this.ZoomReadoutRect;
        _zoomEditor.Text = ((int)Math.Round(_zoom * 100)).ToString();
        _zoomEditor.Visible = true;
        _zoomEditor.SelectionStart = 0;
        _zoomEditor.SelectionLength = _zoomEditor.Text.Length;
        _zoomEditor.Focus();
        this.Invalidate();
    }

    private void OnZoomEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            var text = _zoomEditor!.Text.Replace("%", string.Empty).Trim();
            if (double.TryParse(text, out var percent) && percent > 0)
                this.Zoom = percent / 100.0;

            this.CloseZoomEditor();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            this.CloseZoomEditor();
            e.Handled = true;
        }
    }

    private void CloseZoomEditor()
    {
        if (_zoomEditor is not { Visible: true })
            return;

        _zoomEditor.Visible = false;
        this.Focus();
        this.Invalidate();
    }

    private double ZoomToFraction(double zoom)
    {
        var lo = Math.Log(this.MinZoom);
        var hi = Math.Log(this.MaxZoom);
        return hi <= lo ? 0 : Math.Clamp((Math.Log(zoom) - lo) / (hi - lo), 0, 1);
    }

    private double FractionToZoom(double fraction)
    {
        var lo = Math.Log(this.MinZoom);
        var hi = Math.Log(this.MaxZoom);
        return Math.Exp(lo + (Math.Clamp(fraction, 0, 1) * (hi - lo)));
    }

    // Rounds a raw spacing up to a 1/2/5·10^n step so ruler labels stay readable at any zoom.
    private static double NiceStep(double raw)
    {
        if (raw <= 1)
            return 1;

        var pow = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var frac = raw / pow;
        var nice = frac <= 1 ? 1 : frac <= 2 ? 2 : frac <= 5 ? 5 : 10;
        return nice * pow;
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Middle)
            return;

        if (e.Button == MouseButtons.Left && this.ShowZoomControl && this.HandleZoomControl(e.X, e.Y))
            return;

        _panning = true;
        _panLastX = e.X;
        _panLastY = e.Y;
    }

    // The bottom-right zoom control: − / + step, or a click/drag on the slider sets the zoom directly.
    private bool HandleZoomControl(int x, int y)
    {
        if (this.Viewport.Width < 260 || this.Viewport.Height < 60)
            return false;

        var r = this.ZoomControlRect;
        if (!r.Contains(x, y))
            return false;

        var slider = this.ZoomSliderRect;
        if (slider.Contains(x, y))
        {
            _draggingZoom = true;
            this.ZoomFromSlider(x);
        }
        else if (this.ZoomReadoutRect.Contains(x, y))
            this.OpenZoomEditor();
        else if (x < slider.X)
            this.Zoom = _zoom / _WheelStep;
        else
            this.Zoom = _zoom * _WheelStep;

        return true;
    }

    private void ZoomFromSlider(int x)
    {
        var slider = this.ZoomSliderRect;
        this.Zoom = this.FractionToZoom((double)(x - slider.X) / Math.Max(1, slider.Width));
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_draggingZoom)
        {
            this.ZoomFromSlider(e.X);
            return;
        }

        if (!_panning)
            return;

        _offX += e.X - _panLastX;
        _offY += e.Y - _panLastY;
        _panLastX = e.X;
        _panLastY = e.Y;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _panning = false;
        _draggingZoom = false;
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var factor = e.Delta > 0 ? _WheelStep : 1.0 / _WheelStep;
        var view = this.Viewport;
        this.ZoomTo(_zoom * factor, e.X - view.X, e.Y - view.Y);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Add or Keys.Oemplus:
                this.ZoomTo(_zoom * _WheelStep, this.Viewport.Width / 2, this.Viewport.Height / 2);
                e.Handled = true;
                break;
            case Keys.Subtract or Keys.OemMinus:
                this.ZoomTo(_zoom / _WheelStep, this.Viewport.Width / 2, this.Viewport.Height / 2);
                e.Handled = true;
                break;
            case Keys.D0 when e.Modifiers.HasFlag(KeyModifiers.Control):
                this.ActualSize();
                e.Handled = true;
                break;
            case Keys.D9 when e.Modifiers.HasFlag(KeyModifiers.Control):
                this.FitToWindow();
                e.Handled = true;
                break;
        }
    }
}
