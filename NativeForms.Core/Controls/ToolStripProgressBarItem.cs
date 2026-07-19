namespace Hawkynt.NativeForms;

/// <summary>
/// A determinate progress gauge embedded in a <see cref="StatusStrip"/>. It paints through the same
/// renderer as the standalone <see cref="ProgressBar"/> control, so the fill math and theming are
/// identical — only the hosting differs.
/// </summary>
public class ToolStripProgressBarItem : ToolStripItem
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;

    /// <summary>The fixed pixel width the gauge occupies in the strip. Defaults to 100.</summary>
    public int Width
    {
        get => field;
        set
        {
            var clamped = Math.Max(1, value);
            if (field == clamped)
                return;

            field = clamped;
            this.NotifyOwner();
        }
    } = 100;

    /// <summary>The lowest value the gauge can represent.</summary>
    public int Minimum
    {
        get => _minimum;
        set
        {
            if (_minimum == value)
                return;

            _minimum = value;
            if (_maximum < _minimum)
                _maximum = _minimum;

            this.Value = _value;
            this.NotifyOwner();
        }
    }

    /// <summary>The highest value the gauge can represent.</summary>
    public int Maximum
    {
        get => _maximum;
        set
        {
            if (_maximum == value)
                return;

            _maximum = value;
            if (_minimum > _maximum)
                _minimum = _maximum;

            this.Value = _value;
            this.NotifyOwner();
        }
    }

    /// <summary>The current progress, clamped to [<see cref="Minimum"/>, <see cref="Maximum"/>].</summary>
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _minimum, _maximum);
            if (_value == clamped)
                return;

            _value = clamped;
            this.NotifyOwner();
        }
    }

    /// <summary>Paints the gauge into <paramref name="bounds"/> via the shared progress renderer.</summary>
    internal void Paint(Drawing.IGraphics g, Drawing.ITheme theme, System.Drawing.Rectangle bounds)
        => Drawing.GlyphRenderer.DrawProgressBar(g, theme, bounds, _value, _minimum, _maximum);
}
