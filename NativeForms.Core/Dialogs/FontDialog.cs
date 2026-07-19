using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// The platform's native font picker. <see cref="Font"/> seeds the initial selection and carries the
/// chosen font after <see cref="DialogResult.OK"/>.
/// </summary>
public sealed class FontDialog : CommonDialog
{
    /// <inheritdoc cref="CommonDialog()"/>
    public FontDialog() { }

    /// <inheritdoc cref="CommonDialog(IPlatformBackend)"/>
    internal FontDialog(IPlatformBackend backend) : base(backend) { }

    /// <summary>The chosen font after OK; pre-selects the picker before.</summary>
    public Font Font { get; set; } = DefaultTheme.Instance.DefaultFont;

    /// <inheritdoc/>
    private protected override DialogResult RunDialog(IPlatformBackend backend)
    {
        var font = backend.ShowFontDialog(this.Font);
        if (font is null)
            return DialogResult.Cancel;

        this.Font = font.Value;
        return DialogResult.OK;
    }
}
