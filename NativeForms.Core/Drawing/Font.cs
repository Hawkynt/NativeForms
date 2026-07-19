namespace Hawkynt.NativeForms.Drawing;

/// <summary>Font style flags, combinable like <c>System.Drawing.FontStyle</c>.</summary>
[Flags]
public enum FontStyle
{
    /// <summary>Normal weight, upright.</summary>
    Regular = 0,

    /// <summary>Bold weight.</summary>
    Bold = 1,

    /// <summary>Italic slant.</summary>
    Italic = 2,

    /// <summary>Underlined.</summary>
    Underline = 4,
}

/// <summary>
/// An immutable font descriptor — a family name, a point size and style flags. Deliberately a small
/// value type (not GDI+ <c>System.Drawing.Font</c>) so it stays allocation-free and AOT-safe; the
/// backend resolves it to a real native font when drawing.
/// </summary>
public readonly struct Font : IEquatable<Font>
{
    /// <summary>Creates a font descriptor.</summary>
    public Font(string family, float sizeInPoints, FontStyle style = FontStyle.Regular)
    {
        ArgumentException.ThrowIfNullOrEmpty(family);
        this.Family = family;
        this.SizeInPoints = sizeInPoints;
        this.Style = style;
    }

    /// <summary>The font family name (for example <c>"Segoe UI"</c>).</summary>
    public string Family { get; }

    /// <summary>The size in typographic points.</summary>
    public float SizeInPoints { get; }

    /// <summary>The style flags.</summary>
    public FontStyle Style { get; }

    /// <summary>Returns a copy with a different style.</summary>
    public Font WithStyle(FontStyle style) => new(this.Family, this.SizeInPoints, style);

    /// <inheritdoc/>
    public bool Equals(Font other)
        => this.SizeInPoints.Equals(other.SizeInPoints)
        && this.Style == other.Style
        && string.Equals(this.Family, other.Family, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Font other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Family, this.SizeInPoints, this.Style);

    public static bool operator ==(Font left, Font right) => left.Equals(right);
    public static bool operator !=(Font left, Font right) => !left.Equals(right);
}
