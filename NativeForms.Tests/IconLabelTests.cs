using System.Drawing;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// An <see cref="IconLabel"/> must render image <em>and</em> text together — the pairing the native
/// <see cref="Label"/> widget cannot do — laid out through the shared content geometry, honouring
/// the ambient font and fore colour, and sizing itself to both parts under
/// <see cref="IconLabel.AutoSize"/>.
/// </summary>
[TestFixture]
internal sealed class IconLabelTests
{
    /// <summary>Realizes a label on a fresh form and returns its canvas.</summary>
    private static HeadlessCanvasPeer Realize(IconLabel label, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 400, 200) };
        form.Controls.Add(label);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    [Test]
    public void Draws_both_the_image_and_the_text()
    {
        // The whole point of the control: the native Label drops one of the two, this keeps both.
        var label = new IconLabel { Text = "Documents", Image = new HeadlessImage(16, 16), Bounds = new(0, 0, 200, 24) };
        var canvas = Realize(label, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("image 16x16")), Is.True, "the icon");
            Assert.That(g.DrewText("Documents"), Is.True, "the caption");
        });
    }

    [Test]
    public void ImageBeforeText_puts_the_icon_left_of_the_caption()
    {
        var label = new IconLabel
        {
            Text = "abc",
            Image = new HeadlessImage(16, 16),
            Bounds = new(0, 0, 200, 24),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var canvas = Realize(label, out _);

        var g = canvas.RaisePaint();

        // 16 px icon + 4 px gap, so the text starts at x=20 within the client rectangle.
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("image 16x16 @0,4,16,16"));
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"abc\"") && o.EndsWith("@20,4")), Is.True);
        });
    }

    [Test]
    public void TextBeforeImage_swaps_the_two_parts()
    {
        var label = new IconLabel
        {
            Text = "abc",
            Image = new HeadlessImage(16, 16),
            Bounds = new(0, 0, 200, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.TextBeforeImage,
        };
        var canvas = Realize(label, out _);

        var g = canvas.RaisePaint();

        // "abc" measures 21 px in the deterministic test metric, so the icon follows at 21+4=25.
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"abc\"") && o.EndsWith("@0,4")), Is.True);
            Assert.That(g.Operations, Does.Contain("image 16x16 @25,4,16,16"));
        });
    }

    [Test]
    public void ImageAboveText_stacks_the_two_parts()
    {
        var label = new IconLabel
        {
            Text = "abc",
            Image = new HeadlessImage(16, 16),
            Bounds = new(0, 0, 200, 60),
            TextAlign = ContentAlignment.TopLeft,
            TextImageRelation = TextImageRelation.ImageAboveText,
        };
        var canvas = Realize(label, out _);

        var g = canvas.RaisePaint();

        // 16 px icon, 4 px gap, then the 16 px text line beneath it.
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("image 16x16 @") && o.Contains(",0,16,16")), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"abc\"") && o.EndsWith(",20")), Is.True);
        });
    }

    [Test]
    public void Text_only_still_renders()
    {
        var label = new IconLabel { Text = "no icon", Bounds = new(0, 0, 200, 24) };
        var canvas = Realize(label, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("no icon"), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("image ")), Is.False);
        });
    }

    [Test]
    public void Image_only_anchors_by_ImageAlign()
    {
        var label = new IconLabel
        {
            Image = new HeadlessImage(16, 16),
            Bounds = new(0, 0, 100, 40),
            ImageAlign = ContentAlignment.MiddleCenter,
        };
        var canvas = Realize(label, out _);

        var g = canvas.RaisePaint();

        Assert.That(g.Operations, Does.Contain("image 16x16 @42,12,16,16"), "centred in a 100×40 client");
    }

    [Test]
    public void AutoSize_fits_the_image_the_gap_and_the_text()
    {
        var label = new IconLabel { Text = "abcd", Image = new HeadlessImage(16, 16), AutoSize = true };
        Realize(label, out _);

        // 16 px icon + 4 px gap + 4 chars × 7 px = 48 wide; the taller of 16 and 16 = 16 high.
        Assert.That(label.Size, Is.EqualTo(new Size(48, 16)));
    }

    [Test]
    public void AutoSize_stacks_the_heights_for_a_vertical_relation()
    {
        var label = new IconLabel
        {
            Text = "abcd",
            Image = new HeadlessImage(16, 16),
            AutoSize = true,
            TextImageRelation = TextImageRelation.ImageAboveText,
        };
        Realize(label, out _);

        // max(16, 28) wide; 16 px icon + 4 px gap + 16 px line = 36 high.
        Assert.That(label.Size, Is.EqualTo(new Size(28, 36)));
    }

    [Test]
    public void AutoSize_follows_a_later_text_change()
    {
        var label = new IconLabel { Text = "ab", Image = new HeadlessImage(16, 16), AutoSize = true };
        Realize(label, out _);
        var before = label.Size;

        label.Text = "abcdefgh";

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(new Size(34, 16)));
            Assert.That(label.Size, Is.EqualTo(new Size(76, 16)));
        });
    }

    [Test]
    public void Adopts_the_ambient_font_and_fore_colour()
    {
        var font = new Font("Courier New", 11f);
        var label = new IconLabel
        {
            Text = "styled",
            Image = new HeadlessImage(16, 16),
            Bounds = new(0, 0, 200, 24),
            Font = font,
            ForeColor = Color.FromArgb(0xFF, 0x20, 0x40, 0x60),
        };
        var canvas = Realize(label, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewTextWithFont("styled", font), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"styled\" #FF204060")), Is.True);
        });
    }

    [Test]
    public void A_disabled_label_greys_its_caption()
    {
        var label = new IconLabel { Text = "off", Bounds = new(0, 0, 200, 24), Enabled = false };
        var canvas = Realize(label, out _);

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("text \"off\" #FF9A9A9A")), Is.True);
    }

    [Test]
    public void Right_to_left_mirrors_which_side_the_icon_leads_on()
    {
        var label = new IconLabel
        {
            Text = "abc",
            Image = new HeadlessImage(16, 16),
            Bounds = new(0, 0, 200, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            RightToLeft = RightToLeft.Yes,
        };
        var canvas = Realize(label, out _);

        var g = canvas.RaisePaint();

        // Mirrored: the block anchors right and the icon trails the caption.
        Assert.That(g.Operations, Does.Contain("image 16x16 @184,4,16,16"));
    }

    [Test]
    public void Takes_no_focus()
    {
        var label = new IconLabel { Text = "static", Bounds = new(0, 0, 200, 24) };
        var canvas = Realize(label, out _);

        Assert.That(canvas.Focusable, Is.False);
    }

    [Test]
    public void Painting_stays_inside_the_client_rectangle()
    {
        var label = new IconLabel
        {
            Text = "a caption far too long for the box it was given",
            Image = new HeadlessImage(16, 16),
            Bounds = new(0, 0, 80, 24),
        };
        var canvas = Realize(label, out _);

        Assert.That(canvas.RaisePaint().OutOfBoundsOperations, Is.Empty);
    }
}
