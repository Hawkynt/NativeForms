using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class LinkLabelTests
{
    // DefaultTheme.Accent as the recording graphics formats it.
    private const string _Accent = "#FF0078D4";

    private static HeadlessCanvasPeer Realize(LinkLabel link)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(link);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    [Test]
    public void Paints_text_in_accent_color_with_underline()
    {
        // "Link" measures 28x16 in the headless backend; MiddleLeft in a 30px-high control puts it at y = 7.
        var link = new LinkLabel { Text = "Link", Bounds = new(0, 0, 120, 30) };
        var canvas = Realize(link);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith($"text \"Link\" {_Accent}")), Is.True, "accent text");
            Assert.That(g.Operations.Exists(o => o == $"line {_Accent} 0,22-28,22"), Is.True, "underline under the text extent");
        });
    }

    [Test]
    public void MouseUp_inside_text_extent_raises_LinkClicked()
    {
        var link = new LinkLabel { Text = "Link", Bounds = new(0, 0, 120, 30) };
        var clicks = 0;
        link.LinkClicked += (_, _) => ++clicks;
        var canvas = Realize(link);

        canvas.RaiseMouseUp(10, 15);

        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void MouseUp_outside_text_extent_does_not_raise()
    {
        var link = new LinkLabel { Text = "Link", Bounds = new(0, 0, 120, 30) };
        var clicks = 0;
        link.LinkClicked += (_, _) => ++clicks;
        var canvas = Realize(link);

        canvas.RaiseMouseUp(100, 15); // right of the 28px text extent
        canvas.RaiseMouseUp(10, 2);   // above it

        Assert.That(clicks, Is.Zero);
    }

    [Test]
    public void Space_raises_LinkClicked_when_focused()
    {
        var link = new LinkLabel { Text = "Link", Bounds = new(0, 0, 120, 30) };
        var clicks = 0;
        link.LinkClicked += (_, _) => ++clicks;
        var canvas = Realize(link);

        Assert.That(canvas.Focusable, Is.True, "link labels take keyboard focus");

        canvas.RaiseKeyDown(Keys.Space);

        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void Visited_shifts_the_painted_color_away_from_accent()
    {
        var link = new LinkLabel { Text = "Link", Bounds = new(0, 0, 120, 30) };
        var canvas = Realize(link);

        Assert.That(canvas.RaisePaint().Operations.Exists(o => o.StartsWith($"text \"Link\" {_Accent}")), Is.True);

        link.Visited = true;

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Link"), Is.True, "still paints the text");
            Assert.That(g.Operations.Exists(o => o.StartsWith($"text \"Link\" {_Accent}")), Is.False, "no longer plain accent");
        });
    }

    [Test]
    public void Hover_over_text_shifts_the_color_and_leave_restores_it()
    {
        var link = new LinkLabel { Text = "Link", Bounds = new(0, 0, 120, 30) };
        var canvas = Realize(link);

        canvas.RaiseMouseMove(10, 15);
        Assert.That(canvas.RaisePaint().Operations.Exists(o => o.StartsWith($"text \"Link\" {_Accent}")), Is.False, "hover shifts color");

        canvas.RaiseMouseLeave();
        Assert.That(canvas.RaisePaint().Operations.Exists(o => o.StartsWith($"text \"Link\" {_Accent}")), Is.True, "leave restores accent");
    }
}
