using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// Offers one cell of a <see cref="TreeListView"/> row to a caller before the control paints it
/// itself.
/// </summary>
/// <remarks>
/// The seam exists for the columns whose content is not text: an in-cell bar, a sparkline, a coloured
/// swatch. Set <see cref="Handled"/> to keep the control from drawing the cell's text over what you
/// drew; leave it false to add to the cell's background and let the text land on top.
/// </remarks>
public sealed class TreeListViewCellPaintEventArgs : EventArgs {
  /// <summary>Rebinds the reused instance. Callers never construct one.</summary>
  internal void Rebind(IGraphics graphics, ITheme theme, TreeNode node, int columnIndex, Rectangle bounds, bool selected) {
    this.Graphics = graphics;
    this.Theme = theme;
    this.Node = node;
    this.ColumnIndex = columnIndex;
    this.Bounds = bounds;
    this.Selected = selected;
    this.Handled = false;
  }

  /// <summary>The surface to draw on. Already clipped to <see cref="Bounds"/>.</summary>
  public IGraphics Graphics { get; private set; } = null!;

  /// <summary>The platform's theme, so a custom cell can match the rest of the control.</summary>
  public ITheme Theme { get; private set; } = null!;

  /// <summary>The row's node.</summary>
  public TreeNode Node { get; private set; } = null!;

  /// <summary>Which column, indexed into <see cref="TreeListView.Columns"/>.</summary>
  public int ColumnIndex { get; private set; }

  /// <summary>The cell's rectangle in control coordinates.</summary>
  public Rectangle Bounds { get; private set; }

  /// <summary>Whether the row is the selected one.</summary>
  public bool Selected { get; private set; }

  /// <summary>Set to true when the handler drew the cell and the control should not draw its text.</summary>
  public bool Handled { get; set; }
}
