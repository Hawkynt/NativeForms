using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class ContextMenuTests
{
    /// <summary>Realizes a panel carrying a two-item context menu and returns all the actors.</summary>
    private static Panel CreatePanel(out ContextMenuStrip menu, out ToolStripMenuItem copy, out HeadlessCanvasPeer canvas, out HeadlessBackend backend)
    {
        menu = new();
        copy = new("Copy");
        menu.Items.Add(copy);
        menu.Items.Add(new ToolStripMenuItem("Paste"));

        var panel = new Panel { Bounds = new(10, 10, 200, 150), ContextMenuStrip = menu };
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(panel);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        canvas.ScreenOrigin = new(400, 300);
        return panel;
    }

    private static HeadlessPopupPeer PopupOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessPopupPeer>().Single();

    [Test]
    public void Right_click_opens_the_menu_at_the_cursor()
    {
        CreatePanel(out var menu, out _, out var canvas, out var backend);

        canvas.RaiseMouseDown(30, 40, MouseButtons.Right);

        var popup = PopupOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(popup.IsShown, Is.True);
            Assert.That(popup.ShowCalls.Single().Location, Is.EqualTo(new Point(430, 340)));
        });
    }

    [Test]
    public void Left_click_does_not_open_the_menu()
    {
        CreatePanel(out var menu, out _, out var canvas, out var backend);

        canvas.RaiseMouseDown(30, 40);

        Assert.Multiple(() =>
        {
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(backend.Created.OfType<HeadlessPopupPeer>(), Is.Empty);
        });
    }

    [Test]
    public void Menu_paints_its_items()
    {
        CreatePanel(out _, out _, out var canvas, out var backend);
        canvas.RaiseMouseDown(30, 40, MouseButtons.Right);

        var g = PopupOf(backend).RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Copy"), Is.True);
            Assert.That(g.DrewText("Paste"), Is.True);
        });
    }

    [Test]
    public void Item_click_commits_closes_and_raises_Closed()
    {
        CreatePanel(out var menu, out var copy, out var canvas, out var backend);
        var clicks = 0;
        var closed = 0;
        copy.Click += (_, _) => ++clicks;
        menu.Closed += (_, _) => ++closed;
        canvas.RaiseMouseDown(30, 40, MouseButtons.Right);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(30, 10); // the "Copy" row

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(popup.IsShown, Is.False);
        });
    }

    [Test]
    public void Show_opens_at_an_explicit_client_point()
    {
        var panel = CreatePanel(out var menu, out _, out _, out var backend);

        menu.Show(panel, new(5, 6));

        Assert.That(PopupOf(backend).ShowCalls.Single().Location, Is.EqualTo(new Point(405, 306)));
    }

    [Test]
    public void Show_before_realization_is_a_no_op()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Copy"));

        menu.Show(new Panel(), new(5, 6));

        Assert.That(menu.IsOpen, Is.False);
    }

    [Test]
    public void A_parent_grab_break_under_an_open_submenu_keeps_the_cascade_open()
    {
        var menu = new ContextMenuStrip();
        var more = new ToolStripMenuItem("More");
        more.DropDownItems.Add(new ToolStripMenuItem("Child"));
        menu.Items.Add(more);

        var backend = new HeadlessBackend();
        var host = new Panel { Bounds = new(10, 10, 200, 150), ContextMenuStrip = menu };
        var form = new Form();
        form.Controls.Add(host);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        canvas.ScreenOrigin = new(400, 300);
        canvas.RaiseMouseDown(30, 40, MouseButtons.Right); // open the menu

        var parent = backend.Created.OfType<HeadlessPopupPeer>().Single();
        parent.RaiseMouseMove(20, (backend.Theme.RowHeight / 2) + 1); // hover "More" → opens the submenu
        Assert.That(backend.Created.OfType<HeadlessPopupPeer>().Count(p => p.IsShown), Is.EqualTo(2), "the submenu opened as a second popup");

        // The parent loses its grab to the submenu it just opened — a grab-broken some backends report
        // asynchronously, after the engine's synchronous suppression window has closed.
        parent.RaiseGrabBroken();

        Assert.Multiple(() =>
        {
            Assert.That(menu.IsOpen, Is.True, "the cascade stays open when the parent reports a grab-break under an open submenu");
            Assert.That(backend.Created.OfType<HeadlessPopupPeer>().Count(p => p.IsShown), Is.EqualTo(2), "both levels remain shown");
        });
    }

    [Test]
    public void Light_dismissal_closes_the_menu()
    {
        CreatePanel(out var menu, out _, out var canvas, out var backend);
        canvas.RaiseMouseDown(30, 40, MouseButtons.Right);

        PopupOf(backend).FireDismiss();

        Assert.That(menu.IsOpen, Is.False);
    }

    /// <summary>Realizes a native button carrying a context menu and returns all the actors.</summary>
    private static Button CreateButton(out ContextMenuStrip menu, out HeadlessButtonPeer peer, out HeadlessBackend backend)
    {
        menu = new();
        menu.Items.Add(new ToolStripMenuItem("Copy"));
        menu.Items.Add(new ToolStripMenuItem("Paste"));

        var button = new Button { Bounds = new(10, 10, 80, 26), Text = "Go", ContextMenuStrip = menu };
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(button);
        Application.Run(form, backend);
        peer = backend.Created.OfType<HeadlessButtonPeer>().Single();
        peer.ScreenOrigin = new(400, 300);
        return button;
    }

    [Test]
    public void Right_click_on_a_native_control_opens_its_menu_and_marks_the_request_handled()
    {
        CreateButton(out var menu, out var peer, out var backend);

        var handled = peer.RaiseContextMenu(5, 6);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True, "the core opened a menu, so the widget suppresses its own default one");
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(PopupOf(backend).ShowCalls.Single().Location, Is.EqualTo(new Point(405, 306)));
        });
    }

    [Test]
    public void A_native_context_menu_request_without_a_menu_is_left_for_the_widgets_own()
    {
        var button = new Button { Bounds = new(10, 10, 80, 26), Text = "Go" };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(button);
        Application.Run(form, backend);
        var peer = backend.Created.OfType<HeadlessButtonPeer>().Single();

        var handled = peer.RaiseContextMenu(5, 6);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.False, "with no ContextMenuStrip the request is left unhandled for the widget's own menu");
            Assert.That(backend.Created.OfType<HeadlessPopupPeer>(), Is.Empty);
        });
    }

    [Test]
    public void Opening_cancel_keeps_the_menu_closed_on_both_paths()
    {
        var panel = CreatePanel(out var menu, out _, out var canvas, out var backend);
        var openings = 0;
        menu.Opening += (_, e) =>
        {
            ++openings;
            e.Cancel = true;
        };

        canvas.RaiseMouseDown(30, 40, MouseButtons.Right); // the Control.ContextMenuStrip path
        menu.Show(panel, new Point(5, 5));                 // the explicit path

        Assert.Multiple(() =>
        {
            Assert.That(openings, Is.EqualTo(2));
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(backend.Created.OfType<HeadlessPopupPeer>(), Is.Empty, "no popup was ever created");
        });
    }
}
