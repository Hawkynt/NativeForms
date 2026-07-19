using System.Drawing;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// The platform's native color picker. <see cref="Color"/> seeds the initial selection and carries
/// the chosen color after <see cref="DialogResult.OK"/>.
/// </summary>
public sealed class ColorDialog : CommonDialog
{
    /// <inheritdoc cref="CommonDialog()"/>
    public ColorDialog() { }

    /// <inheritdoc cref="CommonDialog(IPlatformBackend)"/>
    internal ColorDialog(IPlatformBackend backend) : base(backend) { }

    /// <summary>The chosen color after OK; pre-selects the picker before.</summary>
    public Color Color { get; set; } = Color.Black;

    /// <inheritdoc/>
    private protected override DialogResult RunDialog(IPlatformBackend backend)
    {
        var color = backend.ShowColorDialog(this.Color);
        if (color is null)
            return DialogResult.Cancel;

        this.Color = color.Value;
        return DialogResult.OK;
    }
}
