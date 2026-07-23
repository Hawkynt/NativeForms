using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class TreeViewTests
{
    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    /// <summary>root0(child00(leaf000), child01), root1 — 5 nodes, all collapsed.</summary>
    private static TreeView MakeTree()
    {
        var tree = new TreeView { Bounds = new(0, 0, 300, 220) };
        var root0 = tree.Nodes.Add("root0");
        var child00 = root0.Nodes.Add("child00");
        child00.Nodes.Add("leaf000");
        root0.Nodes.Add("child01");
        tree.Nodes.Add("root1");
        return tree;
    }

    [Test]
    public void Nodes_are_addable_before_realization_and_collapsed_by_default()
    {
        var tree = MakeTree();

        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes.Count, Is.EqualTo(2));
            Assert.That(tree.Nodes[0].Nodes.Count, Is.EqualTo(2));
            Assert.That(tree.Nodes[0].IsExpanded, Is.False);
            Assert.That(tree.VisibleNodeCount, Is.EqualTo(2), "only roots are visible while collapsed");
        });
    }

    [Test]
    public void Node_levels_and_parents_follow_the_hierarchy()
    {
        var tree = MakeTree();
        var child00 = tree.Nodes[0].Nodes[0];

        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[0].Level, Is.Zero);
            Assert.That(child00.Level, Is.EqualTo(1));
            Assert.That(child00.Nodes[0].Level, Is.EqualTo(2));
            Assert.That(child00.Parent, Is.SameAs(tree.Nodes[0]));
            Assert.That(tree.Nodes[0].Parent, Is.Null);
        });
    }

    [Test]
    public void Expand_reveals_children_and_collapse_hides_the_whole_subtree()
    {
        var tree = MakeTree();

        tree.Nodes[0].Expand();
        Assert.That(tree.VisibleNodeCount, Is.EqualTo(4), "root0 + its two children + root1");

        tree.Nodes[0].Nodes[0].Expand();
        Assert.That(tree.VisibleNodeCount, Is.EqualTo(5), "leaf000 now visible too");

        tree.Nodes[0].Collapse();
        Assert.That(tree.VisibleNodeCount, Is.EqualTo(2), "collapsing the root hides the expanded grandchild as well");
    }

    [Test]
    public void Mutating_a_collection_reflattens_and_repaints()
    {
        var tree = MakeTree();
        tree.Nodes[0].Expand();
        var canvas = Realize(tree);
        var before = canvas.InvalidateCount;

        tree.Nodes[0].Nodes.Add("child02");

        Assert.Multiple(() =>
        {
            Assert.That(tree.VisibleNodeCount, Is.EqualTo(5));
            Assert.That(canvas.InvalidateCount, Is.GreaterThan(before));
        });

        tree.Nodes[0].Nodes.Clear();
        Assert.That(tree.VisibleNodeCount, Is.EqualTo(2));
    }

    [Test]
    public void BeforeExpand_cancel_keeps_the_node_collapsed_and_suppresses_AfterExpand()
    {
        var tree = MakeTree();
        var after = 0;
        tree.BeforeExpand += (_, e) => e.Cancel = true;
        tree.AfterExpand += (_, _) => ++after;

        tree.Nodes[0].Expand();

        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[0].IsExpanded, Is.False);
            Assert.That(after, Is.Zero);
        });
    }

    [Test]
    public void Expand_raises_Before_then_After_with_the_node()
    {
        var tree = MakeTree();
        TreeNode? beforeNode = null, afterNode = null;
        tree.BeforeExpand += (_, e) => beforeNode = e.Node;
        tree.AfterExpand += (_, e) => afterNode = e.Node;

        tree.Nodes[0].Expand();

        Assert.Multiple(() =>
        {
            Assert.That(beforeNode, Is.SameAs(tree.Nodes[0]));
            Assert.That(afterNode, Is.SameAs(tree.Nodes[0]));
            Assert.That(tree.Nodes[0].IsExpanded, Is.True);
        });
    }

    [Test]
    public void BeforeCollapse_cancel_keeps_the_node_expanded()
    {
        var tree = MakeTree();
        tree.Nodes[0].Expand();
        var after = 0;
        tree.BeforeCollapse += (_, e) => e.Cancel = true;
        tree.AfterCollapse += (_, _) => ++after;

        tree.Nodes[0].Collapse();

        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[0].IsExpanded, Is.True);
            Assert.That(after, Is.Zero);
        });
    }

    [Test]
    public void Click_on_a_row_selects_it_and_raises_AfterSelect()
    {
        var tree = MakeTree();
        TreeNode? selected = null;
        tree.AfterSelect += (_, e) => selected = e.Node;
        var canvas = Realize(tree);

        canvas.RaiseMouseDown(150, 30); // second visible row (22..44) = root1, far from any glyph

        Assert.Multiple(() =>
        {
            Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[1]));
            Assert.That(selected, Is.SameAs(tree.Nodes[1]));
        });
    }

    [Test]
    public void Click_on_the_expand_glyph_toggles_without_selecting()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);

        canvas.RaiseMouseDown(11, 11); // glyph cell of root0 (first indent cell, 0..22)

        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[0].IsExpanded, Is.True);
            Assert.That(tree.SelectedNode, Is.Null, "glyph clicks expand, they do not select");
        });

        canvas.RaiseMouseDown(11, 11);
        Assert.That(tree.Nodes[0].IsExpanded, Is.False);
    }

    [Test]
    public void Double_click_on_a_row_toggles_expansion()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);

        canvas.RaiseMouseDown(150, 11); // select root0
        canvas.RaiseMouseDown(150, 11); // immediate second click = double-click

        Assert.That(tree.Nodes[0].IsExpanded, Is.True);
    }

    [Test]
    public void Double_click_detection_honors_the_theme_interval()
    {
        var backend = new HeadlessBackend
        {
            // An impossible interval: even two clicks in the same millisecond miss it, proving the
            // control reads the theme's user setting instead of a hard-coded 500 ms.
            Theme = new StubTheme { DoubleClickTime = -1 },
        };
        var form = new Form();
        var tree = MakeTree();
        form.Controls.Add(tree);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        canvas.RaiseMouseDown(150, 11);
        canvas.RaiseMouseDown(150, 11);

        Assert.That(tree.Nodes[0].IsExpanded, Is.False, "no interval, no double-click");
    }

    [Test]
    public void Arrow_keys_walk_the_visible_rows()
    {
        var tree = MakeTree();
        tree.Nodes[0].Expand();
        var canvas = Realize(tree);

        canvas.RaiseKeyDown(Keys.Down); // none -> root0
        canvas.RaiseKeyDown(Keys.Down); // root0 -> child00
        Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[0].Nodes[0]));

        canvas.RaiseKeyDown(Keys.Up);
        Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[0]));

        canvas.RaiseKeyDown(Keys.End);
        Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[1]), "End selects the last visible row");

        canvas.RaiseKeyDown(Keys.Home);
        Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[0]));
    }

    [Test]
    public void Right_expands_then_moves_into_the_first_child()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);
        tree.SelectedNode = tree.Nodes[0];

        canvas.RaiseKeyDown(Keys.Right);
        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[0].IsExpanded, Is.True, "first Right expands");
            Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[0]), "selection stays put on expand");
        });

        canvas.RaiseKeyDown(Keys.Right);
        Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[0].Nodes[0]), "second Right enters the child");
    }

    [Test]
    public void Left_collapses_then_moves_to_the_parent()
    {
        var tree = MakeTree();
        tree.Nodes[0].Expand();
        var canvas = Realize(tree);
        tree.SelectedNode = tree.Nodes[0].Nodes[0];

        canvas.RaiseKeyDown(Keys.Left);
        Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[0]), "Left on a leafish node jumps to the parent");

        canvas.RaiseKeyDown(Keys.Left);
        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[0].IsExpanded, Is.False, "Left on an expanded node collapses it");
            Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[0]));
        });
    }

    [Test]
    public void Plus_minus_and_star_keys_expand_and_collapse()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);
        tree.SelectedNode = tree.Nodes[0];

        canvas.RaiseKeyDown(Keys.Add);
        Assert.That(tree.Nodes[0].IsExpanded, Is.True);

        canvas.RaiseKeyDown(Keys.Subtract);
        Assert.That(tree.Nodes[0].IsExpanded, Is.False);

        canvas.RaiseKeyDown(Keys.Multiply);
        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[0].IsExpanded, Is.True, "* expands the node");
            Assert.That(tree.Nodes[0].Nodes[0].IsExpanded, Is.True, "* expands the whole subtree");
            Assert.That(tree.VisibleNodeCount, Is.EqualTo(5));
        });
    }

    [Test]
    public void Check_click_toggles_and_raises_AfterCheck_without_selecting()
    {
        var tree = MakeTree();
        tree.CheckBoxes = true;
        TreeNode? checkedNode = null;
        tree.AfterCheck += (_, e) => checkedNode = e.Node;
        var canvas = Realize(tree);

        canvas.RaiseMouseDown(26, 11); // check cell of root0 (after the 22px glyph cell)

        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[0].Checked, Is.True);
            Assert.That(checkedNode, Is.SameAs(tree.Nodes[0]));
            Assert.That(tree.SelectedNode, Is.Null, "check clicks do not select");
        });
    }

    [Test]
    public void Space_toggles_the_check_when_checkboxes_are_on()
    {
        var tree = MakeTree();
        tree.CheckBoxes = true;
        var canvas = Realize(tree);
        tree.SelectedNode = tree.Nodes[1];

        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(tree.Nodes[1].Checked, Is.True);

        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(tree.Nodes[1].Checked, Is.False);
    }

    [Test]
    public void Paint_indents_child_rows_one_cell_deeper_than_their_parents()
    {
        var tree = MakeTree();
        tree.Nodes[0].Expand();
        var canvas = Realize(tree);

        var g = canvas.RaisePaint();

        var rootText = g.Operations.Single(o => o.StartsWith("text ") && o.Contains("root0"));
        var childText = g.Operations.Single(o => o.StartsWith("text ") && o.Contains("child00"));
        var rootX = int.Parse(rootText[(rootText.LastIndexOf('@') + 1)..].Split(',')[0]);
        var childX = int.Parse(childText[(childText.LastIndexOf('@') + 1)..].Split(',')[0]);
        Assert.That(childX, Is.GreaterThan(rootX), "children start further right than their parents");
    }

    [Test]
    public void Paint_draws_a_glyph_only_for_nodes_with_children()
    {
        var tree = new TreeView { Bounds = new(0, 0, 300, 220) };
        tree.Nodes.Add("parent").Nodes.Add("kid");
        tree.Nodes.Add("leaf");
        var canvas = Realize(tree);

        var g = canvas.RaisePaint();

        // Exactly one expand glyph box: the parent's. Leaves get none.
        var glyphs = g.Operations.Count(o => o.StartsWith("rect ") && o.EndsWith(",9,9"));
        Assert.That(glyphs, Is.EqualTo(1));
    }

    [Test]
    public void Paint_draws_the_node_image_when_an_imagelist_is_set()
    {
        using var images = new ImageList(new Size(4, 4));
        var index = images.Add(new int[16]);
        var tree = new TreeView { Bounds = new(0, 0, 300, 220), ImageList = images };
        tree.Nodes.Add("pictured").ImageIndex = index;
        tree.Nodes.Add("plain");
        var canvas = Realize(tree);

        var g = canvas.RaisePaint();

        var imageOps = g.Operations.Count(o => o.StartsWith("image "));
        Assert.That(imageOps, Is.EqualTo(1), "only the node with an image index paints an image");
    }

    [Test]
    public void Paint_highlights_the_selected_row_with_theme_selection_colors()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);
        tree.SelectedNode = tree.Nodes[1];

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Any(o => o.StartsWith("fill #FF0078D4")), Is.True, "selection background");
            Assert.That(g.Operations.Any(o => o.StartsWith("text \"root1\" #FFFFFFFF")), Is.True, "selection text color");
        });
    }

    [Test]
    public void Painting_is_virtualized_for_very_large_trees()
    {
        var tree = new TreeView { Bounds = new(0, 0, 200, 220) };
        var nodes = new TreeNode[100000];
        for (var i = 0; i < nodes.Length; ++i)
            nodes[i] = new TreeNode("Node" + i);

        tree.Nodes.AddRange(nodes);
        var canvas = Realize(tree);

        var g = canvas.RaisePaint();

        var textOps = g.Operations.Count(o => o.StartsWith("text "));
        Assert.That(textOps, Is.LessThan(50), "only the visible rows are painted");
    }

    [Test]
    public void Wheel_scrolling_changes_the_painted_window()
    {
        var tree = new TreeView { Bounds = new(0, 0, 200, 110) }; // 5 rows at 22px
        for (var i = 0; i < 40; ++i)
            tree.Nodes.Add("Node" + i);

        var canvas = Realize(tree);
        Assert.That(canvas.RaisePaint().DrewText("Node0"), Is.True);

        canvas.RaiseMouseWheel(-120);

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(tree.TopIndex, Is.EqualTo(3));
            Assert.That(g.DrewText("Node0"), Is.False, "scrolled off the top");
            Assert.That(g.DrewText("Node3"), Is.True);
        });
    }

    [Test]
    public void EnsureVisible_expands_ancestors_and_scrolls_to_the_node()
    {
        var tree = new TreeView { Bounds = new(0, 0, 200, 110) }; // 5 rows at 22px
        for (var i = 0; i < 30; ++i)
            tree.Nodes.Add("Filler" + i);

        var deep = tree.Nodes[29].Nodes.Add("hidden").Nodes.Add("target");
        Realize(tree);

        deep.EnsureVisible();

        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[29].IsExpanded, Is.True);
            Assert.That(tree.Nodes[29].Nodes[0].IsExpanded, Is.True);
            Assert.That(tree.TopIndex, Is.GreaterThanOrEqualTo(27), "scrolled so the deep node is inside the window");
        });
    }

    [Test]
    public void Collapsing_an_ancestor_of_the_selected_node_selects_the_ancestor()
    {
        var tree = MakeTree();
        tree.Nodes[0].Expand();
        tree.Nodes[0].Nodes[0].Expand();
        tree.SelectedNode = tree.Nodes[0].Nodes[0].Nodes[0]; // leaf000

        tree.Nodes[0].Collapse();

        Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[0]), "selection may not vanish into a hidden row");
    }

    [Test]
    public void Removing_the_selected_subtree_clears_the_selection()
    {
        var tree = MakeTree();
        tree.Nodes[0].Expand();
        tree.SelectedNode = tree.Nodes[0].Nodes[0];

        tree.Nodes.Remove(tree.Nodes[0]);

        Assert.That(tree.SelectedNode, Is.Null);
    }

    [Test]
    public void BeforeSelect_cancel_keeps_the_previous_selection_on_every_path()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);
        tree.SelectedNode = tree.Nodes[0];

        var after = 0;
        tree.AfterSelect += (_, _) => ++after;
        tree.BeforeSelect += (_, e) => e.Cancel = ReferenceEquals(e.Node, tree.Nodes[1]);

        tree.SelectedNode = tree.Nodes[1]; // assignment path
        Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[0]));

        canvas.RaiseMouseDown(150, 33); // mouse path: row 1 = root1
        Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[0]));

        canvas.RaiseKeyDown(Keys.Down); // keyboard path
        Assert.Multiple(() =>
        {
            Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[0]));
            Assert.That(after, Is.Zero, "vetoed selections raise no AfterSelect");
        });
    }

    [Test]
    public void BeforeCheck_cancel_keeps_the_check_state_and_suppresses_AfterCheck()
    {
        var tree = MakeTree();
        tree.CheckBoxes = true;
        var after = 0;
        tree.BeforeCheck += (_, e) => e.Cancel = true;
        tree.AfterCheck += (_, _) => ++after;

        tree.Nodes[0].Checked = true;

        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[0].Checked, Is.False);
            Assert.That(after, Is.Zero);
        });
    }

    [Test]
    public void ExpandAll_and_CollapseAll_walk_the_whole_tree()
    {
        var tree = MakeTree();

        tree.ExpandAll();
        Assert.That(tree.VisibleNodeCount, Is.EqualTo(5), "every node is visible");

        tree.CollapseAll();
        Assert.Multiple(() =>
        {
            Assert.That(tree.VisibleNodeCount, Is.EqualTo(2), "only roots remain");
            Assert.That(tree.Nodes[0].Nodes[0].IsExpanded, Is.False, "descendants folded too");
        });
    }

    // --- Lazy child population (SetChildLoader) ------------------------------------------------

    [Test]
    public void A_node_with_a_loader_paints_as_expandable_before_it_is_populated()
    {
        var tree = new TreeView { Bounds = new(0, 0, 200, 200) };
        var root = tree.Nodes.Add("root");
        root.SetChildLoader(_ => [new TreeNode("child")]);

        Assert.Multiple(() =>
        {
            Assert.That(root.Nodes.Count, Is.Zero, "nothing is loaded yet");
            Assert.That(root.HasChildren, Is.True, "but the node reads as expandable");
        });
    }

    [Test]
    public void Expanding_a_node_runs_its_loader_exactly_once()
    {
        var tree = new TreeView { Bounds = new(0, 0, 200, 200) };
        var root = tree.Nodes.Add("root");
        var calls = 0;
        root.SetChildLoader(n =>
        {
            ++calls;
            return [new TreeNode($"{n.Text}.a"), new TreeNode($"{n.Text}.b")];
        });

        root.Expand();
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(1), "the loader runs on the first expand");
            Assert.That(root.Nodes.Count, Is.EqualTo(2), "its children were appended");
            Assert.That(root.Nodes[0].Text, Is.EqualTo("root.a"));
        });

        root.Collapse();
        root.Expand();
        Assert.That(calls, Is.EqualTo(1), "the loader is not run again");
    }

    [Test]
    public void A_loader_that_yields_nothing_leaves_a_no_longer_expandable_node()
    {
        var tree = new TreeView { Bounds = new(0, 0, 200, 200) };
        var root = tree.Nodes.Add("root");
        root.SetChildLoader(_ => []);

        root.Expand();

        Assert.That(root.HasChildren, Is.False, "an empty load makes the node a leaf");
    }

    // --- Drag-and-drop reorder (AllowReorder) --------------------------------------------------

    private const int _DragX = 100; // well right of the glyph/check cells, so a press lands on the label

    /// <summary>Presses the given visible row and drags to a band of the target row, returning the peer.</summary>
    private static HeadlessBackend BeginDrag(TreeView tree, int fromRow, int toRow, TreeViewDropLocation where, out HeadlessCanvasPeer canvas)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(tree);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        canvas.RaisePaint();

        var h = tree.ItemHeight;
        canvas.RaiseMouseDown(_DragX, (fromRow * h) + (h / 2));
        var y = where switch
        {
            TreeViewDropLocation.Above => (toRow * h) + 1,
            TreeViewDropLocation.Below => (toRow * h) + h - 1,
            _ => (toRow * h) + (h / 2),
        };
        canvas.RaiseMouseMove(_DragX, y);
        return backend;
    }

    [Test]
    public void Dragging_a_node_above_another_reorders_it_as_a_sibling()
    {
        var tree = MakeTree();          // root0(child00,child01), root1
        tree.AllowReorder = true;
        // rows: root0(0), root1(1)
        BeginDrag(tree, fromRow: 1, toRow: 0, TreeViewDropLocation.Above, out var canvas);

        var preview = tree.DropPreview;
        Assert.Multiple(() =>
        {
            Assert.That(preview.Dragging, Is.True);
            Assert.That(preview.Target, Is.SameAs(tree.Nodes[0]));
            Assert.That(preview.Location, Is.EqualTo(TreeViewDropLocation.Above));
            Assert.That(preview.Valid, Is.True);
        });

        canvas.RaiseMouseUp(_DragX, 1);
        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[0].Text, Is.EqualTo("root1"), "root1 moved before root0");
            Assert.That(tree.Nodes[1].Text, Is.EqualTo("root0"));
            Assert.That(tree.DropPreview.Dragging, Is.False, "the drag ended");
        });
    }

    [Test]
    public void Dragging_a_node_onto_another_reparents_it_as_a_child()
    {
        var tree = MakeTree();
        tree.AllowReorder = true;
        tree.Nodes[0].Expand(); // rows: root0(0), child00(1), child01(2), root1(3)
        BeginDrag(tree, fromRow: 2, toRow: 3, TreeViewDropLocation.Onto, out var canvas);

        Assert.That(tree.DropPreview.Location, Is.EqualTo(TreeViewDropLocation.Onto));
        canvas.RaiseMouseUp(_DragX, (3 * tree.ItemHeight) + (tree.ItemHeight / 2));

        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[0].Nodes.Count, Is.EqualTo(1), "child01 left root0");
            Assert.That(tree.Nodes[1].Nodes.Count, Is.EqualTo(1), "child01 is now under root1");
            Assert.That(tree.Nodes[1].Nodes[0].Text, Is.EqualTo("child01"));
            Assert.That(tree.Nodes[1].IsExpanded, Is.True, "the target expanded to reveal the drop");
        });
    }

    [Test]
    public void A_node_cannot_be_dropped_into_its_own_subtree()
    {
        var tree = MakeTree();
        tree.AllowReorder = true;
        tree.Nodes[0].Expand();
        tree.Nodes[0].Nodes[0].Expand(); // rows: root0(0), child00(1), leaf000(2), child01(3), root1(4)
        BeginDrag(tree, fromRow: 0, toRow: 2, TreeViewDropLocation.Onto, out var canvas); // root0 onto its own leaf000

        Assert.That(tree.DropPreview.Valid, Is.False, "an own-subtree target is rejected");

        canvas.RaiseMouseUp(_DragX, (2 * tree.ItemHeight) + (tree.ItemHeight / 2));
        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[0].Text, Is.EqualTo("root0"), "root0 stayed put");
            Assert.That(tree.Nodes[0].Nodes.Count, Is.EqualTo(2), "its children are intact");
        });
    }

    [Test]
    public void NodeDragOver_can_reject_a_target()
    {
        var tree = MakeTree();
        tree.AllowReorder = true;
        tree.NodeDragOver += (_, e) => e.Cancel = true;
        BeginDrag(tree, fromRow: 1, toRow: 0, TreeViewDropLocation.Above, out var canvas);

        Assert.That(tree.DropPreview.Valid, Is.False, "the handler rejected the target");
        canvas.RaiseMouseUp(_DragX, 1);
        Assert.That(tree.Nodes[0].Text, Is.EqualTo("root0"), "no move happened");
    }

    [Test]
    public void NodeDrop_can_veto_the_release()
    {
        var tree = MakeTree();
        tree.AllowReorder = true;
        var dropRaised = false;
        tree.NodeDrop += (_, e) => { dropRaised = true; e.Cancel = true; };
        BeginDrag(tree, fromRow: 1, toRow: 0, TreeViewDropLocation.Above, out var canvas);

        canvas.RaiseMouseUp(_DragX, 1);
        Assert.Multiple(() =>
        {
            Assert.That(dropRaised, Is.True, "NodeDrop fired on release over a valid target");
            Assert.That(tree.Nodes[0].Text, Is.EqualTo("root0"), "the veto kept the order");
        });
    }

    [Test]
    public void ItemDrag_is_raised_once_the_threshold_is_crossed()
    {
        var tree = MakeTree();
        tree.AllowReorder = true;
        TreeNode? dragged = null;
        tree.ItemDrag += (_, e) => dragged = e.Node;
        BeginDrag(tree, fromRow: 1, toRow: 0, TreeViewDropLocation.Above, out _);

        Assert.That(dragged, Is.SameAs(tree.Nodes[1]).Or.SameAs(tree.Nodes[0]));
        Assert.That(dragged!.Text, Is.EqualTo("root1"));
    }

    [Test]
    public void Hovering_onto_a_collapsed_parent_auto_expands_it()
    {
        var tree = MakeTree();
        tree.AllowReorder = true; // rows: root0(0, collapsed with children), root1(1)
        BeginDrag(tree, fromRow: 1, toRow: 0, TreeViewDropLocation.Onto, out _);

        Assert.That(tree.Nodes[0].IsExpanded, Is.False, "not expanded before the dwell elapses");
        tree.AutoExpandTick(); // the dwell timer would call this
        Assert.That(tree.Nodes[0].IsExpanded, Is.True, "the hovered collapsed parent expanded");
    }

    [Test]
    public void A_drag_with_no_reorder_permission_never_starts()
    {
        var tree = MakeTree(); // AllowReorder stays false
        BeginDrag(tree, fromRow: 1, toRow: 0, TreeViewDropLocation.Above, out var canvas);

        Assert.That(tree.DropPreview.Dragging, Is.False);
        Assert.DoesNotThrow(() => canvas.RaisePaint());
    }

    [Test]
    public void An_above_drop_paints_an_insertion_line_at_the_row_boundary()
    {
        var tree = MakeTree();
        tree.AllowReorder = true;
        BeginDrag(tree, fromRow: 1, toRow: 0, TreeViewDropLocation.Above, out var canvas);
        var g = canvas.RaisePaint();

        var h = tree.ItemHeight;
        // Above row 0: the marker line runs from the content indent (one level in) to the right edge,
        // clamped to y=1. Width 300 → right edge 298.
        Assert.That(
            g.Operations.Exists(o => o.StartsWith("line") && o.Contains($"{h},1-298,1")),
            Is.True,
            "an indented horizontal insertion line is drawn at the top of the target row");
    }

    [Test]
    public void A_drag_paints_a_translucent_drag_image_by_default()
    {
        var tree = MakeTree();
        tree.AllowReorder = true;
        BeginDrag(tree, fromRow: 1, toRow: 0, TreeViewDropLocation.Above, out var canvas);
        var g = canvas.RaisePaint();

        Assert.That(
            g.Operations.Exists(o => o.StartsWith("fill #80")),
            Is.True,
            "a half-alpha (0x80) chip is painted under the pointer as the drag image");
    }

    [Test]
    public void ShowDragImage_off_suppresses_the_drag_image()
    {
        var tree = MakeTree();
        tree.AllowReorder = true;
        tree.ShowDragImage = false;
        BeginDrag(tree, fromRow: 1, toRow: 0, TreeViewDropLocation.Above, out var canvas);
        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("fill #80")), Is.False, "no drag chip when disabled");
    }

    [Test]
    public void An_onto_drop_outlines_the_target_row()
    {
        var tree = MakeTree();
        tree.AllowReorder = true;
        tree.Nodes[0].Expand(); // rows: root0(0), child00(1), child01(2), root1(3)
        BeginDrag(tree, fromRow: 2, toRow: 3, TreeViewDropLocation.Onto, out var canvas);
        var g = canvas.RaisePaint();

        var h = tree.ItemHeight;
        var y = 3 * h;
        // The onto marker outlines the whole target row (width 300 → 299 wide, row height − 1 tall),
        // which is distinct from the full-height border rectangle.
        Assert.That(
            g.Operations.Exists(o => o.StartsWith("rect") && o.Contains($"0,{y},299,{h - 1}")),
            Is.True,
            "the reparent target row is outlined");
    }
}
