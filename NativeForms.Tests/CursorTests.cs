using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Control.Cursor"/> must default to <see cref="Cursors.Arrow"/>, inherit ambiently from
/// the parent chain, flush to the peer on realization and forward live afterwards, and the
/// <see cref="LinkLabel"/> must switch to <see cref="Cursors.Hand"/> while the pointer rests on its
/// link text.
/// </summary>
[TestFixture]
internal sealed class CursorTests
{
    /// <summary>Realizes the given form on a fresh headless backend.</summary>
    private static HeadlessBackend Realize(Form form)
    {
        var backend = new HeadlessBackend();
        Application.Run(form, backend);
        return backend;
    }

    [Test]
    public void Cursors_expose_one_shared_instance_per_stock_shape()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Cursors.Hand, Is.SameAs(Cursors.Hand));
            Assert.That(Cursors.Arrow.Kind, Is.EqualTo(CursorKind.Arrow));
            Assert.That(Cursors.Hand.Kind, Is.EqualTo(CursorKind.Hand));
            Assert.That(Cursors.IBeam.Kind, Is.EqualTo(CursorKind.IBeam));
            Assert.That(Cursors.Wait.Kind, Is.EqualTo(CursorKind.Wait));
            Assert.That(Cursors.Cross.Kind, Is.EqualTo(CursorKind.Cross));
            Assert.That(Cursors.SizeWE.Kind, Is.EqualTo(CursorKind.SizeWE));
            Assert.That(Cursors.SizeNS.Kind, Is.EqualTo(CursorKind.SizeNS));
            Assert.That(Cursors.SizeNWSE.Kind, Is.EqualTo(CursorKind.SizeNWSE));
            Assert.That(Cursors.SizeNESW.Kind, Is.EqualTo(CursorKind.SizeNESW));
            Assert.That(Cursors.No.Kind, Is.EqualTo(CursorKind.No));
        });
    }

    [Test]
    public void Cursor_defaults_to_the_arrow_and_inherits_from_the_parent()
    {
        var form = new Form();
        var label = new Label();
        form.Controls.Add(label);

        Assert.That(label.Cursor, Is.SameAs(Cursors.Arrow));

        form.Cursor = Cursors.Wait;

        Assert.That(label.Cursor, Is.SameAs(Cursors.Wait), "the child inherits the parent's cursor");
    }

    [Test]
    public void ResetCursor_returns_to_the_ambient_value()
    {
        var form = new Form { Cursor = Cursors.Wait };
        var label = new Label { Cursor = Cursors.Cross };
        form.Controls.Add(label);

        label.ResetCursor();

        Assert.That(label.Cursor, Is.SameAs(Cursors.Wait));
    }

    [Test]
    public void Realization_flushes_the_cursor_to_the_peer()
    {
        var label = new Label { Cursor = Cursors.IBeam };
        var plain = new Label();
        var form = new Form();
        form.Controls.Add(label);
        form.Controls.Add(plain);

        var backend = Realize(form);

        var peers = backend.Created.OfType<HeadlessLabelPeer>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(peers[0].Cursor, Is.SameAs(Cursors.IBeam));
            Assert.That(peers[1].Cursor, Is.Null, "no cursor pushed — the native default stays");
        });
    }

    [Test]
    public void Live_cursor_change_forwards_and_cascades_to_inheriting_children()
    {
        var inheriting = new Label();
        var pinned = new Label { Cursor = Cursors.Cross };
        var form = new Form();
        form.Controls.Add(inheriting);
        form.Controls.Add(pinned);
        var backend = Realize(form);

        form.Cursor = Cursors.Wait;

        var peers = backend.Created.OfType<HeadlessLabelPeer>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(peers[0].Cursor, Is.SameAs(Cursors.Wait), "the unset child follows the parent");
            Assert.That(peers[1].Cursor, Is.SameAs(Cursors.Cross), "an own cursor cuts off the cascade");
        });
    }

    [Test]
    public void LinkLabel_shows_the_hand_over_the_link_and_restores_it_off_the_text()
    {
        var link = new LinkLabel { Text = "Go", Bounds = new(0, 0, 200, 24) };
        var form = new Form();
        form.Controls.Add(link);
        var backend = Realize(form);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        // "Go" measures 14×16 headlessly, middle-left: the text spans x 0..14, y 4..20.
        canvas.RaiseMouseMove(5, 10);
        Assert.That(canvas.Cursor, Is.SameAs(Cursors.Hand), "over the text");

        canvas.RaiseMouseMove(150, 10);
        Assert.That(canvas.Cursor, Is.SameAs(Cursors.Arrow), "past the text the ambient arrow returns");
    }
}
