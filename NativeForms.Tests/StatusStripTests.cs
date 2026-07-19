using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class StatusStripTests
{
    /// <summary>Realizes a 300×24 status bar on a fresh form and returns its canvas.</summary>
    private static StatusStrip CreateStrip(out HeadlessCanvasPeer canvas, out HeadlessBackend backend)
    {
        var strip = new StatusStrip { Bounds = new(0, 0, 300, 24) };
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(strip);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return strip;
    }

    [Test]
    public void Labels_paint_caption_and_icon()
    {
        var strip = CreateStrip(out var canvas, out var backend);
        strip.Items.Add(new ToolStripStatusLabel("Ready") { Image = backend.CreateImage(16, 16, new int[256]) });

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Ready"), Is.True);
            // Icon at panel padding, vertically centered: (4, (24-16)/2) = (4, 4).
            Assert.That(g.Operations.Exists(o => o.StartsWith("image 16x16 @4,4")), Is.True);
        });
    }

    [Test]
    public void Spring_panel_absorbs_the_remaining_width()
    {
        var strip = CreateStrip(out _, out _);
        strip.Items.AddRange(
            new ToolStripStatusLabel("A"),                   // fixed: 8 + 7 = 15
            new ToolStripStatusLabel("B") { Spring = true },
            new ToolStripStatusLabel("C"));                  // fixed: 15

        // 300 - 15 - 15 - 14 (size grip) = 256 for the single spring.
        Assert.Multiple(() =>
        {
            Assert.That(strip.GetItemWidth(0), Is.EqualTo(15));
            Assert.That(strip.GetItemWidth(1), Is.EqualTo(256));
            Assert.That(strip.GetItemWidth(2), Is.EqualTo(15));
        });
    }

    [Test]
    public void Several_springs_share_the_leftover_equally()
    {
        var strip = CreateStrip(out _, out _);
        strip.Items.AddRange(
            new ToolStripStatusLabel("A"),                   // fixed: 15
            new ToolStripStatusLabel { Spring = true },
            new ToolStripStatusLabel { Spring = true });

        // 300 - 15 - 14 = 271 → 135 each, the first spring takes the odd pixel.
        Assert.Multiple(() =>
        {
            Assert.That(strip.GetItemWidth(1), Is.EqualTo(136));
            Assert.That(strip.GetItemWidth(2), Is.EqualTo(135));
        });
    }

    [Test]
    public void Hiding_the_grip_returns_its_width_to_the_springs()
    {
        var strip = CreateStrip(out _, out _);
        strip.SizingGrip = false;
        strip.Items.Add(new ToolStripStatusLabel { Spring = true });

        Assert.That(strip.GetItemWidth(0), Is.EqualTo(300));
    }

    [Test]
    public void Spring_panel_paints_at_its_computed_bounds()
    {
        var strip = CreateStrip(out var canvas, out _);
        strip.Items.AddRange(
            new ToolStripStatusLabel("A"),
            new ToolStripStatusLabel("Mid") { Spring = true },
            new ToolStripStatusLabel("C"));

        var g = canvas.RaisePaint();

        // "Mid" starts after the 15px fixed panel, padded by 4.
        Assert.That(g.Operations.Exists(static o => o.StartsWith("text \"Mid\"") && o.Contains("@19,")), Is.True);
    }

    [Test]
    public void Progress_panel_paints_through_the_shared_progress_renderer()
    {
        var strip = CreateStrip(out var canvas, out _);
        strip.Items.Add(new ToolStripProgressBarItem { Value = 50 }); // default width 100, range 0..100

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            // The gauge is inset (2, 3): field track at (2, 3, 96, 18) …
            Assert.That(g.Operations, Does.Contain("fill #FFFFFFFF 2,3,96,18"));
            // … with the accent fill covering (96 - 2) * 50 / 100 = 47 pixels.
            Assert.That(g.Operations, Does.Contain("fill #FF0078D4 3,4,47,16"));
            Assert.That(g.Operations, Does.Contain("rect #FFC8C8C8 2,3,95,17"));
        });
    }

    [Test]
    public void Progress_panel_clamps_its_value()
    {
        var progress = new ToolStripProgressBarItem { Maximum = 10 };

        progress.Value = 42;
        Assert.That(progress.Value, Is.EqualTo(10));

        progress.Value = -5;
        Assert.That(progress.Value, Is.Zero);
    }

    [Test]
    public void Size_grip_paints_its_dot_pattern_bottom_right()
    {
        CreateStrip(out var canvas, out _).Items.Add(new ToolStripStatusLabel("Ready"));

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            // The outermost diagonal dots of the grip in the 300×24 corner.
            Assert.That(g.Operations, Does.Contain("fill #FF9A9A9A 297,21,2,2"));
            Assert.That(g.Operations, Does.Contain("fill #FF9A9A9A 289,21,2,2"));
            Assert.That(g.Operations, Does.Contain("fill #FF9A9A9A 297,13,2,2"));
        });
    }

    [Test]
    public void Size_grip_can_be_hidden()
    {
        var strip = CreateStrip(out var canvas, out _);
        strip.SizingGrip = false;

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(static o => o.Contains("297,21")), Is.False);
    }
}
