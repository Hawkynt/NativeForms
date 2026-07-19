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
    public void Resizing_the_container_relayouts_the_panels()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 100), SplitterDistance = 100 };
        Realize(split, out _);

        split.Bounds = new(0, 0, 400, 120);

        Assert.Multiple(() =>
        {
            Assert.That(split.Panel1.Bounds, Is.EqualTo(new Rectangle(0, 0, 100, 120)));
            Assert.That(split.Panel2.Bounds, Is.EqualTo(new Rectangle(104, 0, 296, 120)));
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
}
