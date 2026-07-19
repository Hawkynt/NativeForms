using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class RichTextBoxTests
{
    private static HeadlessBackend Realize(RichTextBox box)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        return backend;
    }

    private static HeadlessRichTextBoxPeer PeerOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessRichTextBoxPeer>().Single();

    [Test]
    public void Defaults_are_multiline_zoom_one_and_url_detection_on()
    {
        var box = new RichTextBox();

        Assert.Multiple(() =>
        {
            Assert.That(box.Multiline, Is.True);
            Assert.That(box.ZoomFactor, Is.EqualTo(1f));
            Assert.That(box.DetectUrls, Is.True);
        });
    }

    [Test]
    public void Realization_creates_a_rich_peer_and_flushes_the_textbox_surface()
    {
        var box = new RichTextBox { Text = "hello", ReadOnly = true };
        var backend = Realize(box);

        var peer = PeerOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(peer.Text, Is.EqualTo("hello"));
            Assert.That(peer.Multiline, Is.True);
            Assert.That(peer.ReadOnly, Is.True);
            Assert.That(peer.DetectUrls, Is.True);
        });
    }

    [Test]
    public void Selection_style_setters_format_the_current_selection()
    {
        var box = new RichTextBox { Text = "hello world" };
        var backend = Realize(box);
        box.SelectionStart = 6;
        box.SelectionLength = 5;

        box.SelectionBold = true;
        box.SelectionItalic = true;
        box.SelectionUnderline = true;
        box.SelectionStrikeout = true;

        var calls = PeerOf(backend).RichCalls;
        Assert.Multiple(() =>
        {
            Assert.That(calls, Does.Contain("style=Bold,True@6,5"));
            Assert.That(calls, Does.Contain("style=Italic,True@6,5"));
            Assert.That(calls, Does.Contain("style=Underline,True@6,5"));
            Assert.That(calls, Does.Contain("style=Strikeout,True@6,5"));
            Assert.That(box.SelectionBold, Is.True);
        });
    }

    [Test]
    public void Selection_color_size_alignment_and_bullet_write_through()
    {
        var box = new RichTextBox { Text = "hello" };
        var backend = Realize(box);
        box.SelectionStart = 0;
        box.SelectionLength = 5;

        box.SelectionColor = Color.FromArgb(255, 32, 64, 128);
        box.SelectionFontSize = 14f;
        box.SelectionAlignment = ContentAlignment.TopCenter;
        box.SelectionBullet = true;
        box.SelectionBullet = false;

        var calls = PeerOf(backend).RichCalls;
        Assert.Multiple(() =>
        {
            Assert.That(calls, Does.Contain("color=#FF204080@0,5"));
            Assert.That(calls, Does.Contain("fontSize=14@0,5"));
            Assert.That(calls, Does.Contain("alignment=TopCenter@0,5"));
            Assert.That(calls, Does.Contain("bullet=True@0,5"));
            Assert.That(calls, Does.Contain("bullet=False@0,5"));
            Assert.That(box.SelectionBullet, Is.False);
        });
    }

    [Test]
    public void Zoom_set_after_realization_forwards_to_the_peer()
    {
        var box = new RichTextBox();
        var backend = Realize(box);

        box.ZoomFactor = 1.5f;

        Assert.That(PeerOf(backend).Zoom, Is.EqualTo(1.5f));
    }

    [Test]
    public void Zoom_set_before_realization_is_flushed()
    {
        var box = new RichTextBox { ZoomFactor = 2f };
        var backend = Realize(box);

        Assert.That(PeerOf(backend).Zoom, Is.EqualTo(2f));
    }

    [Test]
    public void DetectUrls_off_forwards_to_the_peer()
    {
        var box = new RichTextBox();
        var backend = Realize(box);

        box.DetectUrls = false;

        Assert.That(PeerOf(backend).DetectUrls, Is.False);
    }

    [Test]
    public void Peer_link_notification_raises_LinkClicked_with_the_url()
    {
        var box = new RichTextBox();
        var backend = Realize(box);
        string? clicked = null;
        box.LinkClicked += (_, e) => clicked = e.LinkText;

        PeerOf(backend).FireLinkClicked("https://example.test/");

        Assert.That(clicked, Is.EqualTo("https://example.test/"));
    }

    [Test]
    public void Rtf_set_on_the_realized_control_updates_the_text()
    {
        var box = new RichTextBox();
        var backend = Realize(box);

        box.Rtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil;}}\pard\ql {\b bold} and plain}";

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("bold and plain"));
            Assert.That(PeerOf(backend).Text, Is.EqualTo("bold and plain"));
        });
    }

    [Test]
    public void Rtf_roundtrips_formatting_through_the_peer()
    {
        var box = new RichTextBox();
        var backend = Realize(box);

        box.Rtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil;}}{\colortbl ;\red255\green0\blue0;}\pard\qc {\b\cf1\fs28 hot}}";
        var roundtripped = Hawkynt.NativeForms.Text.RtfSerializer.Parse(box.Rtf);

        var paragraph = roundtripped.Paragraphs.Single();
        var run = paragraph.Runs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(paragraph.Alignment, Is.EqualTo(ContentAlignment.TopCenter));
            Assert.That(run.Text, Is.EqualTo("hot"));
            Assert.That(run.Style, Is.EqualTo(FontStyle.Bold));
            Assert.That(run.Color.ToArgb(), Is.EqualTo(Color.FromArgb(255, 0, 0).ToArgb()));
            Assert.That(run.FontSize, Is.EqualTo(14f));
        });
    }

    [Test]
    public void Rtf_before_realization_sets_the_plain_text_and_is_flushed_to_the_peer()
    {
        var box = new RichTextBox { Rtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil;}}\pard\ql {\i lean} in}" };

        Assert.That(box.Text, Is.EqualTo("lean in"));

        var backend = Realize(box);

        Assert.That(PeerOf(backend).Text, Is.EqualTo("lean in"));
    }

    [Test]
    public void Rtf_get_before_realization_serializes_the_plain_text()
    {
        var box = new RichTextBox { Text = "plain" };

        var document = Hawkynt.NativeForms.Text.RtfSerializer.Parse(box.Rtf);

        Assert.That(document.ToPlainText(), Is.EqualTo("plain"));
    }

    [Test]
    public void Setting_Text_discards_previously_buffered_Rtf()
    {
        var box = new RichTextBox { Rtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil;}}\pard\ql {\b rich}}" };

        box.Text = "plain wins";
        var backend = Realize(box);

        Assert.That(PeerOf(backend).Text, Is.EqualTo("plain wins"));
    }
}
