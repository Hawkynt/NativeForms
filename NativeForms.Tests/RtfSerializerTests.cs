using System.Drawing;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Text;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class RtfSerializerTests
{
    [Test]
    public void Roundtrip_preserves_styles_color_size_alignment_and_bullets()
    {
        var document = new RichDocument();
        var heading = new RichParagraph { Alignment = ContentAlignment.TopCenter };
        heading.Runs.Add(new("Title", FontStyle.Bold | FontStyle.Underline, Color.FromArgb(200, 40, 40), 18f));
        document.Paragraphs.Add(heading);

        var bullet = new RichParagraph { Bullet = true };
        bullet.Runs.Add(new("first "));
        bullet.Runs.Add(new("point", FontStyle.Italic | FontStyle.Strikeout));
        document.Paragraphs.Add(bullet);

        var right = new RichParagraph { Alignment = ContentAlignment.BottomRight };
        right.Runs.Add(new("signed", FontStyle.Regular, Color.Empty, 9.5f));
        document.Paragraphs.Add(right);

        var parsed = RtfSerializer.Parse(RtfSerializer.Write(document));

        Assert.That(parsed.Paragraphs, Has.Count.EqualTo(3));
        var p0 = parsed.Paragraphs[0];
        var p1 = parsed.Paragraphs[1];
        var p2 = parsed.Paragraphs[2];
        Assert.Multiple(() =>
        {
            Assert.That(p0.Alignment, Is.EqualTo(ContentAlignment.TopCenter));
            Assert.That(p0.Runs.Single().Text, Is.EqualTo("Title"));
            Assert.That(p0.Runs.Single().Style, Is.EqualTo(FontStyle.Bold | FontStyle.Underline));
            Assert.That(p0.Runs.Single().Color.ToArgb(), Is.EqualTo(Color.FromArgb(200, 40, 40).ToArgb()));
            Assert.That(p0.Runs.Single().FontSize, Is.EqualTo(18f));

            Assert.That(p1.Bullet, Is.True);
            Assert.That(p1.ToPlainText(), Is.EqualTo("first point"));
            Assert.That(p1.Runs[1].Style, Is.EqualTo(FontStyle.Italic | FontStyle.Strikeout));

            // The vertical component is not part of the subset — the horizontal one survives.
            Assert.That(p2.Alignment, Is.EqualTo(ContentAlignment.TopRight));
            Assert.That(p2.Runs.Single().FontSize, Is.EqualTo(9.5f));
        });
    }

    [Test]
    public void Roundtrip_escapes_braces_backslashes_and_non_ascii()
    {
        var document = RichDocument.FromPlainText(@"a{b}c\d — ümlaut");

        var parsed = RtfSerializer.Parse(RtfSerializer.Write(document));

        Assert.That(parsed.ToPlainText(), Is.EqualTo(@"a{b}c\d — ümlaut"));
    }

    [Test]
    public void FromPlainText_splits_paragraphs_on_line_breaks()
    {
        var document = RichDocument.FromPlainText("one\r\ntwo\nthree");

        Assert.Multiple(() =>
        {
            Assert.That(document.Paragraphs, Has.Count.EqualTo(3));
            Assert.That(document.ToPlainText(), Is.EqualTo("one\ntwo\nthree"));
        });
    }

    [Test]
    public void Unknown_control_words_and_groups_are_ignored()
    {
        var parsed = RtfSerializer.Parse(
            @"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fswiss Arial;}}{\*\generator NativeForms;}\viewkind4\uc1\pard\f0\fs17 kept}");

        Assert.That(parsed.ToPlainText(), Is.EqualTo("kept"));
    }

    [Test]
    public void Tabs_and_unicode_escapes_survive()
    {
        var document = RichDocument.FromPlainText("a\tb☃c");

        var parsed = RtfSerializer.Parse(RtfSerializer.Write(document));

        Assert.That(parsed.ToPlainText(), Is.EqualTo("a\tb☃c"));
    }

    [Test]
    public void Adjacent_equally_formatted_text_merges_into_one_run()
    {
        var parsed = RtfSerializer.Parse(@"{\rtf1\ansi\deff0\pard\ql {\b bo}{\b ld}}");

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Paragraphs.Single().Runs, Has.Count.EqualTo(1));
            Assert.That(parsed.Paragraphs.Single().Runs[0].Text, Is.EqualTo("bold"));
            Assert.That(parsed.Paragraphs.Single().Runs[0].Style, Is.EqualTo(FontStyle.Bold));
        });
    }

    [Test]
    public void An_empty_document_parses_to_a_single_empty_paragraph()
    {
        var parsed = RtfSerializer.Parse(RtfSerializer.Write(new RichDocument()));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Paragraphs, Has.Count.EqualTo(1));
            Assert.That(parsed.ToPlainText(), Is.Empty);
        });
    }
}
