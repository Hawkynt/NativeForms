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

        var popup = backend.CreatePopup(null);

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
        var peer = (HeadlessPopupPeer)new HeadlessBackend().CreatePopup(null);

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
        var peer = (HeadlessPopupPeer)new HeadlessBackend().CreatePopup(null);
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
        var peer = (HeadlessPopupPeer)new HeadlessBackend().CreatePopup(null);
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
        var peer = (HeadlessPopupPeer)new HeadlessBackend().CreatePopup(null);
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
        var peer = (HeadlessPopupPeer)new HeadlessBackend().CreatePopup(null);
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

    // --- Every popup names the window it belongs to ----------------------------------------------
    //
    // A floating surface is a separate native window. A platform that is not told which window owns it
    // treats it as an unrelated application window: it cannot anchor it to its opener, and it marks
    // that opener inactive while the surface is up — which is how opening a menu greyed out the window
    // behind it. These assertions pin the owner at the seam it travels through, so a control that
    // forgets to name its form is caught without a display.

    /// <summary>Realizes a control on a form and hands back the backend that recorded it all.</summary>
    private static HeadlessBackend Realize(Control control, out Form form)
    {
        var backend = new HeadlessBackend();
        form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend;
    }

    /// <summary>The single popup surface the backend was asked for.</summary>
    private static HeadlessPopupPeer PopupOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessPopupPeer>().Single();

    [Test]
    public void ComboBox_drop_down_is_owned_by_the_form_it_sits_on()
    {
        var combo = new ComboBox { Bounds = new(10, 10, 120, 24) };
        combo.Items.Add("Mercury");
        combo.Items.Add("Venus");
        var backend = Realize(combo, out var form);

        combo.OpenDropDown();

        Assert.That(PopupOf(backend).OwnerWindow, Is.SameAs(form.WindowPeer));
    }

    [Test]
    public void DateTimePicker_calendar_is_owned_by_the_form_it_sits_on()
    {
        var picker = new DateTimePicker { Bounds = new(10, 10, 200, 24) };
        var backend = Realize(picker, out var form);

        picker.OpenDropDown();

        Assert.That(PopupOf(backend).OwnerWindow, Is.SameAs(form.WindowPeer));
    }

    [Test]
    public void MenuStrip_drop_down_is_owned_by_the_form_it_sits_on()
    {
        var menu = new MenuStrip { Bounds = new(0, 0, 300, 24) };
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add(new ToolStripMenuItem("New"));
        menu.Items.Add(file);
        var backend = Realize(menu, out var form);

        menu.OpenDropDown(0);

        Assert.That(PopupOf(backend).OwnerWindow, Is.SameAs(form.WindowPeer));
    }
}
