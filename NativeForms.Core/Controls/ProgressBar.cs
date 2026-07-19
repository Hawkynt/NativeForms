using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn, determinate progress bar: a themed track with an accent-filled portion sized in
/// proportion to <see cref="Value"/> within [<see cref="Minimum"/>, <see cref="Maximum"/>]. Purely
/// visual — it takes no focus and handles no input.
/// </summary>
public class ProgressBar : OwnerDrawnControl
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;

    /// <summary>The lowest value the bar can represent.</summary>
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
            this.Invalidate();
        }
    }

    /// <summary>The highest value the bar can represent.</summary>
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
            this.Invalidate();
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
            this.Invalidate();
            this.OnValueChanged(EventArgs.Empty);
        }
    }

    /// <summary>Raised when <see cref="Value"/> changes.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raises <see cref="ValueChanged"/>.</summary>
    protected virtual void OnValueChanged(EventArgs e) => this.ValueChanged?.Invoke(this, e);

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
        => GlyphRenderer.DrawProgressBar(e.Graphics, this.Theme, new Rectangle(0, 0, this.Width, this.Height), _value, _minimum, _maximum);
}
