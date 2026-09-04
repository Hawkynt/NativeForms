using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The three seams that let a caller take part in how a row looks and behaves:
/// <see cref="TreeListView.RowBackColorSelector"/>, <see cref="TreeListView.CellPaint"/> and
/// <see cref="TreeListView.ColumnClick"/>.
/// </summary>
/// <remarks>
/// They exist because a process list needs all three — a row coloured by what kind of process it is,
/// a column whose content is a sparkline rather than text, and a header that sorts when clicked —
/// and none of them could be expressed without subclassing the control.
/// </remarks>
[TestFixture]
internal sealed class TreeListViewRowPaintTests {
  private static HeadlessCanvasPeer Realize(OwnerDrawnControl control) {
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(control);
    Application.Run(form, backend);
    return backend.Created.OfType<HeadlessCanvasPeer>().Single();
  }

  private static TreeListView Build() {
    var tree = new TreeListView { Bounds = new(0, 0, 300, 220), ShowColumnHeaders = true };
    tree.Columns.Add(new TreeListViewColumn("Name", 160));
    tree.Columns.Add(new TreeListViewColumn("Size", 80, node => (string)node.Tag!));
    tree.Nodes.Add(new TreeNode("alpha") { Tag = "1 kB" });
    tree.Nodes.Add(new TreeNode("beta") { Tag = "2 kB" });
    return tree;
  }

  [Test]
  public void A_row_back_color_selector_fills_the_row_behind_the_text() {
    var tree = Build();
    tree.RowBackColorSelector = node => node.Text == "alpha" ? Color.FromArgb(0xFF, 0x00, 0x80, 0x40) : null;

    var operations = Realize(tree).RaisePaint().Operations;

    Assert.That(operations.Any(o => o.StartsWith("fill #FF008040 0,")), Is.True, "the coloured row was filled");
    Assert.That(operations.Count(o => o.StartsWith("fill #FF008040")), Is.EqualTo(1), "only the row that asked for it");
  }

  [Test]
  public void A_row_fore_color_selector_colors_that_row_s_text() {
    var tree = Build();
    tree.RowForeColorSelector = node => node.Text == "beta" ? Color.FromArgb(0xFF, 0xC0, 0x00, 0x00) : null;

    var operations = Realize(tree).RaisePaint().Operations;

    Assert.That(operations.Any(o => o.StartsWith("text \"beta\" #FFC00000")), Is.True);
    Assert.That(operations.Any(o => o.StartsWith("text \"alpha\" #FFC00000")), Is.False, "the other row keeps the theme colour");
  }

  [Test]
  public void Selection_still_wins_over_a_row_color() {
    // A selection some rows swallow is worse than an uncoloured row: the user would lose track of
    // where they are.
    var tree = Build();
    tree.RowBackColorSelector = _ => Color.FromArgb(0xFF, 0x00, 0x80, 0x40);
    tree.SelectedNode = tree.Nodes[0];

    var operations = Realize(tree).RaisePaint().Operations;

    Assert.That(operations.Count(o => o.StartsWith("fill #FF008040")), Is.EqualTo(1), "only the unselected row is coloured");
  }

  [Test]
  public void CellPaint_is_offered_every_cell_of_every_visible_row() {
    var tree = Build();
    var seen = new List<(string Node, int Column)>();
    tree.CellPaint += (_, e) => seen.Add((e.Node.Text, e.ColumnIndex));

    Realize(tree).RaisePaint();

    Assert.That(seen, Does.Contain(("alpha", 0)));
    Assert.That(seen, Does.Contain(("alpha", 1)));
    Assert.That(seen, Does.Contain(("beta", 0)));
    Assert.That(seen, Does.Contain(("beta", 1)));
  }

  [Test]
  public void A_handled_cell_suppresses_the_text_the_control_would_have_drawn() {
    var tree = Build();
    tree.CellPaint += (_, e) => {
      if (e.ColumnIndex != 1)
        return;

      e.Graphics.FillRectangle(Color.FromArgb(0xFF, 0x10, 0x20, 0x30), e.Bounds);
      e.Handled = true;
    };

    var operations = Realize(tree).RaisePaint().Operations;

    Assert.That(operations.Any(o => o.StartsWith("fill #FF102030")), Is.True, "the handler drew");
    Assert.That(operations.Any(o => o.StartsWith("text \"1 kB\"")), Is.False, "and the cell's text was not drawn over it");
    Assert.That(operations.Any(o => o.StartsWith("text \"alpha\"")), Is.True, "other columns are untouched");
  }

  [Test]
  public void An_unhandled_cell_paints_underneath_the_text() {
    var tree = Build();
    tree.CellPaint += (_, e) => e.Graphics.FillRectangle(Color.FromArgb(0xFF, 0x10, 0x20, 0x30), e.Bounds);

    var operations = Realize(tree).RaisePaint().Operations;

    Assert.That(operations.Any(o => o.StartsWith("fill #FF102030")), Is.True);
    Assert.That(operations.Any(o => o.StartsWith("text \"1 kB\"")), Is.True, "the text still lands on top");
  }

  [Test]
  public void The_cell_bounds_match_the_column_the_handler_was_told_about() {
    var tree = Build();
    var bounds = new Dictionary<int, Rectangle>();
    tree.CellPaint += (_, e) => bounds[e.ColumnIndex] = e.Bounds;

    Realize(tree).RaisePaint();

    Assert.That(bounds[0].X, Is.EqualTo(0));
    Assert.That(bounds[0].Width, Is.EqualTo(160));
    Assert.That(bounds[1].X, Is.EqualTo(160), "the second cell starts where the first ends");
    Assert.That(bounds[1].Width, Is.EqualTo(80));
  }

  [Test]
  public void Clicking_a_header_reports_which_column() {
    var tree = Build();
    var clicked = new List<int>();
    tree.ColumnClick += (_, e) => clicked.Add(e.Column);

    var canvas = Realize(tree);
    canvas.RaiseMouseDown(20, 2);                    // inside the first header cell
    canvas.RaiseMouseDown(200, 2);                   // inside the second

    Assert.That(clicked, Is.EqualTo(new[] { 0, 1 }));
  }

  [Test]
  public void Clicking_past_the_last_column_reports_nothing() {
    var tree = Build();
    var clicked = 0;
    tree.ColumnClick += (_, _) => ++clicked;

    Realize(tree).RaiseMouseDown(290, 2);            // right of both columns

    Assert.That(clicked, Is.Zero);
  }

  [Test]
  public void Clicking_a_row_is_not_a_column_click() {
    var tree = Build();
    var clicked = 0;
    tree.ColumnClick += (_, _) => ++clicked;

    var canvas = Realize(tree);
    canvas.RaiseMouseDown(20, 40);            // well below any header

    Assert.That(clicked, Is.Zero);
    Assert.That(tree.SelectedNode, Is.Not.Null, "it selected a row instead");
  }
}
