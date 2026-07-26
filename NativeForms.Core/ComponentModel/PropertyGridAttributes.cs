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
