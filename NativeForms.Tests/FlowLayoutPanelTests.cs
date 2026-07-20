using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="FlowLayoutPanel"/> must position its children in <see cref="Control.Controls"/> order
/// along the <see cref="FlowLayoutPanel.FlowDirection"/>, honoring each child's
/// <see cref="Control.Margin"/>, wrapping at the client edge while
/// <see cref="FlowLayoutPanel.WrapContents"/> is on, and re-flowing whenever a child joins, leaves
/// or resizes. Layout happens in logical space, so <see cref="Panel.AutoScroll"/> still scrolls the
/// overflow by moving peers.
/// </summary>
[TestFixture]
internal sealed class FlowLayoutPanelTests
{
    private static readonly string _TrackFill = "fill #FFECECEC"; // HeaderBackground = scrollbar track

    private static HeadlessCanvasPeer Realize(FlowLayoutPanel panel, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(panel);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    private static Button MakeChild(int width, int height, int margin = 0)
        => new() { Size = new(width, height), Margin = new(margin) };

    [Test]
    public void LeftToRight_flows_children_in_a_row()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 300, 100) };
        panel.Controls.AddRange(MakeChild(60, 20), MakeChild(60, 20), MakeChild(60, 20));

        Assert.Multiple(() =>
        {
            Assert.That(panel.Controls[0].Bounds, Is.EqualTo(new Rectangle(0, 0, 60, 20)));
            Assert.That(panel.Controls[1].Bounds, Is.EqualTo(new Rectangle(60, 0, 60, 20)));
            Assert.That(panel.Controls[2].Bounds, Is.EqualTo(new Rectangle(120, 0, 60, 20)));
        });
    }

    [Test]
    public void Margins_offset_children_and_consume_flow_space()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 300, 100) };
        panel.Controls.AddRange(MakeChild(60, 20, margin: 3), MakeChild(60, 20, margin: 3));

        Assert.Multiple(() =>
        {
            Assert.That(panel.Controls[0].Bounds, Is.EqualTo(new Rectangle(3, 3, 60, 20)));
            Assert.That(panel.Controls[1].Bounds, Is.EqualTo(new Rectangle(69, 3, 60, 20)));
        });
    }

    [Test]
    public void Wrapping_starts_a_new_row_at_the_client_edge()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 150, 100) };
        panel.Controls.AddRange(MakeChild(60, 20), MakeChild(60, 20), MakeChild(60, 20));

        Assert.Multiple(() =>
        {
            Assert.That(panel.Controls[1].Bounds, Is.EqualTo(new Rectangle(60, 0, 60, 20)));
            Assert.That(panel.Controls[2].Bounds, Is.EqualTo(new Rectangle(0, 20, 60, 20)));
        });
    }

    [Test]
    public void A_new_row_starts_below_the_tallest_child_of_the_previous_row()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 150, 100) };
        panel.Controls.AddRange(MakeChild(60, 20), MakeChild(60, 30), MakeChild(60, 20));

        Assert.That(panel.Controls[2].Bounds, Is.EqualTo(new Rectangle(0, 30, 60, 20)));
    }

    [Test]
    public void WrapContents_false_keeps_a_single_line()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 150, 100), WrapContents = false };
        panel.Controls.AddRange(MakeChild(60, 20), MakeChild(60, 20), MakeChild(60, 20));

        Assert.That(panel.Controls[2].Bounds, Is.EqualTo(new Rectangle(120, 0, 60, 20)));
    }

    [Test]
    public void TopDown_flows_down_then_into_a_new_column()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 100, 70), FlowDirection = FlowDirection.TopDown };
        panel.Controls.AddRange(MakeChild(60, 30), MakeChild(60, 30), MakeChild(60, 30));

        Assert.Multiple(() =>
        {
            Assert.That(panel.Controls[0].Bounds, Is.EqualTo(new Rectangle(0, 0, 60, 30)));
            Assert.That(panel.Controls[1].Bounds, Is.EqualTo(new Rectangle(0, 30, 60, 30)));
            Assert.That(panel.Controls[2].Bounds, Is.EqualTo(new Rectangle(60, 0, 60, 30)));
        });
    }

    [Test]
    public void RightToLeft_mirrors_the_flow()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 300, 100), FlowDirection = FlowDirection.RightToLeft };
        panel.Controls.AddRange(MakeChild(60, 20), MakeChild(60, 20));

        Assert.Multiple(() =>
        {
            Assert.That(panel.Controls[0].Bounds, Is.EqualTo(new Rectangle(240, 0, 60, 20)));
            Assert.That(panel.Controls[1].Bounds, Is.EqualTo(new Rectangle(180, 0, 60, 20)));
        });
    }

    [Test]
    public void BottomUp_mirrors_vertically()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 100, 100), FlowDirection = FlowDirection.BottomUp };
        panel.Controls.AddRange(MakeChild(60, 30), MakeChild(60, 30));

        Assert.Multiple(() =>
        {
            Assert.That(panel.Controls[0].Bounds, Is.EqualTo(new Rectangle(0, 70, 60, 30)));
            Assert.That(panel.Controls[1].Bounds, Is.EqualTo(new Rectangle(0, 40, 60, 30)));
        });
    }

    [Test]
    public void Adding_a_child_flows_it_after_the_existing_ones()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 300, 100) };
        panel.Controls.Add(MakeChild(60, 20));

        var late = MakeChild(60, 20);
        panel.Controls.Add(late);

        Assert.That(late.Bounds, Is.EqualTo(new Rectangle(60, 0, 60, 20)));
    }

    [Test]
    public void Removing_a_child_reflows_the_rest()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 300, 100) };
        var middle = MakeChild(60, 20);
        panel.Controls.AddRange(MakeChild(60, 20), middle, MakeChild(60, 20));

        panel.Controls.Remove(middle);

        Assert.That(panel.Controls[1].Bounds, Is.EqualTo(new Rectangle(60, 0, 60, 20)));
    }

    [Test]
    public void Resizing_a_child_reflows_its_followers()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 300, 100) };
        panel.Controls.AddRange(MakeChild(60, 20), MakeChild(60, 20));

        panel.Controls[0].Width = 80;

        Assert.That(panel.Controls[1].Bounds, Is.EqualTo(new Rectangle(80, 0, 60, 20)));
    }

    [Test]
    public void Resizing_the_panel_rewraps()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 300, 100) };
        panel.Controls.AddRange(MakeChild(60, 20), MakeChild(60, 20));

        panel.Width = 100;

        Assert.That(panel.Controls[1].Bounds, Is.EqualTo(new Rectangle(0, 20, 60, 20)));
    }

    [Test]
    public void Changing_FlowDirection_relayouts()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 300, 100) };
        panel.Controls.AddRange(MakeChild(60, 20), MakeChild(60, 20));

        panel.FlowDirection = FlowDirection.TopDown;

        Assert.That(panel.Controls[1].Bounds, Is.EqualTo(new Rectangle(0, 20, 60, 20)));
    }

    [Test]
    public void Overflowing_flow_paints_a_scrollbar_and_scrolls_peers_not_logical_bounds()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 100, 50), AutoScroll = true };
        panel.Controls.AddRange(MakeChild(60, 30), MakeChild(60, 30), MakeChild(60, 30));
        var canvas = Realize(panel, out var backend);
        var firstPeer = backend.Created.OfType<HeadlessButtonPeer>().First();

        var g = canvas.RaisePaint();
        canvas.RaiseMouseWheel(-120);

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith(_TrackFill)), Is.True, "scrollbar track painted");
            Assert.That(panel.Controls[0].Bounds, Is.EqualTo(new Rectangle(0, 0, 60, 30)), "logical bounds stay put");
            Assert.That(firstPeer.Bounds, Is.EqualTo(new Rectangle(0, -40, 60, 30)), "peer moves by the clamped wheel step");
        });
    }

    [Test]
    public void Padding_insets_the_flow_and_wraps_at_the_padded_edge()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 150, 100), Padding = new(10) };
        panel.Controls.AddRange(MakeChild(60, 20), MakeChild(60, 20), MakeChild(60, 20));
        Realize(panel, out _);

        Assert.Multiple(() =>
        {
            Assert.That(panel.Controls[0].Bounds, Is.EqualTo(new Rectangle(10, 10, 60, 20)));
            Assert.That(panel.Controls[1].Bounds, Is.EqualTo(new Rectangle(70, 10, 60, 20)));
            Assert.That(panel.Controls[2].Bounds, Is.EqualTo(new Rectangle(10, 30, 60, 20)), "wraps at the 130px padded line");
        });
    }
}
