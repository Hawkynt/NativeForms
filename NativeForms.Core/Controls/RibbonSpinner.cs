using System.Globalization;

namespace Hawkynt.NativeForms;

/// <summary>
/// A numeric up/down field on the ribbon: a caption, a box showing <see cref="Value"/> and a pair of
/// arrows that step it by <see cref="Increment"/> within [<see cref="Minimum"/>, <see cref="Maximum"/>].
/// Clicking the number edits it from the keyboard — digits, the culture's decimal separator when
/// decimals are shown and, when the range reaches below zero, a minus sign; the first keystroke replaces
/// the whole value. Enter, a click elsewhere, focus leaving or the flyout closing commits the entry,
/// rounded and clamped; an entry that does not parse reverts, and Escape throws it away. Up/Down step
/// the value while it is being edited. Owner-drawn by the ribbon, so it sizes to a small row and keeps
/// working inside a collapsed group's flyout.
/// </summary>
public class RibbonSpinner : RibbonFieldItem {
  private decimal _minimum;
  private decimal _maximum = 100m;
  private decimal _value;
  private int _decimalPlaces;

  /// <summary>The formatted <see cref="Value"/>, cached so a repaint formats nothing.</summary>
  private string? _text;

  /// <summary>Creates a spinner without a caption, ranging 0–100.</summary>
  public RibbonSpinner() { }

  /// <summary>Creates a spinner with the given caption beside its box, ranging 0–100.</summary>
  public RibbonSpinner(string text) => this.Text = text;

  /// <summary>The lowest value accepted. Raising it above <see cref="Maximum"/> drags the maximum along;
  /// the value re-clamps.</summary>
  public decimal Minimum {
    get => _minimum;
    set {
      if (_minimum == value)
        return;

      _minimum = value;
      if (_maximum < value)
        _maximum = value;

      this.SetValue(_value);
    }
  }

  /// <summary>The highest value accepted. Lowering it below <see cref="Minimum"/> drags the minimum
  /// along; the value re-clamps.</summary>
  public decimal Maximum {
    get => _maximum;
    set {
      if (_maximum == value)
        return;

      _maximum = value;
      if (_minimum > value)
        _minimum = value;

      this.SetValue(_value);
    }
  }

  /// <summary>The current value, always within [<see cref="Minimum"/>, <see cref="Maximum"/>] — an
  /// assignment outside it is clamped.</summary>
  public decimal Value {
    get => _value;
    set => this.SetValue(value);
  }

  /// <summary>The step an arrow or the Up/Down key changes the value by. Never negative.</summary>
  /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
  public decimal Increment {
    get => field;
    set {
      ArgumentOutOfRangeException.ThrowIfNegative(value);
      field = value;
    }
  } = 1m;

  /// <summary>The number of decimal digits shown (0–28); a typed entry is rounded to it.</summary>
  /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than 28.</exception>
  public int DecimalPlaces {
    get => _decimalPlaces;
    set {
      ArgumentOutOfRangeException.ThrowIfNegative(value);
      ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 28);
      if (_decimalPlaces == value)
        return;

      _decimalPlaces = value;
      _text = null;
      this.NotifyValueChanged();
    }
  }

  /// <summary>Raised when <see cref="Value"/> changes, by an arrow, a typed entry or assignment.</summary>
  public event EventHandler? ValueChanged;

  /// <summary>Raises <see cref="ValueChanged"/>.</summary>
  protected virtual void OnValueChanged(EventArgs e) => this.ValueChanged?.Invoke(this, e);

  /// <inheritdoc/>
  internal override string FieldText => _text ??= _value.ToString("F" + _decimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);

  /// <summary>Steps the value by <paramref name="steps"/> increments, clamped.</summary>
  internal void Step(int steps) => this.SetValue(_value + (steps * this.Increment));

  /// <summary>Whether a typed character may go into the box: a digit, the culture's decimal separator
  /// when decimals are shown, or a minus sign when the range reaches below zero.</summary>
  internal bool Accepts(char c) {
    if (char.IsAsciiDigit(c))
      return true;

    var format = CultureInfo.CurrentCulture.NumberFormat;
    if (_decimalPlaces > 0 && format.NumberDecimalSeparator.Length == 1 && c == format.NumberDecimalSeparator[0])
      return true;

    return _minimum < 0 && format.NegativeSign.Length == 1 && c == format.NegativeSign[0];
  }

  /// <summary>Commits a typed entry: parsed in the current culture, rounded to the places shown and
  /// clamped. Returns whether it parsed; an entry that did not leaves the value alone.</summary>
  internal bool TryCommit(string text) {
    if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed))
      return false;

    this.SetValue(Math.Round(parsed, _decimalPlaces, MidpointRounding.AwayFromZero));
    return true;
  }

  private void SetValue(decimal value) {
    var clamped = Math.Clamp(value, _minimum, _maximum);
    if (clamped == _value)
      return;

    _value = clamped;
    _text = null;
    this.NotifyValueChanged();
    this.OnValueChanged(EventArgs.Empty);
  }
}
