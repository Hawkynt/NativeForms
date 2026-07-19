using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class LabelTests
{
    private static (HeadlessBackend Backend, HeadlessLabelPeer Peer) Realize(Label label)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(label);
        Application.Run(form, backend);
        return (backend, backend.Created.OfType<HeadlessLabelPeer>().Single());
    }

    [Test]
    public void Backend_text_measurement_is_deterministic()
    {
        var backend = new HeadlessBackend();
        var font = backend.Theme.DefaultFont;

        Assert.Multiple(() =>
        {
            Assert.That(backend.MeasureText("Hello", font), Is.EqualTo(new Size(35, 16)));
            Assert.That(backend.MeasureText(string.Empty, font), Is.EqualTo(new Size(0, 16)));
        });
    }

    [Test]
    public void AutoSize_resizes_on_realization()
    {
        var label = new Label { Text = "Hello", AutoSize = true, Bounds = new(10, 20, 1, 1) };

        Realize(label);

        Assert.That(label.Bounds, Is.EqualTo(new Rectangle(10, 20, 35, 16)));
    }

    [Test]
    public void AutoSize_resizes_on_text_change_while_realized()
    {
        var label = new Label { Text = "Hi", AutoSize = true };
        Realize(label);

        label.Text = "A longer caption";

        Assert.That(label.Size, Is.EqualTo(new Size(16 * 7, 16)));
    }

    [Test]
    public void AutoSize_enabled_after_realization_resizes_immediately()
    {
        var label = new Label { Text = "Hello", Bounds = new(0, 0, 200, 50) };
        Realize(label);

        label.AutoSize = true;

        Assert.That(label.Size, Is.EqualTo(new Size(35, 16)));
    }

    [Test]
    public void AutoSize_off_leaves_bounds_alone()
    {
        var label = new Label { Text = "Hello", Bounds = new(0, 0, 200, 50) };
        Realize(label);

        label.Text = "Something entirely different";

        Assert.That(label.Size, Is.EqualTo(new Size(200, 50)));
    }

    [Test]
    public void TextAlign_is_forwarded_to_peer()
    {
        var label = new Label { Text = "x", TextAlign = ContentAlignment.MiddleCenter };

        var (_, peer) = Realize(label);
        Assert.That(peer.TextAlign, Is.EqualTo(ContentAlignment.MiddleCenter));

        label.TextAlign = ContentAlignment.TopRight;
        Assert.That(peer.TextAlign, Is.EqualTo(ContentAlignment.TopRight));
    }

    [Test]
    public void BorderStyle_is_forwarded_to_peer()
    {
        var label = new Label { BorderStyle = BorderStyle.FixedSingle };

        var (_, peer) = Realize(label);
        Assert.That(peer.BorderStyle, Is.EqualTo(BorderStyle.FixedSingle));

        label.BorderStyle = BorderStyle.None;
        Assert.That(peer.BorderStyle, Is.EqualTo(BorderStyle.None));
    }

    [Test]
    public void UseMnemonic_is_forwarded_to_peer()
    {
        var label = new Label { UseMnemonic = false };

        var (_, peer) = Realize(label);
        Assert.That(peer.UseMnemonic, Is.False);

        label.UseMnemonic = true;
        Assert.That(peer.UseMnemonic, Is.True);
    }
}
