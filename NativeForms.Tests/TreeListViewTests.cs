using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class TreeListViewTests
{
    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    /// <summary>A data-source item: a name and nested children.</summary>
    private sealed class Item(string name)
    {
        public string Name { get; } = name;
        public List<Item> Children { get; } = [];
    }

    /// <summary>
    /// root0(child00(leaf000), child01), root1 — 5 nodes, all collapsed. Columns: the 200px tree
    /// column and an 80px "Size" column whose selector reads the size string stored in each Tag.
    /// </summary>
    private static TreeListView MakeTree()
    {
        var tree = new TreeListView { Bounds = new(0, 0, 400, 242) }; // 22px header + 10 rows
        tree.Columns.AddRange(
        [
            new TreeListViewColumn("Name", 200),
            new TreeListViewColumn("Size", 80, static n => n.Tag as string ?? string.Empty),
        ]);
        var root0 = tree.Nodes.Add("root0");
        root0.Tag = "10 KB";
        var child00 = root0.Nodes.Add("child00");
        child00.Tag = "2 KB";
        child00.Nodes.Add("leaf000").Tag = "1 KB";
        root0.Nodes.Add("child01").Tag = "3 KB";
        tree.Nodes.Add("root1").Tag = "5 KB";
        return tree;
    }

    [Test]
    public void Paint_draws_headers_tree_cells_and_selector_subitems()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Name"), Is.True, "header caption");
            Assert.That(g.DrewText("Size"), Is.True, "header caption");
            Assert.That(g.DrewText("root0"), Is.True, "tree-column node text");
            Assert.That(g.DrewText("10 KB"), Is.True, "selector-produced sub-item text");
            Assert.That(g.DrewText("child00"), Is.False, "collapsed children stay hidden");
        });
    }

    [Test]
    public void Subitem_cells_use_the_column_alignment()
    {
        var tree = MakeTree();
        tree.Columns[1].TextAlign = ContentAlignment.MiddleRight;
        var canvas = Realize(tree);

        var g = canvas.RaisePaint();

        var subItem = g.Operations.Single(o => o.StartsWith("text \"10 KB\""));
        Assert.That(subItem, Does.Contain("MiddleRight"));
    }

    [Test]
    public void Cells_clip_to_their_column_width()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("clip 0,0,200,22"), "header cell of the tree column");
            Assert.That(g.Operations, Does.Contain("clip 200,0,80,22"), "header cell of the Size column");
            Assert.That(g.Operations, Does.Contain("clip 0,22,200,22"), "first row's tree cell");
            Assert.That(g.Operations, Does.Contain("clip 200,22,80,22"), "first row's Size cell");
        });
    }

    [Test]
    public void Tree_column_indents_child_rows_one_cell_deeper_than_their_parents()
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
    public void Expand_glyph_is_pixel_identical_to_the_TreeView_glyph()
    {
        var treeView = new TreeView { Bounds = new(0, 0, 300, 220) };
        treeView.Nodes.Add("parent").Nodes.Add("kid");
        var treeList = new TreeListView { Bounds = new(0, 0, 300, 220), ShowColumnHeaders = false };
        treeList.Columns.Add(new TreeListViewColumn("Name", 200));
        treeList.Nodes.Add("parent").Nodes.Add("kid");

        var treeViewOps = Realize(treeView).RaisePaint().Operations;
        var treeListOps = Realize(treeList).RaisePaint().Operations;

        // The glyph box plus its +/− strokes (control-text lines); TreeView's extra connector lines
        // use the disabled-text color and are excluded on both sides by the same filter.
        static List<string> GlyphOps(List<string> ops)
            => ops.Where(o => (o.StartsWith("rect ") && o.EndsWith(",9,9")) || o.StartsWith("line #FF1A1A1A")).ToList();

        Assert.That(GlyphOps(treeListOps), Is.EqualTo(GlyphOps(treeViewOps)));
    }

    [Test]
    public void Paint_draws_a_glyph_only_for_nodes_with_children()
    {
        var tree = new TreeListView { Bounds = new(0, 0, 300, 242) };
        tree.Columns.Add(new TreeListViewColumn("Name", 200));
        tree.Nodes.Add("parent").Nodes.Add("kid");
        tree.Nodes.Add("leaf");
        var canvas = Realize(tree);

        var g = canvas.RaisePaint();

        var glyphs = g.Operations.Count(o => o.StartsWith("rect ") && o.EndsWith(",9,9"));
        Assert.That(glyphs, Is.EqualTo(1));
    }

    [Test]
    public void Expand_reveals_children_updates_rows_and_repaints()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);
        var before = canvas.InvalidateCount;

        tree.Nodes[0].Expand();

        Assert.Multiple(() =>
        {
            Assert.That(tree.VisibleNodeCount, Is.EqualTo(4), "root0 + its two children + root1");
            Assert.That(canvas.InvalidateCount, Is.GreaterThan(before));
        });

        tree.Nodes[0].Collapse();
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
    public void Click_below_the_header_selects_the_full_row_and_raises_AfterSelect()
    {
        var tree = MakeTree();
        TreeNode? selected = null;
        tree.AfterSelect += (_, e) => selected = e.Node;
        var canvas = Realize(tree);

        canvas.RaiseMouseDown(390, 30); // first row (22..44), far right of every column = full-row select

        Assert.Multiple(() =>
        {
            Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[0]));
            Assert.That(selected, Is.SameAs(tree.Nodes[0]));
        });
    }

    [Test]
    public void Click_on_the_header_band_does_not_select()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);

        canvas.RaiseMouseDown(10, 5); // within the header band

        Assert.That(tree.SelectedNode, Is.Null);
    }

    [Test]
    public void Click_on_the_expand_glyph_toggles_without_selecting()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);

        canvas.RaiseMouseDown(11, 33); // glyph cell of root0 (first indent cell of row 22..44)

        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes[0].IsExpanded, Is.True);
            Assert.That(tree.SelectedNode, Is.Null, "glyph clicks expand, they do not select");
        });

        canvas.RaiseMouseDown(11, 33);
        Assert.That(tree.Nodes[0].IsExpanded, Is.False);
    }

    [Test]
    public void Glyph_clicks_stop_at_the_tree_column_boundary()
    {
        var tree = new TreeListView { Bounds = new(0, 0, 300, 242) };
        tree.Columns.AddRange(
        [
            new TreeListViewColumn("Name", 30), // narrower than two indent cells
            new TreeListViewColumn("Size", 100),
        ]);
        var kid = tree.Nodes.Add("parent").Nodes.Add("kid");
        kid.Nodes.Add("grandkid");
        tree.Nodes[0].Expand();
        var canvas = Realize(tree);

        // kid's glyph cell spans x 22..44 but the tree column ends at 30; click the clipped part.
        canvas.RaiseMouseDown(35, 55); // second row (44..66)

        Assert.Multiple(() =>
        {
            Assert.That(kid.IsExpanded, Is.False, "the clipped-away glyph must not react outside its column");
            Assert.That(tree.SelectedNode, Is.SameAs(kid), "the click falls through to row selection");
        });
    }

    [Test]
    public void Double_click_on_a_row_toggles_expansion()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);

        canvas.RaiseMouseDown(150, 33); // select root0
        canvas.RaiseMouseDown(150, 33); // immediate second click = double-click

        Assert.That(tree.Nodes[0].IsExpanded, Is.True);
    }

    [Test]
    public void Check_click_toggles_and_raises_AfterCheck_without_selecting()
    {
        var tree = MakeTree();
        tree.CheckBoxes = true;
        TreeNode? checkedNode = null;
        tree.AfterCheck += (_, e) => checkedNode = e.Node;
        var canvas = Realize(tree);

        canvas.RaiseMouseDown(26, 33); // check cell of root0 (after the 22px glyph cell)

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
    public void PageDown_and_PageUp_move_the_selection_by_a_page_and_scroll()
    {
        var tree = new TreeListView { Bounds = new(0, 0, 200, 132) }; // 22px header + 5 rows
        tree.Columns.Add(new TreeListViewColumn("Name", 180));
        for (var i = 0; i < 40; ++i)
            tree.Nodes.Add("Node" + i);

        var canvas = Realize(tree);
        tree.SelectedNode = tree.Nodes[0];

        canvas.RaiseKeyDown(Keys.PageDown);
        Assert.Multiple(() =>
        {
            Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[5]));
            Assert.That(tree.TopIndex, Is.EqualTo(1), "selection scrolled into view");
        });

        canvas.RaiseKeyDown(Keys.PageUp);
        Assert.That(tree.SelectedNode, Is.SameAs(tree.Nodes[0]));
    }

    [Test]
    public void Wheel_scrolling_changes_the_painted_window()
    {
        var tree = new TreeListView { Bounds = new(0, 0, 200, 132) }; // 22px header + 5 rows
        tree.Columns.Add(new TreeListViewColumn("Name", 180));
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
    public void Paint_highlights_the_selected_row_across_all_columns()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);
        tree.SelectedNode = tree.Nodes[0];

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FF0078D4 0,22,400,22"), "full-width selection background");
            Assert.That(g.Operations.Any(o => o.StartsWith("text \"root0\" #FFFFFFFF")), Is.True, "selection text color in the tree cell");
            Assert.That(g.Operations.Any(o => o.StartsWith("text \"10 KB\" #FFFFFFFF")), Is.True, "selection text color in the sub-item cell");
        });
    }

    [Test]
    public void Paint_draws_the_node_image_when_an_imagelist_is_set()
    {
        using var images = new ImageList(new Size(4, 4));
        var index = images.Add(new int[16]);
        var tree = new TreeListView { Bounds = new(0, 0, 300, 242), ImageList = images };
        tree.Columns.Add(new TreeListViewColumn("Name", 200));
        tree.Nodes.Add("pictured").ImageIndex = index;
        tree.Nodes.Add("plain");
        var canvas = Realize(tree);

        var g = canvas.RaisePaint();

        var imageOps = g.Operations.Count(o => o.StartsWith("image "));
        Assert.That(imageOps, Is.EqualTo(1), "only the node with an image index paints an image");
    }

    [Test]
    public void Painting_is_virtualized_for_very_large_trees()
    {
        var tree = new TreeListView { Bounds = new(0, 0, 200, 242) };
        tree.Columns.AddRange(
        [
            new TreeListViewColumn("Name", 120),
            new TreeListViewColumn("Index", 60, static n => n.Tag as string ?? string.Empty),
        ]);
        var nodes = new TreeNode[100000];
        for (var i = 0; i < nodes.Length; ++i)
            nodes[i] = new TreeNode("Node" + i);

        tree.Nodes.AddRange(nodes);
        var canvas = Realize(tree);

        var g = canvas.RaisePaint();

        var textOps = g.Operations.Count(o => o.StartsWith("text "));
        Assert.That(textOps, Is.LessThan(50), "only the visible rows (plus the header) are painted");
    }

    [Test]
    public void Column_width_change_repaints_with_the_new_metrics()
    {
        var tree = MakeTree();
        var canvas = Realize(tree);
        var before = canvas.InvalidateCount;

        tree.Columns[1].Width = 120;

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(canvas.InvalidateCount, Is.GreaterThan(before), "width change invalidates");
            Assert.That(g.Operations, Does.Contain("clip 200,22,120,22"), "cells clip to the new width");
        });
    }

    [Test]
    public void SetDataSource_builds_the_hierarchy_via_the_children_selector_and_round_trips_tags()
    {
        var docs = new Item("docs");
        var readme = new Item("readme");
        docs.Children.Add(readme);
        var src = new Item("src");
        var tree = MakeTree();

        tree.SetDataSource([docs, src], static i => i.Name, static i => i.Children);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes.Count, Is.EqualTo(2), "old nodes are replaced");
            Assert.That(tree.Nodes[0].Text, Is.EqualTo("docs"));
            Assert.That(tree.Nodes[0].Tag, Is.SameAs(docs), "the item rides along in Tag");
            Assert.That(tree.Nodes[0].Nodes[0].Text, Is.EqualTo("readme"));
            Assert.That(tree.Nodes[0].Nodes[0].Tag, Is.SameAs(readme));
            Assert.That(tree.Nodes[1].Tag, Is.SameAs(src));
            Assert.That(tree.VisibleNodeCount, Is.EqualTo(2), "children start collapsed");
        });

        tree.Nodes[0].Expand();
        Assert.That(tree.VisibleNodeCount, Is.EqualTo(3));
    }

    [Test]
    public void SetDataSource_depth_guard_bounds_cyclic_graphs()
    {
        var a = new Item("A");
        a.Children.Add(a); // self-cycle
        var tree = new TreeListView { Bounds = new(0, 0, 200, 242) };

        tree.SetDataSource([a], static i => i.Name, static i => i.Children, maxDepth: 5);
        tree.Nodes[0].ExpandAll();

        Assert.That(tree.VisibleNodeCount, Is.EqualTo(5), "the cycle is cut after maxDepth levels");
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
}
