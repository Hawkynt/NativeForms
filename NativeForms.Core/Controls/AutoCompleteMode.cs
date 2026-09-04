namespace Hawkynt.NativeForms;

/// <summary>How an editable <see cref="ComboBox"/> completes what is typed against its items.</summary>
[Flags]
public enum AutoCompleteMode {
  /// <summary>No completion: the field holds exactly what was typed.</summary>
  None = 0,

  /// <summary>The drop-down narrows to the items matching what has been typed so far.</summary>
  Suggest = 1,

  /// <summary>
  /// The rest of the first matching item is filled into the field and left selected, so the next
  /// keystroke replaces it. Deleting never completes, or the text could not be shortened.
  /// </summary>
  Append = 2,

  /// <summary>Both — the drop-down narrows and the field completes.</summary>
  SuggestAppend = Suggest | Append,
}
