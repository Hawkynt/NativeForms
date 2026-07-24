using System.Linq;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The owner-drawn message box the framework falls back to for a custom icon (still or animated) or
/// arbitrary button labels — the cases the native dialog cannot express.
/// </summary>
[TestFixture]
internal sealed class MessageBoxCustomTests
{
    private static AnimatedImage TwoFrames()
        => new(new DecodedImage(2, 2, [new ImageFrame(new int[4], 100), new ImageFrame(new int[4], 100)], 0));

    [Test]
    public void Custom_buttons_return_the_index_of_the_one_clicked()
    {
        var backend = new HeadlessBackend();
        try
        {
            MessageBox.ConfiguredForTest = form => backend.ModalAction = _ => form.Buttons[2].PerformClick();

            var index = MessageBox.Show(backend, "Pick one", "Custom", ["Alpha", "Beta", "Gamma"], null, MessageBoxIcon.None);

            Assert.That(index, Is.EqualTo(2));
        }
        finally
        {
            MessageBox.ConfiguredForTest = null;
        }
    }

    [Test]
    public void The_dialog_builds_one_button_per_label()
    {
        var backend = new HeadlessBackend();
        var form = new MessageBoxForm(backend, "Body", "Title", null, MessageBoxIcon.None, ["Yes", "No", "Maybe"], 0, -1);

        Assert.Multiple(() =>
        {
            Assert.That(form.Buttons.Count, Is.EqualTo(3));
            Assert.That(form.Buttons.Select(b => b.Text), Is.EqualTo(new[] { "Yes", "No", "Maybe" }));
        });
    }

    [Test]
    public void A_custom_icon_may_be_an_animated_image_and_is_hosted_in_a_picture_box()
    {
        var backend = new HeadlessBackend();
        var icon = TwoFrames();

        var form = new MessageBoxForm(backend, "Working…", "Busy", icon, MessageBoxIcon.None, ["Cancel"], 0, 0);

        var picture = FindPictureBox(form);
        Assert.Multiple(() =>
        {
            Assert.That(picture, Is.Not.Null, "a custom icon is shown in a PictureBox");
            Assert.That(picture!.Image, Is.SameAs(icon), "the animated image flows through the box's plain Image property");
        });
    }

    [Test]
    public void A_standard_button_set_with_a_custom_icon_maps_the_click_to_a_dialog_result()
    {
        var backend = new HeadlessBackend();
        var icon = TwoFrames();
        try
        {
            // YesNo → click button 1 ("No").
            MessageBox.ConfiguredForTest = form => backend.ModalAction = _ => form.Buttons[1].PerformClick();

            var result = MessageBox.Show(backend, "Overwrite?", "Confirm", MessageBoxButtons.YesNo, icon);

            Assert.That(result, Is.EqualTo(DialogResult.No));
        }
        finally
        {
            MessageBox.ConfiguredForTest = null;
        }
    }

    private static PictureBox? FindPictureBox(Control root)
    {
        for (var i = 0; i < root.Controls.Count; ++i)
        {
            if (root.Controls[i] is PictureBox box)
                return box;

            if (FindPictureBox(root.Controls[i]) is { } nested)
                return nested;
        }

        return null;
    }
}
