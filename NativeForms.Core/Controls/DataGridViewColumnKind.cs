namespace Hawkynt.NativeForms;

/// <summary>
/// How a <see cref="DataGridViewColumn"/> renders its cells. One column class serves every kind — the
/// kind only picks the painter and the click behavior, so the paint path stays a single allocation-free
/// switch instead of a per-type class hierarchy.
/// </summary>
public enum DataGridViewColumnKind
{
    /// <summary>Plain text from <see cref="DataGridViewColumn.ValueSelector"/>, with an optional icon
    /// before the text when <see cref="DataGridViewColumn.ImageSelector"/> is set.</summary>
    Text,

    /// <summary>A themed check glyph driven by <see cref="DataGridViewColumn.CheckedSelector"/>;
    /// clicking toggles through <see cref="DataGridViewColumn.CheckedSetter"/> unless the cell is
    /// read-only.</summary>
    Check,

    /// <summary>A themed button face with per-cell text; <see cref="DataGridViewColumn.EnabledSelector"/>
    /// greys the text and suppresses the content-click event.</summary>
    Button,

    /// <summary>Accent-colored, underlined text that raises <see cref="DataGridView.CellContentClick"/>
    /// on click.</summary>
    Link,

    /// <summary>Several icons side by side from <see cref="DataGridViewColumn.ImagesSelector"/>; each
    /// icon is hit-tested individually and reported via
    /// <see cref="DataGridViewCellEventArgs.ContentIndex"/>.</summary>
    MultiImage,

    /// <summary>A themed progress fill (0..100) driven by
    /// <see cref="DataGridViewColumn.ProgressSelector"/>.</summary>
    Progress,
}
