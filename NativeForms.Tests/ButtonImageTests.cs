using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Button.Image"/> (with <see cref="Button.ImageAlign"/> and
/// <see cref="Button.TextImageRelation"/>) on both halves of the button: buffered and forwarded to the
/// peer as one <c>SetImage</c> triple while the widget can express the face, and painted through the
/// shared <see cref="ContentLayout"/> once it cannot — an image with a caption beside it, which is the
/// promotion gate (PRD §12).
/// </summary>
[TestFixture]
internal sealed class ButtonImageTests
{
    private static HeadlessButtonPeer Realize(Button button)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(button);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessButtonPeer>().Single();
    }

    private static HeadlessCanvasPeer RealizeDrawn(Button button, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(button);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    // --- The widget half ---------------------------------------------------------------------------

    [Test]
    public void An_image_without_a_caption_is_flushed_to_the_peer_on_realization()
    {
        var image = new HeadlessImage(16, 16);
        var button = new Button { Image = image };

        var peer = Realize(button);

        Assert.Multiple(() =>
        {
            Assert.That(button.IsNativeWidget, Is.True, "every platform centres a bare image on a button, so the widget can say it");
            Assert.That(peer.Image, Is.SameAs(image));
            Assert.That(peer.ImageAlign, Is.EqualTo(ContentAlignment.MiddleCenter), "WinForms default");
            Assert.That(peer.ImageRelation, Is.EqualTo(TextImageRelation.ImageBeforeText));
        });
    }

    [Test]
    public void Alignment_and_relation_changes_are_forwarded()
    {
        var button = new Button { Image = new HeadlessImage(16, 16) };
        var peer = Realize(button);

        button.ImageAlign = ContentAlignment.TopLeft;
        button.TextImageRelation = TextImageRelation.ImageAboveText;

        Assert.Multiple(() =>
        {
            Assert.That(peer.ImageAlign, Is.EqualTo(ContentAlignment.TopLeft));
            Assert.That(peer.ImageRelation, Is.EqualTo(TextImageRelation.ImageAboveText));
        });
    }

    [Test]
    public void Clearing_the_image_reaches_the_peer()
    {
        var button = new Button { Image = new HeadlessImage(16, 16) };
        var peer = Realize(button);

        button.Image = null;

        Assert.That(peer.Image, Is.Null);
    }

    [Test]
    public void A_caption_alone_keeps_the_widget()
    {
        var button = new Button { Text = "Go" };

        Realize(button);

        Assert.That(button.IsNativeWidget, Is.True);
    }

    // --- The gate ----------------------------------------------------------------------------------

    [Test]
    public void An_image_beside_a_caption_gives_up_the_widget()
    {
        var button = new Button { Text = "Go", Image = new HeadlessImage(16, 16) };

        RealizeDrawn(button, out var backend);

        Assert.Multiple(() =>
        {
            Assert.That(button.IsNativeWidget, Is.False, "no platform button draws both the same way");
            Assert.That(backend.Created.OfType<HeadlessButtonPeer>(), Is.Empty, "so no widget was asked for");
        });
    }

    [Test]
    public void Adding_a_caption_to_an_image_button_moves_it_onto_the_canvas()
    {
        var button = new Button { Image = new HeadlessImage(16, 16) };
        Realize(button);
        Assume.That(button.IsNativeWidget, Is.True);

        button.Text = "Go";

        Assert.That(button.IsNativeWidget, Is.False, "the swap runs on a live control, not only at realization");
    }

    [Test]
    public void Taking_the_image_away_again_moves_it_back_onto_the_widget()
    {
        var button = new Button { Text = "Go", Image = new HeadlessImage(16, 16) };
        RealizeDrawn(button, out _);
        Assume.That(button.IsNativeWidget, Is.False);

        button.Image = null;

        Assert.That(button.IsNativeWidget, Is.True, "the gate swings both ways and the application never sees it");
    }

    [Test]
    public void A_button_kept_off_the_widget_path_paints_even_without_an_image()
    {
        var button = new Button { Text = "Go", UseNativeWidget = false };

        var canvas = RealizeDrawn(button, out _);
        var painted = canvas.RaisePaint();

        Assert.That(painted.TextDraws.Select(static draw => draw.Text), Does.Contain("Go"));
    }

    // --- The painted half --------------------------------------------------------------------------

    [Test]
    public void The_painted_face_draws_the_image_and_the_caption()
    {
        var button = new Button { Bounds = new(0, 0, 120, 30), Text = "Go", Image = new HeadlessImage(16, 16) };

        var canvas = RealizeDrawn(button, out _);
        var painted = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(painted.Operations.Count(static op => op.StartsWith("image ")), Is.EqualTo(1));
            Assert.That(painted.TextDraws.Select(static draw => draw.Text), Does.Contain("Go"));
        });
    }

    [Test]
    public void The_relation_decides_which_side_the_image_leads_on()
    {
        Assert.That(ImageLeft(TextImageRelation.ImageBeforeText), Is.LessThan(ImageLeft(TextImageRelation.TextBeforeImage)));

        static int ImageLeft(TextImageRelation relation)
        {
            var button = new Button
            {
                Bounds = new(0, 0, 160, 30),
                Text = "Go",
                Image = new HeadlessImage(16, 16),
                TextImageRelation = relation,
            };

            var canvas = RealizeDrawn(button, out _);
            var op = canvas.RaisePaint().Operations.First(static op => op.StartsWith("image "));
            return int.Parse(op.Split('@')[^1].Split(',')[0]);
        }
    }

    [Test]
    public void The_mnemonic_is_stripped_from_the_painted_caption_and_underlined()
    {
        var plain = new Button { Bounds = new(0, 0, 120, 30), Text = "Go", Image = new HeadlessImage(16, 16) };
        var marked = new Button { Bounds = new(0, 0, 120, 30), Text = "&Go", Image = new HeadlessImage(16, 16) };

        var plainPaint = RealizeDrawn(plain, out _).RaisePaint();
        var markedPaint = RealizeDrawn(marked, out _).RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(markedPaint.TextDraws.Select(static draw => draw.Text), Does.Contain("Go"), "the ampersand is mark-up, not a glyph");
            Assert.That(
                markedPaint.Operations.Count(static op => op.StartsWith("line ")),
                Is.EqualTo(plainPaint.Operations.Count(static op => op.StartsWith("line ")) + 1),
                "and the marked character gains an underline");
        });
    }

    [Test]
    public void The_painted_face_clicks_like_the_widget_does()
    {
        var button = new Button { Bounds = new(0, 0, 120, 30), Text = "Go", Image = new HeadlessImage(16, 16) };
        var clicks = 0;
        button.Click += (_, _) => ++clicks;
        var canvas = RealizeDrawn(button, out _);

        canvas.RaiseMouseDown(20, 15);
        canvas.RaiseMouseUp(20, 15);

        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void A_press_released_off_the_face_is_a_cancelled_click()
    {
        var button = new Button { Bounds = new(0, 0, 120, 30), Text = "Go", Image = new HeadlessImage(16, 16) };
        var clicks = 0;
        button.Click += (_, _) => ++clicks;
        var canvas = RealizeDrawn(button, out _);

        canvas.RaiseMouseDown(20, 15);
        canvas.RaiseMouseUp(400, 15);

        Assert.That(clicks, Is.Zero, "every platform button lets a press be taken back by sliding off it");
    }

    [Test]
    public void Space_and_Enter_work_the_painted_face_on_the_key_release()
    {
        var button = new Button { Bounds = new(0, 0, 120, 30), Text = "Go", Image = new HeadlessImage(16, 16) };
        var clicks = 0;
        button.Click += (_, _) => ++clicks;
        var canvas = RealizeDrawn(button, out _);

        canvas.RaiseKeyDown(Keys.Space);
        var afterDown = clicks;
        canvas.RaiseKeyUp(Keys.Space);
        canvas.RaiseKeyUp(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(afterDown, Is.Zero, "a held key must not auto-repeat the click");
            Assert.That(clicks, Is.EqualTo(2));
        });
    }

    [Test]
    public void A_painted_button_still_reports_its_DialogResult_to_the_form()
    {
        var button = new Button
        {
            Bounds = new(0, 0, 120, 30),
            Text = "OK",
            Image = new HeadlessImage(16, 16),
            DialogResult = DialogResult.OK,
        };

        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(button);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        canvas.RaiseMouseDown(20, 15);
        canvas.RaiseMouseUp(20, 15);

        Assert.That(form.DialogResult, Is.EqualTo(DialogResult.OK), "the dialog contract belongs to the control, not to the peer");
    }
}
