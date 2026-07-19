using System.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>The border a control paints around itself.</summary>
public enum BorderStyle
{
    /// <summary>No border.</summary>
    None,

    /// <summary>A single flat line.</summary>
    FixedSingle,

    /// <summary>A sunken 3-D edge.</summary>
    Fixed3D,
}

/// <summary>
/// A simple owner-drawn container that fills itself with the theme's control background and optionally
/// draws a border. A grouping surface for other controls.
/// </summary>
public class Panel : OwnerDrawnControl
{
    private BorderStyle _borderStyle = BorderStyle.None;

    /// <summary>The border drawn around the panel.</summary>
    public BorderStyle BorderStyle
    {
        get => _borderStyle;
        set
        {
            if (_borderStyle == value)
                return;

            _borderStyle = value;
            this.Invalidate();
        }
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var full = new Rectangle(0, 0, this.Width, this.Height);
        g.FillRectangle(this.Theme.ControlBackground, full);

        switch (_borderStyle)
        {
            case BorderStyle.FixedSingle:
                g.DrawRectangle(this.Theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
                break;
            case BorderStyle.Fixed3D:
                g.DrawLine(this.Theme.Border, 0, 0, this.Width - 1, 0);
                g.DrawLine(this.Theme.Border, 0, 0, 0, this.Height - 1);
                break;
            case BorderStyle.None:
            default:
                break;
        }
    }
}
