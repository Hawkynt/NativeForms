using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Expander"/> must fold to its header row — remembering the expanded height and hiding
/// the child peers without clobbering their logical <see cref="Control.Visible"/> — and unfold back,
/// toggled by a header click or the Space key.
/// </summary>
[TestFixture]
internal sealed class ExpanderTests
{
    private const int _HeaderHeight = 22; // DefaultTheme.RowHeight

    private static HeadlessCanvasPeer Realize(Expander expander, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(expander);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    [Test]
    public void Collapsing_shrinks_to_the_header_and_hides_the_child_peers()
    {
        var expander = new Expander { Text = "Details", Bounds = new(0, 0, 200, 150) };
        var button = new Button { Bounds = new(10, 40, 80, 24) };
        expander.Controls.Add(button);
        var changes = 0;
        expander.ExpandedChanged += (_, _) => ++changes;
        var canvas = Realize(expander, out var backend);
        var buttonPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();

        expander.Expanded = false;

        Assert.Multiple(() =>
        {
            Assert.That(expander.Height, Is.EqualTo(_HeaderHeight));
            Assert.That(canvas.Bounds, Is.EqualTo(new Rectangle(0, 0, 200, _HeaderHeight)));
            Assert.That(buttonPeer.Visible, Is.False);
            Assert.That(
                button.Visible,
                Is.False,
                "Visible is effective: a child the collapsed expander vetoed is not on screen");
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void Expanding_restores_the_height_and_the_child_peers()
    {
        var expander = new Expander { Text = "Details", Bounds = new(0, 0, 200, 150) };
        var button = new Button { Bounds = new(10, 40, 80, 24) };
        expander.Controls.Add(button);
        var canvas = Realize(expander, out var backend);
        var buttonPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();
        expander.Expanded = false;

        expander.Expanded = true;

        Assert.Multiple(() =>
        {
            Assert.That(expander.Height, Is.EqualTo(150), "remembered expanded height");
            Assert.That(canvas.Bounds, Is.EqualTo(new Rectangle(0, 0, 200, 150)));
            Assert.That(buttonPeer.Visible, Is.True);
        });
    }

    [Test]
    public void Clicking_the_header_toggles()
    {
        var expander = new Expander { Text = "Details", Bounds = new(0, 0, 200, 150) };
        var canvas = Realize(expander, out _);

        canvas.RaiseMouseUp(10, _HeaderHeight / 2);
        Assert.That(expander.Expanded, Is.False);

        canvas.RaiseMouseUp(10, _HeaderHeight / 2);
        Assert.That(expander.Expanded, Is.True);
    }

    [Test]
    public void Clicking_the_content_area_does_not_toggle()
    {
        var expander = new Expander { Text = "Details", Bounds = new(0, 0, 200, 150) };
        var canvas = Realize(expander, out _);

        canvas.RaiseMouseUp(10, 100);

        Assert.That(expander.Expanded, Is.True);
    }

    [Test]
    public void Space_toggles()
    {
        var expander = new Expander { Text = "Details", Bounds = new(0, 0, 200, 150) };
        var canvas = Realize(expander, out _);

        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(expander.Expanded, Is.False);

        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(expander.Expanded, Is.True);
    }

    [Test]
    public void Child_added_while_collapsed_realizes_hidden()
    {
        var expander = new Expander { Text = "Details", Bounds = new(0, 0, 200, 150) };
        Realize(expander, out var backend);
        expander.Expanded = false;

        expander.Controls.Add(new Button { Bounds = new(10, 40, 80, 24) });

        var buttonPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();
        Assert.That(buttonPeer.Visible, Is.False);
    }

    [Test]
    public void Header_paints_the_caption_and_a_glyph()
    {
        var expander = new Expander { Text = "Details", Bounds = new(0, 0, 200, 150) };
        var canvas = Realize(expander, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Details"), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("line ")), Is.True, "triangle glyph strokes");
        });
    }
}
