namespace Hawkynt.NativeForms;

/// <summary>
/// The spacing a layout container reserves around a control's outside edge, in pixels per side.
/// A 16-byte value type, so every control carries a <see cref="Control.Margin"/> without allocation.
/// </summary>
/// <param name="Left">The spacing left of the control.</param>
/// <param name="Top">The spacing above the control.</param>
/// <param name="Right">The spacing right of the control.</param>
/// <param name="Bottom">The spacing below the control.</param>
public readonly record struct Padding(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Creates a padding with the same spacing on every side.</summary>
    public Padding(int all) : this(all, all, all, all) { }

    /// <summary>The uniform spacing when all four sides agree, otherwise -1.</summary>
    public int All => this.Left == this.Top && this.Top == this.Right && this.Right == this.Bottom ? this.Left : -1;

    /// <summary>The combined horizontal spacing: <see cref="Left"/> + <see cref="Right"/>.</summary>
    public int Horizontal => this.Left + this.Right;

    /// <summary>The combined vertical spacing: <see cref="Top"/> + <see cref="Bottom"/>.</summary>
    public int Vertical => this.Top + this.Bottom;
}
