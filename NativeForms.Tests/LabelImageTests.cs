using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Label.Image"/> and <see cref="Label.ImageAlign"/> are buffered until realization and
/// then forwarded to the peer as one <c>SetImage</c> pair — the (platform-limited) native rendering
/// is each backend's business.
/// </summary>
[TestFixture]
internal sealed class LabelImageTests
{
    private static HeadlessLabelPeer Realize(Label label)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(label);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessLabelPeer>().Single();
    }

    [Test]
    public void Image_is_flushed_to_the_peer_on_realization()
    {
        var image = new HeadlessImage(16, 16);
        var label = new Label { Image = image };

        var peer = Realize(label);

        Assert.Multiple(() =>
        {
            Assert.That(peer.Image, Is.SameAs(image));
            Assert.That(peer.ImageAlign, Is.EqualTo(ContentAlignment.MiddleCenter), "WinForms default");
        });
    }

    [Test]
    public void Image_set_after_realization_is_forwarded()
    {
        var label = new Label();
        var peer = Realize(label);

        var image = new HeadlessImage(16, 16);
        label.Image = image;

        Assert.That(peer.Image, Is.SameAs(image));
    }

    [Test]
    public void ImageAlign_change_is_forwarded()
    {
        var label = new Label { Image = new HeadlessImage(16, 16) };
        var peer = Realize(label);

        label.ImageAlign = ContentAlignment.TopRight;

        Assert.That(peer.ImageAlign, Is.EqualTo(ContentAlignment.TopRight));
    }

    [Test]
    public void Clearing_the_image_reaches_the_peer()
    {
        var label = new Label { Image = new HeadlessImage(16, 16) };
        var peer = Realize(label);

        label.Image = null;

        Assert.That(peer.Image, Is.Null);
    }
}
