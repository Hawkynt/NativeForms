namespace Hawkynt.NativeForms;

/// <summary>How a <see cref="TextBox"/> normalizes the casing of assigned and typed text.</summary>
public enum CharacterCasing
{
    /// <summary>Text is kept exactly as entered.</summary>
    Normal,

    /// <summary>Text is converted to upper case.</summary>
    Upper,

    /// <summary>Text is converted to lower case.</summary>
    Lower,
}
