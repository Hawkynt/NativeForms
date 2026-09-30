using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="TreeView.GetNodeAt(int, int)"/> maps a client point to the node on that row — the
/// query a drop target runs to learn which node the pointer is over. The row math is the one the
/// mouse-down path uses, so the node a press would select and the node a drop lands on agree.
/// </summary>
[TestFixture]
internal sealed class TreeViewGetNodeAtTests {
  private static void Realize(OwnerDrawnControl control) {
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(control);
    Application.Run(form, backend);
  }

  /// <summary>root0 (expanded: child00, child01), root1 — four visible rows.</summary>
  private static TreeView MakeTree() {
    var tree = new TreeView { Bounds = new(0, 0, 300, 220) };
    var root0 = tree.Nodes.Add("root0");
    root0.Nodes.Add("child00");
    root0.Nodes.Add("child01");
    tree.Nodes.Add("root1");
    root0.Expand();
    Realize(tree);
    return tree;
  }

  [Test]
  public void Given_expanded_tree_when_the_point_is_on_a_row_then_that_rows_node_is_returned() {
    var tree = MakeTree();
    var h = tree.ItemHeight;

    Assert.Multiple(() => {
      Assert.That(tree.GetNodeAt(10, 0)?.Text, Is.EqualTo("root0"), "first pixel of the first row");
      Assert.That(tree.GetNodeAt(10, h - 1)?.Text, Is.EqualTo("root0"), "last pixel of the first row");
      Assert.That(tree.GetNodeAt(10, h)?.Text, Is.EqualTo("child00"), "first pixel of the second row");
      Assert.That(tree.GetNodeAt(10, (2 * h) + (h / 2))?.Text, Is.EqualTo("child01"));
      Assert.That(tree.GetNodeAt(10, (4 * h) - 1)?.Text, Is.EqualTo("root1"), "last pixel of the last row");
    });
  }

  [Test]
  public void Given_expanded_tree_when_the_point_is_below_the_last_row_then_null_is_returned() {
    var tree = MakeTree();
    var h = tree.ItemHeight;

    Assert.Multiple(() => {
      Assert.That(tree.GetNodeAt(10, 4 * h), Is.Null, "first pixel below the last row");
      Assert.That(tree.GetNodeAt(10, 219), Is.Null, "bottom of the client area");
    });
  }

  [TestCase(-1, 5, TestName = "Given_tree_when_x_is_negative_then_null_is_returned")]
  [TestCase(10, -1, TestName = "Given_tree_when_y_is_negative_then_null_is_returned")]
  [TestCase(10, -5, TestName = "Given_tree_when_y_is_a_partial_row_above_the_top_then_null_is_returned")]
  [TestCase(300, 5, TestName = "Given_tree_when_x_is_the_client_width_then_null_is_returned")]
  [TestCase(10, 220, TestName = "Given_tree_when_y_is_the_client_height_then_null_is_returned")]
  public void Points_outside_the_client_area_hit_no_node(int x, int y)
      => Assert.That(MakeTree().GetNodeAt(x, y), Is.Null);

  [Test]
  public void Given_tree_when_x_is_the_last_client_column_then_the_row_node_is_returned()
      => Assert.That(MakeTree().GetNodeAt(299, 1)?.Text, Is.EqualTo("root0"), "the whole row width belongs to the node");

  [Test]
  public void Given_scrolled_tree_when_the_point_is_on_the_top_row_then_the_scrolled_to_node_is_returned() {
    var tree = new TreeView { Bounds = new(0, 0, 300, 100) };
    for (var i = 0; i < 50; ++i)
      tree.Nodes.Add("node" + i);

    Realize(tree);
    tree.SelectedNode = tree.Nodes[49]; // scrolls the last node into view

    Assert.That(tree.TopIndex, Is.GreaterThan(0), "precondition: the tree scrolled");
    Assert.Multiple(() => {
      Assert.That(tree.GetNodeAt(10, 0), Is.SameAs(tree.Nodes[tree.TopIndex]));
      Assert.That(tree.GetNodeAt(10, 0), Is.Not.SameAs(tree.Nodes[0]));
    });
  }

  [Test]
  public void Given_collapsed_parent_when_the_point_is_on_the_row_after_it_then_the_next_sibling_is_returned() {
    var tree = MakeTree();
    tree.Nodes[0].Collapse();

    Assert.That(tree.GetNodeAt(10, tree.ItemHeight)?.Text, Is.EqualTo("root1"), "hidden children occupy no rows");
  }

  [Test]
  public void Given_tree_when_queried_by_point_then_the_overload_agrees_with_the_coordinates() {
    var tree = MakeTree();
    var h = tree.ItemHeight;

    Assert.Multiple(() => {
      Assert.That(tree.GetNodeAt(new Point(10, h + 1)), Is.SameAs(tree.GetNodeAt(10, h + 1)));
      Assert.That(tree.GetNodeAt(new Point(10, 4 * h)), Is.Null);
    });
  }

  [Test]
  public void Given_empty_tree_when_queried_then_null_is_returned() {
    var tree = new TreeView { Bounds = new(0, 0, 300, 220) };
    Realize(tree);

    Assert.That(tree.GetNodeAt(10, 0), Is.Null);
  }
}
