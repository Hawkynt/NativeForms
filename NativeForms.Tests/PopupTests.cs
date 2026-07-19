using System;
using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class PopupTests
{
    [Test]
    public void CreatePopup_returns_a_canvas_capable_peer()
    {
        var backend = new HeadlessBackend();

        var popup = backend.CreatePopup();

        Assert.That(popup, Is.InstanceOf<Hawkynt.NativeForms.Backends.ICanvasPeer>());
        var peer = (HeadlessPopupPeer)popup;
        var painted = 0;
        var pressed = 0;
        popup.Paint += (_, _) => ++painted;
        popup.MouseDown += (_, e) =>
        {
            ++pressed;
            Assert.That((e.X, e.Y), Is.EqualTo((12, 34)));
        };

        peer.RaisePaint();
        peer.RaiseMouseDown(12, 34);

        Assert.Multiple(() =>
        {
            Assert.That(painted, Is.EqualTo(1));
            Assert.That(pressed, Is.EqualTo(1));
        });
    }

    [Test]
    public void ShowAt_records_position_and_size()
    {
        var peer = (HeadlessPopupPeer)new HeadlessBackend().CreatePopup();

        peer.ShowAt(new(100, 250), new(180, 90));

        Assert.Multiple(() =>
        {
            Assert.That(peer.IsShown, Is.True);
            Assert.That(peer.ShowCalls, Is.EqualTo(new[] { (new Point(100, 250), new Size(180, 90)) }));
        });
    }

    [Test]
    public void Hide_records_and_marks_the_surface_hidden()
    {
        var peer = (HeadlessPopupPeer)new HeadlessBackend().CreatePopup();
        peer.ShowAt(new(10, 20), new(50, 60));

        peer.Hide();

        Assert.Multiple(() =>
        {
            Assert.That(peer.IsShown, Is.False);
            Assert.That(peer.HideCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void FireDismiss_hides_first_then_raises_Dismissed_exactly_once()
    {
        var peer = (HeadlessPopupPeer)new HeadlessBackend().CreatePopup();
        peer.ShowAt(new(0, 0), new(80, 40));
        var dismissals = 0;
        peer.Dismissed += (sender, e) =>
        {
            ++dismissals;
            Assert.Multiple(() =>
            {
                // Dismissal semantics: the surface is already hidden when the event arrives.
                Assert.That(peer.IsShown, Is.False);
                Assert.That(peer.HideCount, Is.EqualTo(1));
                Assert.That(sender, Is.SameAs(peer));
                Assert.That(e, Is.SameAs(EventArgs.Empty));
            });
        };

        peer.FireDismiss();

        Assert.That(dismissals, Is.EqualTo(1));
    }

    [Test]
    public void FireDismiss_while_hidden_does_nothing()
    {
        var peer = (HeadlessPopupPeer)new HeadlessBackend().CreatePopup();
        var dismissals = 0;
        peer.Dismissed += (_, _) => ++dismissals;

        peer.FireDismiss();
        peer.ShowAt(new(0, 0), new(80, 40));
        peer.FireDismiss();
        peer.FireDismiss();

        Assert.Multiple(() =>
        {
            Assert.That(dismissals, Is.EqualTo(1));
            Assert.That(peer.HideCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Peer_PointToScreen_offsets_the_client_point_by_the_screen_origin()
    {
        var peer = (HeadlessPopupPeer)new HeadlessBackend().CreatePopup();
        peer.ScreenOrigin = new(300, 400);

        Assert.Multiple(() =>
        {
            Assert.That(peer.PointToScreen(Point.Empty), Is.EqualTo(new Point(300, 400)));
            Assert.That(peer.PointToScreen(new(7, 11)), Is.EqualTo(new Point(307, 411)));
        });
    }

    [Test]
    public void Control_PointToScreen_throws_before_realization()
    {
        var panel = new Panel();
        Assert.Throws<InvalidOperationException>(() => panel.PointToScreen(Point.Empty));
    }

    [Test]
    public void Control_PointToScreen_maps_through_the_peer_after_realization()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var panel = new Panel { Bounds = new(10, 10, 200, 150) };
        form.Controls.Add(panel);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        canvas.ScreenOrigin = new(640, 480);

        Assert.That(panel.PointToScreen(new(5, 8)), Is.EqualTo(new Point(645, 488)));
    }
}
