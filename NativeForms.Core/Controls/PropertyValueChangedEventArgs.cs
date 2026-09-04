namespace Hawkynt.NativeForms;

/// <summary>Carries a committed value change from a <see cref="PropertyGrid"/> row.</summary>
public sealed class PropertyValueChangedEventArgs(PropertyGridRow row, string oldValue, string newValue) : EventArgs {
  /// <summary>The row whose value changed.</summary>
  public PropertyGridRow Row { get; } = row;

  /// <summary>The value before the edit.</summary>
  public string OldValue { get; } = oldValue;

  /// <summary>The value after the edit.</summary>
  public string NewValue { get; } = newValue;
}
