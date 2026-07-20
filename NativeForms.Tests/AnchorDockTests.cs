using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The Anchor/Dock layout engine must reproduce the Windows Forms resize model: when a container
/// resizes, each <see cref="DockStyle.None"/> child repositions per <see cref="Control.Anchor"/>
/// (anchored edges keep their distance to the container's <see cref="Control.DisplayRectangle"/>,
/// opposing anchors stretch, no anchors drift by half the delta), while docked children claim
/// edges of the remaining rectangle in <see cref="Control.Controls"/> order with
/// <see cref="DockStyle.Fill"/> taking the rest. Layout panels that own their children's bounds
/// keep ignoring both properties; plain containers (Form, Panel, GroupBox, TabPage,
/// SplitContainer panels) honor them.
/// </summary>
[TestFixture]
internal sealed class AnchorDockTests
{
    private static Panel MakeContainer(int width = 200, int height = 100)
        => new() { Bounds = new(0, 0, width, height) };

    private static Button MakeChild(int x, int y, int width, int height)
        => new() { Bounds = new(x, y, width, height) };

    [Test]
    public void Defaults_match_WinForms()
    {
        var child = new Button();

        Assert.Multiple(() =>
        {
            Assert.That(child.Anchor, Is.EqualTo(AnchorStyles.Top | AnchorStyles.Left));
            Assert.That(child.Dock, Is.EqualTo(DockStyle.None));
        });
    }

    [Test]
    public void TopLeft_anchored_child_stays_put_on_resize()
    {
        var panel = MakeContainer();
        var child = MakeChild(10, 20, 50, 30);
        panel.Controls.Add(child);

        panel.Size = new(300, 200);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(10, 20, 50, 30)));
    }

    [Test]
    public void Right_anchored_child_translates_with_the_right_edge()
    {
        var panel = MakeContainer();
        var child = MakeChild(140, 20, 50, 30);
        child.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        panel.Controls.Add(child);

        panel.Size = new(300, 100);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(240, 20, 50, 30)));
    }

    [Test]
    public void Bottom_anchored_child_translates_with_the_bottom_edge()
    {
        var panel = MakeContainer();
        var child = MakeChild(10, 60, 50, 30);
        child.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        panel.Controls.Add(child);

        panel.Size = new(200, 180);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(10, 140, 50, 30)));
    }

    [Test]
    public void LeftRight_anchored_child_stretches_horizontally()
    {
        var panel = MakeContainer();
        var child = MakeChild(10, 20, 100, 30);
        child.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(child);

        panel.Size = new(260, 100);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(10, 20, 160, 30)));
    }

    [Test]
    public void TopBottom_anchored_child_stretches_vertically()
    {
        var panel = MakeContainer();
        var child = MakeChild(10, 20, 50, 60);
        child.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        panel.Controls.Add(child);

        panel.Size = new(200, 150);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(10, 20, 50, 110)));
    }

    [Test]
    public void All_edges_anchored_child_stretches_both_ways()
    {
        var panel = MakeContainer();
        var child = MakeChild(10, 10, 180, 80);
        child.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(child);

        panel.Size = new(150, 80);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(10, 10, 130, 60)));
    }

    [Test]
    public void Unanchored_child_drifts_by_half_the_delta()
    {
        var panel = MakeContainer();
        var child = MakeChild(80, 40, 40, 20);
        child.Anchor = AnchorStyles.None;
        panel.Controls.Add(child);

        panel.Size = new(300, 160);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(130, 70, 40, 20)));
    }

    [Test]
    public void Shrinking_the_container_moves_right_anchored_children_left()
    {
        var panel = MakeContainer();
        var child = MakeChild(140, 20, 50, 30);
        child.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        panel.Controls.Add(child);

        panel.Size = new(160, 100);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(100, 20, 50, 30)));
    }

    [Test]
    public void Successive_resizes_accumulate_anchor_deltas()
    {
        var panel = MakeContainer();
        var child = MakeChild(140, 20, 50, 30);
        child.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        panel.Controls.Add(child);

        panel.Size = new(300, 100);
        panel.Size = new(250, 100);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(190, 20, 50, 30)));
    }

    [Test]
    public void Setting_Dock_resets_Anchor_to_the_default()
    {
        var child = new Button { Anchor = AnchorStyles.Bottom | AnchorStyles.Right };

        child.Dock = DockStyle.Top;

        Assert.That(child.Anchor, Is.EqualTo(AnchorStyles.Top | AnchorStyles.Left));
    }

    [Test]
    public void Setting_Anchor_resets_Dock_to_None()
    {
        var child = new Button { Dock = DockStyle.Fill };

        child.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

        Assert.Multiple(() =>
        {
            Assert.That(child.Dock, Is.EqualTo(DockStyle.None));
            Assert.That(child.Anchor, Is.EqualTo(AnchorStyles.Bottom | AnchorStyles.Right));
        });
    }

    [Test]
    public void Dock_Top_claims_the_full_width_and_keeps_the_height()
    {
        var panel = MakeContainer();
        var child = MakeChild(30, 40, 50, 25);
        panel.Controls.Add(child);

        child.Dock = DockStyle.Top;

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(0, 0, 200, 25)));
    }

    [Test]
    public void Docked_children_claim_edges_in_Controls_order_and_Fill_takes_the_rest()
    {
        var panel = MakeContainer(200, 100);
        var top = MakeChild(0, 0, 10, 20);
        var left = MakeChild(0, 0, 30, 10);
        var fill = MakeChild(0, 0, 10, 10);
        panel.Controls.AddRange(top, left, fill);

        top.Dock = DockStyle.Top;
        left.Dock = DockStyle.Left;
        fill.Dock = DockStyle.Fill;

        Assert.Multiple(() =>
        {
            Assert.That(top.Bounds, Is.EqualTo(new Rectangle(0, 0, 200, 20)));
            Assert.That(left.Bounds, Is.EqualTo(new Rectangle(0, 20, 30, 80)));
            Assert.That(fill.Bounds, Is.EqualTo(new Rectangle(30, 20, 170, 80)));
        });
    }

    [Test]
    public void Fill_takes_the_remainder_even_when_added_before_edge_docks()
    {
        var panel = MakeContainer(200, 100);
        var fill = MakeChild(0, 0, 10, 10);
        var bottom = MakeChild(0, 0, 10, 20);
        panel.Controls.AddRange(fill, bottom);

        fill.Dock = DockStyle.Fill;
        bottom.Dock = DockStyle.Bottom;

        Assert.Multiple(() =>
        {
            Assert.That(bottom.Bounds, Is.EqualTo(new Rectangle(0, 80, 200, 20)));
            Assert.That(fill.Bounds, Is.EqualTo(new Rectangle(0, 0, 200, 80)));
        });
    }

    [Test]
    public void Dock_Right_and_Bottom_claim_their_edges()
    {
        var panel = MakeContainer(200, 100);
        var right = MakeChild(0, 0, 40, 10);
        var bottom = MakeChild(0, 0, 10, 30);
        panel.Controls.AddRange(right, bottom);

        right.Dock = DockStyle.Right;
        bottom.Dock = DockStyle.Bottom;

        Assert.Multiple(() =>
        {
            Assert.That(right.Bounds, Is.EqualTo(new Rectangle(160, 0, 40, 100)));
            Assert.That(bottom.Bounds, Is.EqualTo(new Rectangle(0, 70, 160, 30)));
        });
    }

    [Test]
    public void Docking_respects_the_container_padding()
    {
        var panel = MakeContainer(200, 100);
        panel.Padding = new(5);
        var fill = MakeChild(0, 0, 10, 10);
        panel.Controls.Add(fill);

        fill.Dock = DockStyle.Fill;

        Assert.That(fill.Bounds, Is.EqualTo(new Rectangle(5, 5, 190, 90)));
    }

    [Test]
    public void Docked_children_follow_a_container_resize()
    {
        var panel = MakeContainer(200, 100);
        var top = MakeChild(0, 0, 10, 20);
        panel.Controls.Add(top);
        top.Dock = DockStyle.Top;

        panel.Size = new(300, 150);

        Assert.That(top.Bounds, Is.EqualTo(new Rectangle(0, 0, 300, 20)));
    }

    [Test]
    public void Anchored_children_position_against_the_display_rectangle_not_the_docked_remainder()
    {
        var panel = MakeContainer(200, 100);
        var toolbar = MakeChild(0, 0, 10, 20);
        var button = MakeChild(140, 60, 50, 30);
        panel.Controls.AddRange(toolbar, button);
        toolbar.Dock = DockStyle.Top;
        button.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

        panel.Size = new(260, 150);

        Assert.That(button.Bounds, Is.EqualTo(new Rectangle(200, 110, 50, 30)));
    }

    [Test]
    public void Nested_containers_cascade_anchor_layout()
    {
        var outer = MakeContainer(200, 100);
        var inner = MakeContainer(180, 80);
        inner.Bounds = new(10, 10, 180, 80);
        inner.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        var child = MakeChild(130, 50, 40, 20);
        child.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        inner.Controls.Add(child);
        outer.Controls.Add(inner);

        outer.Size = new(300, 160);

        Assert.Multiple(() =>
        {
            Assert.That(inner.Bounds, Is.EqualTo(new Rectangle(10, 10, 280, 140)));
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(230, 110, 40, 20)));
        });
    }

    [Test]
    public void SuspendLayout_batches_until_ResumeLayout()
    {
        var panel = MakeContainer(200, 100);
        var child = MakeChild(140, 20, 50, 30);
        child.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        panel.Controls.Add(child);

        panel.SuspendLayout();
        panel.Size = new(300, 100);
        panel.Size = new(260, 100);
        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(140, 20, 50, 30)), "no layout while suspended");

        panel.ResumeLayout();

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(200, 20, 50, 30)), "one pass over the accumulated delta");
    }

    [Test]
    public void ResumeLayout_false_skips_the_pending_pass()
    {
        var panel = MakeContainer(200, 100);
        var child = MakeChild(0, 0, 10, 20);
        panel.Controls.Add(child);
        child.Dock = DockStyle.Top;

        panel.SuspendLayout();
        child.Bounds = new(30, 40, 50, 25); // knock it out of its dock slot
        panel.Size = new(300, 100);
        panel.ResumeLayout(performLayout: false);
        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(30, 40, 50, 25)), "still where it was knocked to");

        panel.PerformLayout();

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(0, 0, 300, 25)));
    }

    [Test]
    public void Nested_suspends_need_matching_resumes()
    {
        var panel = MakeContainer(200, 100);
        var child = MakeChild(0, 0, 10, 20);
        panel.Controls.Add(child);
        child.Dock = DockStyle.Top;

        panel.SuspendLayout();
        panel.SuspendLayout();
        panel.Size = new(300, 100);
        panel.ResumeLayout();
        Assert.That(child.Bounds.Width, Is.EqualTo(200), "still suspended after the first resume");

        panel.ResumeLayout();

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(0, 0, 300, 20)));
    }

    [Test]
    public void Removing_a_docked_child_reflows_the_remaining_ones()
    {
        var panel = MakeContainer(200, 100);
        var top = MakeChild(0, 0, 10, 20);
        var fill = MakeChild(0, 0, 10, 10);
        panel.Controls.AddRange(top, fill);
        top.Dock = DockStyle.Top;
        fill.Dock = DockStyle.Fill;

        panel.Controls.Remove(top);

        Assert.That(fill.Bounds, Is.EqualTo(new Rectangle(0, 0, 200, 100)));
    }

    [Test]
    public void Form_resize_drives_the_engine()
    {
        var form = new Form { Bounds = new(0, 0, 200, 100) };
        var child = MakeChild(140, 20, 50, 30);
        child.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        form.Controls.Add(child);

        form.Size = new(320, 100);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(260, 20, 50, 30)));
    }

    [Test]
    public void A_native_user_resize_relayouts_the_form()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 200, 100) };
        var status = MakeChild(0, 0, 10, 20);
        form.Controls.Add(status);
        status.Dock = DockStyle.Bottom;
        Application.Run(form, backend);
        var peer = backend.Created.OfType<HeadlessWindowPeer>().Single();

        peer.FireBoundsChanged(new(0, 0, 300, 200));

        Assert.That(status.Bounds, Is.EqualTo(new Rectangle(0, 180, 300, 20)));
    }

    [Test]
    public void FlowLayoutPanel_ignores_Anchor_and_Dock()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 300, 100) };
        var first = new Button { Size = new(60, 20) };
        var second = new Button { Size = new(60, 20) };
        panel.Controls.AddRange(first, second);

        first.Dock = DockStyle.Fill;
        second.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        panel.Size = new(400, 100);

        Assert.Multiple(() =>
        {
            Assert.That(first.Bounds, Is.EqualTo(new Rectangle(0, 0, 60, 20)));
            Assert.That(second.Bounds, Is.EqualTo(new Rectangle(60, 0, 60, 20)));
        });
    }

    [Test]
    public void TableLayoutPanel_still_fills_cells_for_unassigned_children()
    {
        var table = new TableLayoutPanel { Bounds = new(0, 0, 200, 100), ColumnCount = 2 };
        var child = new Button { Size = new(60, 20) };
        table.Controls.Add(child);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(0, 0, 100, 100)));
    }

    [Test]
    public void TableLayoutPanel_anchors_an_assigned_child_within_its_cell()
    {
        var table = new TableLayoutPanel { Bounds = new(0, 0, 200, 100), ColumnCount = 2 };
        var child = new Button { Size = new(60, 20) };
        table.Controls.Add(child);

        child.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(0, 0, 60, 20)));
    }

    [Test]
    public void TableLayoutPanel_stretches_opposing_anchors_and_centers_the_free_axis()
    {
        var table = new TableLayoutPanel { Bounds = new(0, 0, 200, 100), ColumnCount = 2 };
        var child = new Button { Size = new(60, 20) };
        table.Controls.Add(child);

        child.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(0, 40, 100, 20)));
    }

    [Test]
    public void TableLayoutPanel_docks_an_assigned_child_within_its_cell()
    {
        var table = new TableLayoutPanel { Bounds = new(0, 0, 200, 100), ColumnCount = 2 };
        var child = new Button { Size = new(60, 20) };
        table.Controls.Add(child);

        child.Dock = DockStyle.Bottom;

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(0, 80, 100, 20)));
    }

    [Test]
    public void TabPage_children_honor_their_anchors()
    {
        var tabs = new TabControl { Bounds = new(0, 0, 200, 100) };
        var page = new TabPage("First");
        tabs.TabPages.Add(page);
        var pageSize = page.Size;
        var child = new Button { Bounds = new(pageSize.Width - 60, pageSize.Height - 30, 50, 20) };
        child.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        page.Controls.Add(child);

        tabs.Size = new(300, 160);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(pageSize.Width + 40, pageSize.Height + 30, 50, 20)));
    }

    [Test]
    public void SplitContainer_panel_children_honor_their_anchors()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 200, 100), SplitterDistance = 80 };
        var child = new Button { Bounds = new(20, 0, 50, 20) };
        child.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        split.Panel1.Controls.Add(child);

        split.SplitterDistance = 120;

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(60, 0, 50, 20)));
    }

    [Test]
    public void A_padding_inset_shift_moves_only_the_children_anchored_to_the_moved_edge()
    {
        var panel = MakeContainer(200, 100);
        var top = MakeChild(10, 20, 50, 30);
        var bottom = MakeChild(10, 60, 50, 30);
        bottom.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        panel.Controls.AddRange(top, bottom);

        panel.Padding = new(0, 10, 0, 0); // only the display rectangle's top edge moves

        Assert.Multiple(() =>
        {
            Assert.That(top.Bounds, Is.EqualTo(new Rectangle(10, 30, 50, 30)), "top-anchored keeps its distance to the shifted top edge");
            Assert.That(bottom.Bounds, Is.EqualTo(new Rectangle(10, 60, 50, 30)), "bottom-anchored stays with the unmoved bottom edge");
        });
    }

    [Test]
    public void SplitContainer_keeps_its_default_distance_until_it_has_a_size()
    {
        var split = new SplitContainer();
        Assert.That(split.SplitterDistance, Is.EqualTo(50), "construction must not clamp the default away");

        split.Bounds = new(0, 0, 200, 100);

        Assert.That(split.Panel1.Bounds, Is.EqualTo(new Rectangle(0, 0, 50, 100)));
    }

    [Test]
    public void GroupBox_docks_children_into_its_display_rectangle()
    {
        var box = new GroupBox { Bounds = new(0, 0, 200, 100) };
        var display = box.DisplayRectangle;
        var fill = MakeChild(0, 0, 10, 10);
        box.Controls.Add(fill);

        fill.Dock = DockStyle.Fill;

        Assert.That(fill.Bounds, Is.EqualTo(display));
    }
}
