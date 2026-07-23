using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Breadcrumb"/> must lay path segments left to right separated by chevrons, hit-test a
/// click to its segment, trim the path to a clicked segment (the navigate-up gesture) and fold the
/// leading segments behind a "…" chip when they outgrow the width.
/// </summary>
[TestFixture]
internal sealed class BreadcrumbTests
{
    // RecordingGraphics measures 7 px per character; segment = 2*8 padding + text; chevron advance 16.
    private const int _Pad = 8;
    private const int _Char = 7;
    private const int _Chevron = 16;

    private static HeadlessCanvasPeer Realize(Breadcrumb crumb, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(crumb);
        Application.Run(form, backend);
        return (HeadlessCanvasPeer)crumb.Peer!;
    }

    private static Breadcrumb ThreeSegments(int width = 400)
    {
        var crumb = new Breadcrumb { Bounds = new(0, 0, width, 24) };
        crumb.Items.AddRange("Home", "Docs", "Sub");
        return crumb;
    }

    [Test]
    public void Clicking_a_segment_raises_ItemClicked_with_its_index()
    {
        var crumb = ThreeSegments();
        var canvas = Realize(crumb, out _);
        canvas.RaisePaint();
        BreadcrumbItemEventArgs? clicked = null;
        crumb.ItemClicked += (_, e) => clicked = e;

        // "Home" spans x in [0, 2*8+4*7=44); the chevron then "Docs" begins at 44+16=60.
        canvas.RaiseMouseDown(70, 12); // inside "Docs"

        Assert.Multiple(() =>
        {
            Assert.That(clicked, Is.Not.Null);
            Assert.That(clicked!.Index, Is.EqualTo(1));
            Assert.That(clicked.Item.Text, Is.EqualTo("Docs"));
        });
    }

    [Test]
    public void Clicking_trims_the_path_to_the_clicked_segment()
    {
        var crumb = ThreeSegments();
        var canvas = Realize(crumb, out _);
        canvas.RaisePaint();

        canvas.RaiseMouseDown(10, 12); // "Home", the first segment

        Assert.Multiple(() =>
        {
            Assert.That(crumb.Items, Has.Count.EqualTo(1), "the path is trimmed to Home");
            Assert.That(crumb.Items[0].Text, Is.EqualTo("Home"));
        });
    }

    [Test]
    public void TrimOnClick_off_keeps_the_whole_path()
    {
        var crumb = ThreeSegments();
        crumb.TrimOnClick = false;
        var canvas = Realize(crumb, out _);
        canvas.RaisePaint();

        canvas.RaiseMouseDown(10, 12); // "Home"

        Assert.That(crumb.Items, Has.Count.EqualTo(3));
    }

    [Test]
    public void Paints_captions_and_a_chevron_between_segments()
    {
        var crumb = ThreeSegments();
        var canvas = Realize(crumb, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Home"), Is.True);
            Assert.That(g.DrewText("Docs"), Is.True);
            Assert.That(g.DrewText("Sub"), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("line")), Is.True, "chevron separators are painted as glyphs");
        });
    }

    [Test]
    public void Overflowing_segments_fold_behind_an_ellipsis_and_keep_the_last()
    {
        var crumb = new Breadcrumb { Bounds = new(0, 0, 120, 24) };
        crumb.Items.AddRange("Root", "Level1", "Level2", "Level3", "Leaf");
        var canvas = Realize(crumb, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("…"), Is.True, "leading segments fold behind the overflow chip");
            Assert.That(g.DrewText("Leaf"), Is.True, "the last segment always stays visible");
            Assert.That(g.DrewText("Root"), Is.False, "a folded leading segment is not painted");
        });
    }

    [Test]
    public void An_empty_breadcrumb_paints_without_error()
    {
        var crumb = new Breadcrumb { Bounds = new(0, 0, 200, 24) };
        var canvas = Realize(crumb, out _);

        Assert.DoesNotThrow(() => canvas.RaisePaint());
    }

    [Test]
    public void TrimAfter_removes_the_trailing_segments()
    {
        var crumb = ThreeSegments();
        Realize(crumb, out _);

        crumb.Items.TrimAfter(0);

        Assert.That(crumb.Items, Has.Count.EqualTo(1));
    }
}
