using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// Marks a class the <c>NativeForms.Generators</c> source generator turns into a
/// <c>PopulateGrid(PropertyGrid)</c> method: it emits one <see cref="PropertyGrid"/> row per public,
/// settable property, inferring the editor from the property type and reading category / description /
/// range from the attributes below — all at compile time, so no runtime reflection is used. The class must
/// be declared <see langword="partial"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GridEditableAttribute : Attribute;

/// <summary>The category header a generated row groups under.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class GridCategoryAttribute(string category) : Attribute
{
    /// <summary>The category name.</summary>
    public string Category { get; } = category;
}

/// <summary>The description a generated row shows in the grid's description strip.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class GridDescriptionAttribute(string description) : Attribute
{
    /// <summary>The description text.</summary>
    public string Description { get; } = description;
}

/// <summary>The inclusive numeric bounds a generated number row clamps to.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class GridRangeAttribute(double minimum, double maximum) : Attribute
{
    /// <summary>The inclusive lower bound.</summary>
    public double Minimum { get; } = minimum;

    /// <summary>The inclusive upper bound.</summary>
    public double Maximum { get; } = maximum;
}

/// <summary>Overrides the displayed name of a generated row (otherwise the property name is used).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class GridDisplayNameAttribute(string name) : Attribute
{
    /// <summary>The row's display name.</summary>
    public string Name { get; } = name;
}

/// <summary>Excludes a property from the generated grid.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class GridIgnoreAttribute : Attribute;

// --- Grid-column attributes ----------------------------------------------------------------------
//
// These add the concerns a DataGridView has and a PropertyGrid does not (width, sort, per-row rules).
// Following the WindowsFormsExtensions convention, a dynamic rule is expressed as the *name* of another
// member on the model rather than a delegate — but the generator resolves those names at compile time,
// so a typo is a build error instead of a silent no-op.

/// <summary>The pixel width a generated <see cref="DataGridView"/> column starts at.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GridColumnWidthAttribute(int width) : Attribute
{
    /// <summary>The column width in pixels.</summary>
    public int Width { get; } = width;
}

/// <summary>Overrides the column kind the generator would infer from the property type.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GridColumnKindAttribute(DataGridViewColumnKind kind) : Attribute
{
    /// <summary>The column kind to use.</summary>
    public DataGridViewColumnKind Kind { get; } = kind;
}

/// <summary>
/// Draws the image the named property yields in this column's cells, beside the text.
/// </summary>
/// <remarks>
/// The property must be a readable public one returning an <see cref="Drawing.IImage"/>; returning
/// <see langword="null"/> for a row simply leaves that cell without an icon. Pair it with
/// <see cref="GridColumnTextImageRelationAttribute"/> to choose which side the icon sits on, and with
/// <see cref="GridColumnImageSizeAttribute"/> to fix the box it is drawn into.
/// </remarks>
/// <param name="propertyName">The name of the property producing the image.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GridColumnImageAttribute(string propertyName) : Attribute
{
    /// <summary>The name of the property producing the image.</summary>
    public string PropertyName { get; } = propertyName;
}

/// <summary>
/// Draws the several images the named property yields side by side in this column's cells — the strip
/// form, for badges or a rating.
/// </summary>
/// <remarks>The property must return a readable list of <see cref="Drawing.IImage"/>.</remarks>
/// <param name="propertyName">The name of the property producing the images.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GridColumnImagesAttribute(string propertyName) : Attribute
{
    /// <summary>The name of the property producing the images.</summary>
    public string PropertyName { get; } = propertyName;
}

/// <summary>
/// Stacks the images the named property yields over this column's cells as badges.
/// </summary>
/// <remarks>
/// The property returns only the badges that currently apply, so several conditions compose without the
/// attribute needing to be stackable itself.
/// </remarks>
/// <param name="propertyName">The name of the property producing the overlays.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GridColumnOverlayImagesAttribute(string propertyName) : Attribute
{
    /// <summary>The name of the property producing the overlay badges.</summary>
    public string PropertyName { get; } = propertyName;
}

/// <summary>Fixes the box a generated column draws its image into.</summary>
/// <param name="width">The box width in pixels.</param>
/// <param name="height">The box height in pixels.</param>
/// <param name="keepAspectRatio">Whether to letterbox the image rather than stretch it.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GridColumnImageSizeAttribute(int width, int height, bool keepAspectRatio = true) : Attribute
{
    /// <summary>The box width in pixels.</summary>
    public int Width { get; } = width;

    /// <summary>The box height in pixels.</summary>
    public int Height { get; } = height;

    /// <summary>Whether the image is letterboxed into the box rather than stretched to it.</summary>
    public bool KeepAspectRatio { get; } = keepAspectRatio;
}

/// <summary>Places a generated column's image relative to its text.</summary>
/// <param name="relation">Where the image sits.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GridColumnTextImageRelationAttribute(TextImageRelation relation) : Attribute
{
    /// <summary>Where the image sits relative to the text.</summary>
    public TextImageRelation Relation { get; } = relation;
}

/// <summary>Makes a generated column's cells read-only while the named <see cref="bool"/> property on
/// the row model is <see langword="true"/>.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GridColumnReadOnlyWhenAttribute(string propertyName) : Attribute
{
    /// <summary>The name of the <see cref="bool"/> property that gates the read-only state.</summary>
    public string PropertyName { get; } = propertyName;
}

/// <summary>Sorts a generated column; <see cref="DataGridViewColumnSortMode.Automatic"/> makes its
/// header clickable.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GridColumnSortModeAttribute(DataGridViewColumnSortMode sortMode) : Attribute
{
    /// <summary>The sort mode to apply.</summary>
    public DataGridViewColumnSortMode SortMode { get; } = sortMode;
}

/// <summary>Hides a row while the named <see cref="bool"/> property on the row model is
/// <see langword="true"/>. Wired to <see cref="DataGridView.RowHiddenSelector"/>.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GridRowHiddenWhenAttribute(string propertyName) : Attribute
{
    /// <summary>The name of the gating <see cref="bool"/> property.</summary>
    public string PropertyName { get; } = propertyName;
}

/// <summary>Allows a row to be selected only while the named <see cref="bool"/> property on the row
/// model is <see langword="true"/>. Wired to <see cref="DataGridView.RowSelectableSelector"/>.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GridRowSelectableWhenAttribute(string propertyName) : Attribute
{
    /// <summary>The name of the gating <see cref="bool"/> property.</summary>
    public string PropertyName { get; } = propertyName;
}

/// <summary>Takes a row's pixel height from the named <see cref="int"/> property on the row model.
/// Wired to <see cref="DataGridView.RowHeightSelector"/>.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GridRowHeightFromAttribute(string propertyName) : Attribute
{
    /// <summary>The name of the <see cref="int"/> property supplying the height.</summary>
    public string PropertyName { get; } = propertyName;
}
