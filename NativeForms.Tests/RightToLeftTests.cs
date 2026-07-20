using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The right-to-left honest subset (PRD §8): the ambient <see cref="Control.RightToLeft"/> property
/// resolving through the parent chain, the shared mirroring helper, and the mirrored painting of
/// the adopted owner-drawn faces (CheckBox, RadioButton, ToggleSwitch, LinkLabel). Container
/// layout deliberately does not mirror yet.
/// </summary>
[TestFixture]
internal sealed class RightToLeftTests
{
    private static HeadlessCanvasPeer Realize(Control control, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 300, 200) };
        form.Controls.Add(control);
        form.RealizeWindow(backend);
        return (HeadlessCanvasPeer)control.Peer!;
    }

    [Test]
    public void RightToLeft_defaults_to_Inherit_and_resolves_to_No()
    {
        var checkBox = new CheckBox();

        Assert.Multiple(() =>
        {
            Assert.That(checkBox.RightToLeft, Is.EqualTo(RightToLeft.Inherit));
            Assert.That(checkBox.IsRightToLeft, Is.False);
        });
    }

    [Test]
    public void Inherit_resolves_through_the_parent_chain()
    {
        var form = new Form { RightToLeft = RightToLeft.Yes };
        var panel = new Panel();
        var checkBox = new CheckBox();
        form.Controls.Add(panel);
        panel.Controls.Add(checkBox);

        Assert.That(checkBox.IsRightToLeft, Is.True, "the form's Yes flows down two levels of Inherit");
    }

    [Test]
    public void An_explicit_No_overrides_an_inherited_Yes()
    {
        var form = new Form { RightToLeft = RightToLeft.Yes };
        var checkBox = new CheckBox { RightToLeft = RightToLeft.No };
        form.Controls.Add(checkBox);

        Assert.That(checkBox.IsRightToLeft, Is.False);
    }

    [Test]
    public void Changing_an_ancestors_direction_repaints_inheriting_owner_drawn_children()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 300, 200) };
        var checkBox = new CheckBox { Bounds = new(0, 0, 120, 24) };
        form.Controls.Add(checkBox);
        form.RealizeWindow(backend);
        var canvas = (HeadlessCanvasPeer)checkBox.Peer!;
        var before = canvas.InvalidateCount;

        form.RightToLeft = RightToLeft.Yes;

        Assert.That(canvas.InvalidateCount, Is.GreaterThan(before));
    }

    [Test]
    public void Mirror_helper_flips_rectangles_and_alignments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RtlLayout.Mirror(new Rectangle(0, 4, 14, 14), 120), Is.EqualTo(new Rectangle(106, 4, 14, 14)));
            Assert.That(RtlLayout.Mirror(new Rectangle(106, 4, 14, 14), 120), Is.EqualTo(new Rectangle(0, 4, 14, 14)));
            Assert.That(RtlLayout.Mirror(ContentAlignment.MiddleLeft), Is.EqualTo(ContentAlignment.MiddleRight));
            Assert.That(RtlLayout.Mirror(ContentAlignment.TopRight), Is.EqualTo(ContentAlignment.TopLeft));
            Assert.That(RtlLayout.Mirror(ContentAlignment.BottomCenter), Is.EqualTo(ContentAlignment.BottomCenter));
        });
    }

    [Test]
    public void CheckBox_paints_its_box_at_the_right_edge_when_mirrored()
    {
        var checkBox = new CheckBox { Bounds = new(0, 0, 120, 24), Text = "Check", RightToLeft = RightToLeft.Yes };
        var canvas = Realize(checkBox, out _);

        var g = canvas.RaisePaint();

        var boxX = 120 - GlyphRenderer.CheckBoxSize;
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("rect ") && o.Contains($"{boxX},")), Is.True, "the check square sits at the right edge");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text ") && o.Contains("MiddleRight")), Is.True, "the caption anchors toward the square");
        });
    }

    [Test]
    public void RadioButton_paints_its_ring_at_the_right_edge_when_mirrored()
    {
        var radio = new RadioButton { Bounds = new(0, 0, 120, 24), Text = "Pick", RightToLeft = RightToLeft.Yes };
        var canvas = Realize(radio, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("ellipse ") && o.Contains("106,")), Is.True, "the 14px ring sits at x = 120 - 14");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text ") && o.Contains("MiddleRight")), Is.True);
        });
    }

    [Test]
    public void ToggleSwitch_paints_its_track_at_the_right_edge_when_mirrored()
    {
        var toggle = new ToggleSwitch { Bounds = new(0, 0, 120, 24), Text = "Wifi", RightToLeft = RightToLeft.Yes };
        var canvas = Realize(toggle, out _);

        var g = canvas.RaisePaint();

        var trackX = 120 - ToggleSwitch.TrackWidth;
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("fillround ") && o.Contains($"{trackX},")), Is.True, "the track pill starts at x = 120 - 36");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text ") && o.Contains("MiddleRight")), Is.True, "the caption sits left of the track, anchored toward it");
        });
    }

    [Test]
    public void ToggleSwitch_thumb_sides_swap_when_mirrored()
    {
        var toggle = new ToggleSwitch { Bounds = new(0, 0, 120, 24), RightToLeft = RightToLeft.Yes, Checked = true };
        var canvas = Realize(toggle, out _);

        var g = canvas.RaisePaint();

        // In RTL the track spans x 84…120 and "on" means the thumb hugs the LEFT (mirrored far) end.
        var trackX = 120 - ToggleSwitch.TrackWidth;
        Assert.That(g.Operations.Exists(o => o.StartsWith("ellipse ") && o.Contains($"{trackX + 2},")), Is.True);
    }

    [Test]
    public void LinkLabel_anchors_text_and_underline_at_the_right_when_mirrored()
    {
        var link = new LinkLabel { Bounds = new(0, 0, 120, 24), Text = "Go", RightToLeft = RightToLeft.Yes };
        var canvas = Realize(link, out _);

        var g = canvas.RaisePaint();

        // RecordingGraphics measures 7px per character: "Go" is 14px wide, so it anchors at x = 106.
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("text ") && o.Contains("MiddleRight")), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("line ") && o.Contains("106,")), Is.True, "the underline starts under the mirrored text");
        });
    }

    [Test]
    public void LinkLabel_hit_testing_follows_the_mirrored_text()
    {
        var link = new LinkLabel { Bounds = new(0, 0, 120, 24), Text = "Go", RightToLeft = RightToLeft.Yes };
        var canvas = Realize(link, out _);
        var clicks = 0;
        link.LinkClicked += (_, _) => ++clicks;

        canvas.RaiseMouseUp(5, 12);   // the old (left) position — now empty space
        canvas.RaiseMouseUp(110, 12); // inside the mirrored text extent

        Assert.That(clicks, Is.EqualTo(1));
    }
}
