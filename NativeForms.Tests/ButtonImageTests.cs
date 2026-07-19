using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Button.Image"/> (with <see cref="Button.ImageAlign"/> and
/// <see cref="Button.TextImageRelation"/>) is buffered until realization and then forwarded to the
/// peer as one <c>SetImage</c> triple — the native mapping itself is each backend's business.
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

    [Test]
    public void Image_is_flushed_to_the_peer_on_realization()
    {
        var image = new HeadlessImage(16, 16);
        var button = new Button { Text = "Go", Image = image };

        var peer = Realize(button);

        Assert.Multiple(() =>
        {
            Assert.That(peer.Image, Is.SameAs(image));
            Assert.That(peer.ImageAlign, Is.EqualTo(ContentAlignment.MiddleCenter), "WinForms default");
            Assert.That(peer.ImageRelation, Is.EqualTo(TextImageRelation.ImageBeforeText));
        });
    }

    [Test]
    public void Image_set_after_realization_is_forwarded()
    {
        var button = new Button { Text = "Go" };
        var peer = Realize(button);

        var image = new HeadlessImage(16, 16);
        button.Image = image;

        Assert.That(peer.Image, Is.SameAs(image));
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
}
