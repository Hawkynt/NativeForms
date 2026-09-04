namespace Hawkynt.NativeForms;

/// <summary>The syntactic class of a <see cref="CodeToken"/>, mapped to a colour by <see cref="CodeTextBox"/>.</summary>
public enum CodeTokenKind {
  /// <summary>Ordinary text, drawn in the control's foreground colour.</summary>
  Plain,

  /// <summary>A language keyword.</summary>
  Keyword,

  /// <summary>A string or character literal.</summary>
  String,

  /// <summary>A comment span.</summary>
  Comment,

  /// <summary>A numeric literal.</summary>
  Number,

  /// <summary>A type name.</summary>
  Type,
}

/// <summary>A coloured span within a single line, produced by a <see cref="CodeTextBox.Tokenizer"/>:
/// characters <c>[Start, Start+Length)</c> are drawn in the colour <see cref="Kind"/> maps to.</summary>
public readonly struct CodeToken(int start, int length, CodeTokenKind kind) {
  /// <summary>The zero-based character offset of the span within its line.</summary>
  public int Start { get; } = start;

  /// <summary>The span length in characters.</summary>
  public int Length { get; } = length;

  /// <summary>The span's syntactic class.</summary>
  public CodeTokenKind Kind { get; } = kind;
}
