namespace Hawkynt.NativeForms;

/// <summary>The state of a <see cref="CheckBox"/>, which is three-valued once
/// <see cref="CheckBox.ThreeState"/> is on.</summary>
public enum CheckState {
  /// <summary>Off.</summary>
  Unchecked,

  /// <summary>On.</summary>
  Checked,

  /// <summary>
  /// Neither: the mixed state a box shows for a set whose members disagree — some files read-only,
  /// some not. <see cref="CheckBox.Checked"/> reads <see langword="true"/> here, matching Windows
  /// Forms, so code that only asks the boolean question treats "mixed" as "on" rather than silently
  /// losing it.
  /// </summary>
  Indeterminate,
}
