using System.Drawing;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A container must never paint outside its client rectangle — neither its own drawing nor the
/// native children it hosts. On GTK a panel holding content larger than itself was allocated the
/// content's bounding box, so its children were drawn straight over the controls beside it; the
/// toolkit-side half of the guarantee is the clip <see cref="OwnerDrawnControl"/> pushes before
/// handing the surface to <c>OnPaint</c>, which holds on every backend.
/// </summary>
[TestFixture]
internal sealed class ContainerClippingTests
{
    /// <summary>A control that deliberately paints well past its own bounds.</summary>
    private sealed class OverflowingControl : OwnerDrawnControl
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.FillRectangle(Color.Red, new Rectangle(0, 0, this.Width, this.Height));

            // Far outside the client rectangle on every side.
            e.Graphics.FillRectangle(Color.Lime, new Rectangle(this.Width + 10, 4, 60, 20));
            e.Graphics.DrawText("spill", DefaultTheme.Instance.DefaultFont, Color.Blue, new Rectangle(4, this.Height + 10, 60, 16));
        }
    }

    private static HeadlessCanvasPeer Realize(Control control, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    [Test]
    public void A_controls_paint_is_clipped_to_its_client_rectangle()
    {
        var control = new OverflowingControl { Bounds = new(0, 0, 200, 100) };
        var canvas = Realize(control, out _);

        var graphics = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(
                graphics.Operations[0],
                Is.EqualTo("clip 0,0,200,100"),
                "the client rectangle is clipped before the subclass paints");
            Assert.That(graphics.Operations[^1], Is.EqualTo("unclip"), "and released afterwards");
            Assert.That(
                graphics.ClippedOperations.Exists(o => o.Contains("FF00FF00")),
                Is.False,
                "the fill past the right edge never reaches the surface");
            Assert.That(
                graphics.DrewTextClipped("spill"),
                Is.False,
                "nor does the text below the bottom edge");
            Assert.That(
                graphics.ClippedOperations.Exists(o => o.Contains("FFFF0000")),
                Is.True,
                "while the in-bounds background still paints");
        });
    }

    [Test]
    public void Painting_outside_the_client_rectangle_is_detectable()
    {
        var control = new OverflowingControl { Bounds = new(0, 0, 200, 100) };
        var canvas = Realize(control, out _);

        var graphics = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(graphics.StayedInBounds, Is.False);
            Assert.That(graphics.OutOfBoundsOperations, Has.Count.EqualTo(2));
            Assert.That(graphics.CurrentClip, Is.EqualTo(new Rectangle(0, 0, 200, 100)).Or.EqualTo(graphics.Surface));
        });
    }

    [Test]
    public void A_well_behaved_control_stays_inside_its_client_rectangle()
    {
        var panel = new Panel { Bounds = new(0, 0, 120, 80), BorderStyle = BorderStyle.FixedSingle };
        var canvas = Realize(panel, out _);

        var graphics = canvas.RaisePaint();

        Assert.That(graphics.StayedInBounds, Is.True, string.Join(" | ", graphics.OutOfBoundsOperations));
    }

    [Test]
    public void Children_scrolled_out_of_an_autoscroll_panel_leave_the_client_rectangle()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 60), AutoScroll = true };
        var first = new Button { Bounds = new(0, 0, 80, 30) };
        var second = new Button { Bounds = new(0, 120, 80, 30) };
        panel.Controls.Add(first);
        panel.Controls.Add(second);
        var canvas = Realize(panel, out _);

        Assert.That(canvas.ChildrenOutsideClientRectangle, Has.Count.EqualTo(1), "the far child starts out of view");

        panel.AutoScrollPosition = new Point(0, 90);

        Assert.Multiple(() =>
        {
            Assert.That(panel.AutoScrollPosition.Y, Is.EqualTo(-90));
            Assert.That(
                canvas.ChildrenOutsideClientRectangle,
                Has.Count.EqualTo(1),
                "the first child is now scrolled above the panel and must be clipped, not drawn over its neighbours");
            Assert.That(first.Bounds, Is.EqualTo(new Rectangle(0, 0, 80, 30)), "logical bounds stay untouched");
        });
    }

    [Test]
    public void An_autoscroll_panels_own_paint_stays_inside_the_client_rectangle_while_scrolled()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 60), AutoScroll = true };
        panel.Controls.Add(new Button { Bounds = new(0, 0, 400, 300) });
        var canvas = Realize(panel, out _);

        panel.AutoScrollPosition = new Point(40, 40);
        var graphics = canvas.RaisePaint();

        Assert.That(
            graphics.StayedInBounds,
            Is.True,
            "scrollbars and background must not bleed past the panel: " + string.Join(" | ", graphics.OutOfBoundsOperations));
    }
}
