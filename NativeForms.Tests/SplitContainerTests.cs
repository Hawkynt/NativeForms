using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="SplitContainer"/> must realize <see cref="SplitContainer.Panel1"/>/<c>Panel2</c> as real
/// nested children, lay them out on either side of the splitter for both orientations, clamp
/// <see cref="SplitContainer.SplitterDistance"/> to the panel minimum sizes, and move the splitter by
/// mouse drag (committing with <see cref="SplitContainer.SplitterMoved"/>) and by arrow keys.
/// </summary>
[TestFixture]
internal sealed class SplitContainerTests
{
    private static HeadlessCanvasPeer Realize(SplitContainer split, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(split);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    [Test]
    public void Panels_realize_as_children_and_sit_side_by_side_when_vertical()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100 };
        var canvas = Realize(split, out var backend);

        var panelCanvases = backend.Created.OfType<HeadlessCanvasPeer>().Skip(1).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(canvas.Children, Has.Count.EqualTo(2));
            Assert.That(panelCanvases[0].Bounds, Is.EqualTo(new Rectangle(0, 0, 100, 100)));
            Assert.That(panelCanvases[1].Bounds, Is.EqualTo(new Rectangle(104, 0, 196, 100)));
        });
    }

    [Test]
    public void Horizontal_orientation_stacks_the_panels()
    {
        var split = new SplitContainer
        {
            Bounds = new(0, 0, 200, 300),
            Orientation = Orientation.Horizontal,
            SplitterDistance = 120,
        };
        Realize(split, out _);

        Assert.Multiple(() =>
        {
            Assert.That(split.Panel1.Bounds, Is.EqualTo(new Rectangle(0, 0, 200, 120)));
            Assert.That(split.Panel2.Bounds, Is.EqualTo(new Rectangle(0, 124, 200, 176)));
        });
    }

    [Test]
    public void SplitterDistance_clamps_to_both_minimum_sizes()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100) };

        split.SplitterDistance = 5;
        Assert.That(split.SplitterDistance, Is.EqualTo(25), "Panel1MinSize floor");

        split.SplitterDistance = 290;
        Assert.That(split.SplitterDistance, Is.EqualTo(300 - 4 - 25), "Panel2MinSize ceiling");
    }

    [Test]
    public void Dragging_the_splitter_relayouts_live_and_raises_SplitterMoved_on_release()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100 };
        var moves = 0;
        split.SplitterMoved += (_, _) => ++moves;
        var canvas = Realize(split, out _);

        canvas.RaiseMouseDown(102, 50); // inside the 100..104 splitter zone
        canvas.RaiseMouseMove(152, 50);

        Assert.Multiple(() =>
        {
            Assert.That(split.SplitterDistance, Is.EqualTo(150), "live drag keeps the grab offset");
            Assert.That(split.Panel1.Bounds.Width, Is.EqualTo(150));
            Assert.That(split.Panel2.Bounds, Is.EqualTo(new Rectangle(154, 0, 146, 100)));
            Assert.That(moves, Is.Zero, "no commit before release");
        });

        canvas.RaiseMouseUp(152, 50);
        Assert.That(moves, Is.EqualTo(1));
    }

    [Test]
    public void Drag_respects_the_minimum_sizes()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100 };
        var canvas = Realize(split, out _);

        canvas.RaiseMouseDown(102, 50);
        canvas.RaiseMouseMove(2, 50);
        canvas.RaiseMouseUp(2, 50);

        Assert.That(split.SplitterDistance, Is.EqualTo(25));
    }

    [Test]
    public void Resizing_the_container_scales_the_distance_proportionally_by_default()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100 };
        Realize(split, out _);

        split.Bounds = new(0, 0, 400, 120);

        Assert.Multiple(() =>
        {
            Assert.That(split.SplitterDistance, Is.EqualTo(133), "FixedPanel.None keeps 100/300 of the axis");
            Assert.That(split.Panel1.Bounds, Is.EqualTo(new Rectangle(0, 0, 133, 120)));
            Assert.That(split.Panel2.Bounds, Is.EqualTo(new Rectangle(137, 0, 263, 120)));
        });
    }

    [Test]
    public void Arrow_keys_move_the_splitter_and_raise_SplitterMoved()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100 };
        var moves = 0;
        split.SplitterMoved += (_, _) => ++moves;
        var canvas = Realize(split, out _);

        canvas.RaiseKeyDown(Keys.Right);
        Assert.Multiple(() =>
        {
            Assert.That(split.SplitterDistance, Is.EqualTo(108));
            Assert.That(moves, Is.EqualTo(1));
        });

        canvas.RaiseKeyDown(Keys.Left);
        Assert.That(split.SplitterDistance, Is.EqualTo(100));
    }

    [Test]
    public void FixedPanel1_keeps_panel1_size_on_resize()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100, FixedPanel = FixedPanel.Panel1 };
        Realize(split, out _);

        split.Bounds = new(0, 0, 400, 100);

        Assert.Multiple(() =>
        {
            Assert.That(split.SplitterDistance, Is.EqualTo(100));
            Assert.That(split.Panel2.Bounds, Is.EqualTo(new Rectangle(104, 0, 296, 100)), "panel2 absorbs the growth");
        });
    }

    [Test]
    public void FixedPanel2_keeps_panel2_size_on_resize()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100, FixedPanel = FixedPanel.Panel2 };
        Realize(split, out _);

        split.Bounds = new(0, 0, 400, 100);

        Assert.Multiple(() =>
        {
            Assert.That(split.SplitterDistance, Is.EqualTo(200), "panel1 absorbs the growth");
            Assert.That(split.Panel2.Bounds, Is.EqualTo(new Rectangle(204, 0, 196, 100)), "panel2 keeps its 196px");
        });
    }

    [Test]
    public void Collapsing_panel1_hides_its_peer_and_gives_panel2_the_whole_area()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100 };
        Realize(split, out var backend);
        var panelCanvases = backend.Created.OfType<HeadlessCanvasPeer>().Skip(1).ToArray();

        split.Panel1Collapsed = true;

        Assert.Multiple(() =>
        {
            Assert.That(panelCanvases[0].Visible, Is.False, "panel1's peer hides");
            Assert.That(split.Panel1.Visible, Is.True, "its logical visibility stays untouched");
            Assert.That(split.Panel2.Bounds, Is.EqualTo(new Rectangle(0, 0, 300, 100)));
        });

        split.Panel1Collapsed = false;
        Assert.Multiple(() =>
        {
            Assert.That(panelCanvases[0].Visible, Is.True, "un-collapsing restores the peer");
            Assert.That(split.Panel1.Bounds, Is.EqualTo(new Rectangle(0, 0, 100, 100)));
            Assert.That(split.Panel2.Bounds, Is.EqualTo(new Rectangle(104, 0, 196, 100)));
        });
    }

    [Test]
    public void Collapsing_panel2_gives_panel1_the_whole_area()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100 };
        Realize(split, out var backend);
        var panelCanvases = backend.Created.OfType<HeadlessCanvasPeer>().Skip(1).ToArray();

        split.Panel2Collapsed = true;

        Assert.Multiple(() =>
        {
            Assert.That(panelCanvases[1].Visible, Is.False);
            Assert.That(split.Panel1.Bounds, Is.EqualTo(new Rectangle(0, 0, 300, 100)));
        });
    }

    [Test]
    public void Collapsing_one_panel_uncollapses_the_other()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100) };

        split.Panel1Collapsed = true;
        split.Panel2Collapsed = true;

        Assert.Multiple(() =>
        {
            Assert.That(split.Panel1Collapsed, Is.False, "collapsing panel2 releases panel1");
            Assert.That(split.Panel2Collapsed, Is.True);
        });
    }

    // --- The splitter band's cursor -----------------------------------------------------------
    //
    // The band is a region of the container's own surface, not a child control, so Control.Cursor
    // cannot express it: setting that would tint the whole control and every child. It is pushed as a
    // region cursor while the pointer is over the band and released again on the way out.

    [Test]
    public void The_splitter_band_shows_the_horizontal_sizing_cursor()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100, SplitterWidth = 6 };
        var canvas = Realize(split, out _);

        canvas.RaiseMouseMove(103, 50);

        Assert.That(canvas.Cursor, Is.SameAs(Cursors.SizeWE));
    }

    [Test]
    public void A_horizontal_split_shows_the_vertical_sizing_cursor()
    {
        var split = new SplitContainer
        {
            Bounds = new(0, 0, 100, 300),
            Orientation = Orientation.Horizontal,
            SplitterDistance = 100,
            SplitterWidth = 6,
        };
        var canvas = Realize(split, out _);

        canvas.RaiseMouseMove(50, 103);

        Assert.That(canvas.Cursor, Is.SameAs(Cursors.SizeNS));
    }

    [Test]
    public void Moving_off_the_splitter_band_restores_the_cursor()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100, SplitterWidth = 6 };
        var canvas = Realize(split, out _);

        canvas.RaiseMouseMove(103, 50);
        canvas.RaiseMouseMove(40, 50);

        Assert.That(canvas.Cursor, Is.SameAs(Cursors.Arrow));
    }

    [Test]
    public void Leaving_the_control_restores_the_cursor()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100, SplitterWidth = 6 };
        var canvas = Realize(split, out _);

        canvas.RaiseMouseMove(103, 50);
        canvas.RaiseMouseLeave();

        Assert.That(canvas.Cursor, Is.SameAs(Cursors.Arrow));
    }

    [Test]
    public void The_sizing_cursor_survives_a_drag_that_runs_past_the_band()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100, SplitterWidth = 6 };
        var canvas = Realize(split, out _);

        canvas.RaiseMouseMove(103, 50);
        canvas.RaiseMouseDown(103, 50);
        canvas.RaiseMouseMove(160, 50);

        Assert.That(canvas.Cursor, Is.SameAs(Cursors.SizeWE), "the pointer routinely runs ahead of the bar mid-drag");
    }

    [Test]
    public void Releasing_a_drag_away_from_the_band_restores_the_cursor()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100, SplitterWidth = 6 };
        var canvas = Realize(split, out _);

        canvas.RaiseMouseMove(103, 50);
        canvas.RaiseMouseDown(103, 50);
        canvas.RaiseMouseMove(160, 50);
        canvas.RaiseMouseUp(20, 50);

        Assert.That(canvas.Cursor, Is.SameAs(Cursors.Arrow));
    }
}
