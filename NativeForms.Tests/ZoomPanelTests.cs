using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="ZoomPanel"/> scales and pans its content: the wheel zooms about the cursor, a left-drag
/// pans, <see cref="ZoomPanel.FitToWindow"/>/<see cref="ZoomPanel.ActualSize"/> frame it, and it draws
/// the <see cref="ZoomPanel.Image"/> plus any host <see cref="ZoomPanel.PaintContent"/> overlay.
/// </summary>
[TestFixture]
internal sealed class ZoomPanelTests
{
    private static ZoomPanel Create(out HeadlessBackend backend, out HeadlessCanvasPeer canvas)
    {
        var panel = new ZoomPanel { Bounds = new(0, 0, 200, 200) };
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(panel);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return panel;
    }

    [Test]
    public void Setting_an_image_adopts_its_size_and_fits_it_to_the_window()
    {
        var panel = Create(out var backend, out _);
        panel.Image = backend.CreateImage(400, 400, new int[400 * 400]);

        Assert.Multiple(() =>
        {
            Assert.That(panel.ContentSize, Is.EqualTo(new Size(400, 400)));
            Assert.That(panel.Zoom, Is.EqualTo(0.5).Within(0.001), "400-px content fits into the 200-px viewport at 50%");
        });
    }

    [Test]
    public void ActualSize_resets_the_zoom_to_one()
    {
        var panel = Create(out var backend, out _);
        panel.Image = backend.CreateImage(400, 400, new int[400 * 400]);

        panel.ActualSize();

        Assert.That(panel.Zoom, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void The_wheel_zooms_in_and_raises_the_change()
    {
        var panel = Create(out _, out var canvas);
        panel.ActualSize();
        var changes = 0;
        panel.ZoomChanged += (_, _) => ++changes;

        canvas.RaiseMouseWheel(120, 100, 100);

        Assert.Multiple(() =>
        {
            Assert.That(panel.Zoom, Is.GreaterThan(1.0), "a positive notch zooms in");
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void The_zoom_is_clamped_to_its_bounds()
    {
        var panel = Create(out _, out _);
        panel.MaxZoom = 4;
        panel.MinZoom = 0.5;

        panel.Zoom = 100;
        Assert.That(panel.Zoom, Is.EqualTo(4.0).Within(0.001));

        panel.Zoom = 0.001;
        Assert.That(panel.Zoom, Is.EqualTo(0.5).Within(0.001));
    }

    [Test]
    public void A_left_drag_pans_and_moves_the_image()
    {
        var panel = Create(out var backend, out var canvas);
        panel.Image = backend.CreateImage(100, 100, new int[100 * 100]);
        panel.ActualSize();

        var before = FirstImageRect(canvas);
        canvas.RaiseMouseDown(100, 100);
        canvas.RaiseMouseMove(140, 130);
        canvas.RaiseMouseUp(140, 130);
        var after = FirstImageRect(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(after.X - before.X, Is.EqualTo(40), "panned 40 px right");
            Assert.That(after.Y - before.Y, Is.EqualTo(30), "panned 30 px down");
        });
    }

    [Test]
    public void PaintContent_receives_a_content_to_view_mapping()
    {
        var panel = Create(out _, out var canvas);
        panel.ContentSize = new Size(100, 100);
        panel.ActualSize();
        PointF? mapped = null;
        panel.PaintContent += (_, e) => mapped = e.ToView(new PointF(10, 10));

        canvas.RaisePaint();

        Assert.That(mapped, Is.Not.Null, "the host overlay ran");
    }

    private static Rectangle FirstImageRect(HeadlessCanvasPeer canvas)
    {
        var g = canvas.RaisePaint();
        var op = g.Operations.First(o => o.StartsWith("image"));
        // "image WxH @x,y,w,h"
        var at = op.Split('@')[1].Split(',');
        return new Rectangle(int.Parse(at[0]), int.Parse(at[1]), int.Parse(at[2]), int.Parse(at[3]));
    }
}
