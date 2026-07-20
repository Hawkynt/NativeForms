using System.Globalization;

namespace Hawkynt.NativeForms;

/// <summary>
/// A spinner for a <see cref="decimal"/> value: the hosted editor shows the number formatted to
/// <see cref="DecimalPlaces"/>, the spinner buttons and Up/Down keys step by <see cref="Increment"/>,
/// and assignments clamp into [<see cref="Minimum"/>, <see cref="Maximum"/>].
/// </summary>
/// <remarks>
/// A typed edit is committed — parsed with clamping, or reverted when unparsable — at the base
/// class's commit points (before a step, on focus loss) and additionally whenever <see cref="Value"/>
/// is read while an edit is pending, mirroring the classic control's getter-side validation.
/// </remarks>
public class NumericUpDown : UpDownBase
{
    private decimal _minimum;
    private decimal _maximum = 100m;
    private decimal _value;

    /// <summary>Creates the spinner showing its initial value of 0.</summary>
    public NumericUpDown() => this.UpdateEditText();

    /// <summary>The lowest value the spinner accepts. Raising it above <see cref="Maximum"/> drags
    /// the maximum along; the current value re-clamps.</summary>
    public decimal Minimum
    {
        get => _minimum;
        set
        {
            if (_minimum == value)
                return;

            _minimum = value;
            if (_maximum < _minimum)
                _maximum = _minimum;

            this.SetValue(_value);
        }
    }

    /// <summary>The highest value the spinner accepts. Lowering it below <see cref="Minimum"/> drags
    /// the minimum along; the current value re-clamps.</summary>
    public decimal Maximum
    {
        get => _maximum;
        set
        {
            if (_maximum == value)
                return;

            _maximum = value;
            if (_minimum > _maximum)
                _minimum = _maximum;

            this.SetValue(_value);
        }
    }

    /// <summary>
    /// The current value, clamped to [<see cref="Minimum"/>, <see cref="Maximum"/>]. Reading it
    /// commits a pending typed edit first, so callers always see what the user entered.
    /// </summary>
    public decimal Value
    {
        get
        {
            this.CommitEdit();
            return _value;
        }
        set => this.SetValue(value);
    }

    /// <summary>The step a spinner button or Up/Down key changes the value by. Never negative.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public decimal Increment
    {
        get => field;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 1m;

    /// <summary>The number of decimal digits the editor displays (0–28).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than 28.</exception>
    public int DecimalPlaces
    {
        get => field;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 28);
            if (field == value)
                return;

            field = value;
            this.UpdateEditText();
        }
    }

    /// <summary>Whether the editor groups digits with the culture's thousands separator. Typed input
    /// parses with or without separators either way. Defaults to <see langword="false"/>.</summary>
    public bool ThousandsSeparator
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.UpdateEditText();
        }
    }

    /// <summary>Raised when <see cref="Value"/> changes, by stepping, typing or assignment.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raises <see cref="ValueChanged"/>.</summary>
    protected virtual void OnValueChanged(EventArgs e) => this.ValueChanged?.Invoke(this, e);

    /// <inheritdoc/>
    public override void UpButton()
    {
        this.CommitEdit();
        this.SetValue(_value + this.Increment);
    }

    /// <inheritdoc/>
    public override void DownButton()
    {
        this.CommitEdit();
        this.SetValue(_value - this.Increment);
    }

    /// <inheritdoc/>
    protected override void UpdateEditText()
        => this.SetEditorText(_value.ToString((this.ThousandsSeparator ? "N" : "F") + this.DecimalPlaces, CultureInfo.CurrentCulture));

    /// <inheritdoc/>
    protected override void ValidateEditText()
    {
        if (decimal.TryParse(this.Text, NumberStyles.Number | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var parsed))
            this.SetValue(parsed);
        else
            this.UpdateEditText(); // unparsable input reverts to the current value
    }

    /// <summary>Clamps and applies a new value, refreshing the editor and raising
    /// <see cref="ValueChanged"/> when it actually moved.</summary>
    private void SetValue(decimal value)
    {
        var clamped = Math.Clamp(value, _minimum, _maximum);
        if (_value == clamped)
        {
            this.UpdateEditText(); // still normalize the editor ("042", clamped input, stale format)
            return;
        }

        _value = clamped;
        this.UpdateEditText();
        this.OnValueChanged(EventArgs.Empty);
    }
}
