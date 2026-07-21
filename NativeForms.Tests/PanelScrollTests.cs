using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Panel.AutoScroll"/> must paint themed scrollbars only while children overflow the
/// client area, and scroll by physically moving the child peers: the child's logical
/// <see cref="Control.Bounds"/> never changes, only the rectangle pushed to its peer does.
/// </summary>
[TestFixture]
internal sealed class PanelScrollTests
{
    // DefaultTheme metrics the expectations below are built on.
    private const int _ScrollBarSize = 16;
    private const int _WheelStep = 3 * 22;

    private static readonly string _TrackFill = "fill #FFECECEC"; // HeaderBackground = scrollbar track

    private static HeadlessCanvasPeer Realize(Panel panel, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(panel);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    [Test]
    public void No_scrollbars_paint_while_content_fits()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 100), AutoScroll = true };
        panel.Controls.Add(new Button { Bounds = new(10, 10, 60, 30) });
        var canvas = Realize(panel, out _);

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith(_TrackFill)), Is.False);
    }

    [Test]
    public void Vertical_scrollbar_paints_when_children_overflow_below()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 100), AutoScroll = true };
        panel.Controls.Add(new Button { Bounds = new(10, 150, 60, 30) });
        var canvas = Realize(panel, out _);

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith(_TrackFill)), Is.True);
    }

    [Test]
    public void Wheel_scrolls_children_by_moving_their_peers_and_keeps_logical_bounds()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 100), AutoScroll = true };
        var button = new Button { Bounds = new(10, 150, 60, 30) };
        panel.Controls.Add(button);
        var canvas = Realize(panel, out var backend);
        var buttonPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();

        canvas.RaiseMouseWheel(-120);

        Assert.Multiple(() =>
        {
            Assert.That(panel.AutoScrollPosition, Is.EqualTo(new Point(0, -_WheelStep)));
            Assert.That(button.Bounds, Is.EqualTo(new Rectangle(10, 150, 60, 30)), "logical bounds stay put");
            Assert.That(buttonPeer.Bounds, Is.EqualTo(new Rectangle(10, 150 - _WheelStep, 60, 30)), "peer moves");
        });
    }

    [Test]
    public void Wheel_clamps_at_the_content_extent()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 100), AutoScroll = true };
        panel.Controls.Add(new Button { Bounds = new(10, 150, 60, 30) });
        var canvas = Realize(panel, out _);

        canvas.RaiseMouseWheel(-120);
        canvas.RaiseMouseWheel(-120);
        canvas.RaiseMouseWheel(-120);

        // Extent 180, viewport 100 -> maximum scroll 80.
        Assert.That(panel.AutoScrollPosition, Is.EqualTo(new Point(0, -80)));

        canvas.RaiseMouseWheel(120);
        canvas.RaiseMouseWheel(120);

        Assert.That(panel.AutoScrollPosition, Is.EqualTo(new Point(0, 0)));
    }

    [Test]
    public void Thumb_drag_updates_AutoScrollPosition()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 100), AutoScroll = true };
        panel.Controls.Add(new Button { Bounds = new(10, 150, 60, 30) });
        var canvas = Realize(panel, out _);

        // Extent 180, viewport 100: the vertical track is the right 16px column.
        var track = new Rectangle(100 - _ScrollBarSize, 0, _ScrollBarSize, 100);
        var thumb = Drawing.ScrollBarRenderer.GetThumb(track, vertical: true, extent: 180, viewport: 100, position: 0);
        canvas.RaiseMouseDown(thumb.X + (thumb.Width / 2), thumb.Y + 2);
        canvas.RaiseMouseMove(thumb.X + (thumb.Width / 2), thumb.Y + 22);
        canvas.RaiseMouseUp(thumb.X + (thumb.Width / 2), thumb.Y + 22);

        var expected = Drawing.ScrollBarRenderer.PositionFromThumbDelta(track, vertical: true, extent: 180, viewport: 100, startPosition: 0, pixelDelta: 20);
        Assert.Multiple(() =>
        {
            Assert.That(expected, Is.GreaterThan(0), "sanity: the drag must map to a scroll");
            Assert.That(panel.AutoScrollPosition, Is.EqualTo(new Point(0, -expected)));
        });
    }

    [Test]
    public void Horizontal_overflow_scrolls_the_x_axis_via_AutoScrollPosition()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 100), AutoScroll = true };
        var button = new Button { Bounds = new(150, 10, 60, 20) };
        panel.Controls.Add(button);
        Realize(panel, out var backend);
        var buttonPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();

        panel.AutoScrollPosition = new(-50, 0);

        Assert.Multiple(() =>
        {
            Assert.That(panel.AutoScrollPosition, Is.EqualTo(new Point(-50, 0)));
            Assert.That(buttonPeer.Bounds, Is.EqualTo(new Rectangle(100, 10, 60, 20)));
        });
    }

    [Test]
    public void Panel_smaller_than_a_scrollbar_still_paints_without_throwing()
    {
        var panel = new Panel { Bounds = new(0, 0, 10, 10), AutoScroll = true };
        panel.Controls.Add(new Button { Bounds = new(0, 0, 50, 50) });
        var canvas = Realize(panel, out _);

        Assert.DoesNotThrow(() => canvas.RaisePaint());
    }

    [Test]
    public void Disabling_AutoScroll_resets_the_offset_and_restores_peer_bounds()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 100), AutoScroll = true };
        var button = new Button { Bounds = new(10, 150, 60, 30) };
        panel.Controls.Add(button);
        var canvas = Realize(panel, out var backend);
        var buttonPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();
        canvas.RaiseMouseWheel(-120);

        panel.AutoScroll = false;

        Assert.Multiple(() =>
        {
            Assert.That(panel.AutoScrollPosition, Is.EqualTo(Point.Empty));
            Assert.That(buttonPeer.Bounds, Is.EqualTo(new Rectangle(10, 150, 60, 30)));
        });
    }

    /// <summary>
    /// Showing a scrollbar has to shrink the client area, exactly like Windows Forms: otherwise the
    /// Anchor/Dock engine lays children out across the whole width and a docked child ends up
    /// underneath the bar, where — its peer being a native window stacked above the container's own
    /// surface — it swallows every press aimed at the thumb.
    /// </summary>
    [Test]
    public void Display_rectangle_reserves_the_band_of_each_visible_scrollbar()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 100), AutoScroll = true };
        var tall = new Button { Bounds = new(0, 0, 10, 400) };
        panel.Controls.Add(tall);

        Assert.That(
            panel.DisplayRectangle,
            Is.EqualTo(new Rectangle(0, 0, 100 - _ScrollBarSize, 100)),
            "the vertical bar alone takes width, never height");

        // Now the content also overflows horizontally, so both bars show and both bands are reserved.
        panel.Controls.Add(new Button { Bounds = new(0, 0, 400, 10) });

        Assert.That(
            panel.DisplayRectangle,
            Is.EqualTo(new Rectangle(0, 0, 100 - _ScrollBarSize, 100 - _ScrollBarSize)),
            "each bar takes its own band");
    }

    /// <summary>A panel that scrolls nothing keeps the plain padded client area.</summary>
    [Test]
    public void Display_rectangle_reserves_nothing_while_no_scrollbar_shows()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 100), AutoScroll = true };
        panel.Controls.Add(new Button { Bounds = new(10, 10, 60, 30) });

        Assert.That(panel.DisplayRectangle, Is.EqualTo(new Rectangle(0, 0, 100, 100)));
    }

    /// <summary>
    /// A child placed at absolute coordinates is never touched by the layout engine, so
    /// <see cref="Control.DisplayRectangle"/> alone cannot keep it off the strip — its peer
    /// rectangle has to be pulled back to the viewport as well.
    /// </summary>
    [Test]
    public void A_child_overhanging_the_vertical_bar_has_its_peer_pulled_off_the_band()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 100), AutoScroll = true };
        panel.Controls.Add(new Button { Bounds = new(0, 0, 10, 400) });
        var wide = new Button { Bounds = new(0, 200, 98, 20) };
        panel.Controls.Add(wide);
        Realize(panel, out var backend);
        var widePeer = backend.Created.OfType<HeadlessButtonPeer>().Last();

        Assert.Multiple(() =>
        {
            Assert.That(
                widePeer.Bounds.Right,
                Is.EqualTo(100 - _ScrollBarSize),
                "the peer stops at the viewport, leaving the strip clear");
            Assert.That(
                wide.Bounds,
                Is.EqualTo(new Rectangle(0, 200, 98, 20)),
                "the child's logical bounds are untouched");
        });
    }

    /// <summary>
    /// The pull-back applies only to an edge facing a bar that is actually shown. Without a
    /// horizontal bar a child must keep its full height while it scrolls past the bottom edge —
    /// clipping there is the parent window's job, and trimming instead would visibly shrink the
    /// widget.
    /// </summary>
    [Test]
    public void A_child_below_the_viewport_keeps_its_height_while_no_horizontal_bar_shows()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 100), AutoScroll = true };
        var low = new Button { Bounds = new(10, 90, 60, 30) };
        panel.Controls.Add(low);
        Realize(panel, out var backend);
        var lowPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();

        Assert.That(lowPeer.Bounds, Is.EqualTo(new Rectangle(10, 90, 60, 30)));
    }
}
