using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="SegmentedControl"/> splits into equal cells, selects the first on <c>SetSegments</c>,
/// moves the selection on a click or the arrow keys (raising <see cref="SegmentedControl.SelectedIndexChanged"/>),
/// and paints the selected cell in the accent colour.
/// </summary>
[TestFixture]
internal sealed class SegmentedControlTests
{
    private static SegmentedControl Create(out HeadlessCanvasPeer canvas)
    {
        var seg = new SegmentedControl { Bounds = new(0, 0, 300, 28) };
        seg.SetSegments("Day", "Week", "Month");
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(seg);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return seg;
    }

    [Test]
    public void SetSegments_selects_the_first_segment()
    {
        var seg = Create(out _);

        Assert.Multiple(() =>
        {
            Assert.That(seg.SelectedIndex, Is.EqualTo(0));
            Assert.That(seg.SelectedSegment, Is.EqualTo("Day"));
        });
    }

    [Test]
    public void Clicking_a_cell_selects_it_and_raises_the_change()
    {
        var seg = Create(out var canvas);
        var changes = 0;
        seg.SelectedIndexChanged += (_, _) => ++changes;

        canvas.RaiseMouseDown(250, 14); // the third of three 100-px cells → "Month"

        Assert.Multiple(() =>
        {
            Assert.That(seg.SelectedIndex, Is.EqualTo(2));
            Assert.That(seg.SelectedSegment, Is.EqualTo("Month"));
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void Arrow_keys_move_the_selection_without_wrapping()
    {
        var seg = Create(out var canvas);

        canvas.RaiseKeyDown(Keys.Right);
        Assert.That(seg.SelectedIndex, Is.EqualTo(1));
        canvas.RaiseKeyDown(Keys.Left);
        canvas.RaiseKeyDown(Keys.Left); // already at 0 — no wrap
        Assert.That(seg.SelectedIndex, Is.EqualTo(0));
    }

    [Test]
    public void The_selected_cell_is_painted_in_the_accent()
    {
        var seg = Create(out var canvas);
        seg.SelectedIndex = 1;

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("fill #FF0078D4 100,0")), Is.True, "the second cell fills with the accent");
    }

    [Test]
    public void Ram_presets_are_one_mutually_exclusive_segment_selection()
    {
        var seg = new SegmentedControl { Bounds = new(0, 0, 500, 28) };
        seg.SetSegments("¼ RAM", "½ RAM", "1× RAM", "2 GiB", "8 GiB");
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(seg);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        canvas.RaiseMouseDown(450, 14);

        Assert.Multiple(() =>
        {
            Assert.That(seg.SelectedIndex, Is.EqualTo(4));
            Assert.That(seg.SelectedSegment, Is.EqualTo("8 GiB"));
        });
    }
}
