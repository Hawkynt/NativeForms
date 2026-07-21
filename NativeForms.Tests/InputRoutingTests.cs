using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;
using NUnit.Framework;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Guards the routing contract every backend owes the toolkit: an input event belongs to the
/// innermost surface under the pointer and to nobody else. GTK hands an unhandled event to each
/// ancestor in turn without retranslating its coordinates, so a container that re-handled a child's
/// event would hit-test the child's client point against its own layout — which is exactly how a
/// click on a control inside a <see cref="TabPage"/> used to land in the <see cref="TabControl"/>'s
/// header strip and switch tabs.
/// </summary>
[TestFixture]
internal sealed class InputRoutingTests
{
    /// <summary>A tab control that records every mouse-down its own surface is handed.</summary>
    private sealed class RecordingTabControl : TabControl
    {
        public List<Point> MouseDowns { get; } = [];

        protected override void OnMouseDown(MouseEventArgs e)
        {
            this.MouseDowns.Add(e.Location);
            base.OnMouseDown(e);
        }
    }

    /// <summary>A panel that records every mouse-down its own surface is handed.</summary>
    private sealed class RecordingPanel : Panel
    {
        public List<Point> MouseDowns { get; } = [];

        protected override void OnMouseDown(MouseEventArgs e)
        {
            this.MouseDowns.Add(e.Location);
            base.OnMouseDown(e);
        }
    }

    /// <summary>The control's origin in its form's client space — where a click on it would land.</summary>
    private static Point ClientOffset(Control control)
    {
        var offset = Point.Empty;
        for (var c = control; c is not null and not Form; c = c.Parent)
        {
            offset.X += c.Bounds.X;
            offset.Y += c.Bounds.Y;
        }

        return offset;
    }

    [Test]
    public void A_click_on_a_control_inside_a_TabPage_does_not_reach_the_TabControl()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 400, 300) };
        var tabs = new RecordingTabControl { Bounds = new(0, 0, 400, 260) };
        var first = new TabPage("First");
        var second = new TabPage("Second");
        tabs.TabPages.Add(first);
        tabs.TabPages.Add(second);
        var panel = new RecordingPanel { Bounds = new(20, 10, 300, 150) };
        var box = new CheckBox { Text = "Nested", Bounds = new(10, 6, 160, 24) };
        panel.Controls.Add(box);
        second.Controls.Add(panel);
        form.Controls.Add(tabs);
        Application.Run(form, backend);
        tabs.SelectedIndex = 1;

        // Near the checkbox's top-left, so the untranslated client point an ancestor would wrongly
        // re-use (10, 10) falls inside the tab header strip and onto the first tab.
        var window = backend.Created.OfType<HeadlessWindowPeer>().Single();
        var origin = ClientOffset(box);
        var route = window.RouteMouseDown(new(origin.X + 10, origin.Y + 10));
        window.RouteMouseUp(new(origin.X + 10, origin.Y + 10));

        Assert.Multiple(() =>
        {
            Assert.That(route.Location, Is.EqualTo(new Point(10, 10)), "the checkbox must see its own client coordinates");
            Assert.That(box.Checked, Is.True, "the click must reach the nested control");
            Assert.That(panel.MouseDowns, Is.Empty, "an ancestor must not also receive the child's event");
            Assert.That(tabs.MouseDowns, Is.Empty, "an ancestor must not also receive the child's event");
            Assert.That(tabs.SelectedIndex, Is.EqualTo(1), "the page must not switch when an unrelated control is clicked");
        });
    }

    [Test]
    public void Routing_delivers_an_event_to_the_innermost_surface_only()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 400, 300) };
        var outer = new RecordingPanel { Bounds = new(30, 20, 300, 200) };
        var inner = new RecordingPanel { Bounds = new(15, 25, 200, 120) };
        outer.Controls.Add(inner);
        form.Controls.Add(outer);
        Application.Run(form, backend);

        var window = backend.Created.OfType<HeadlessWindowPeer>().Single();
        var route = window.RouteMouseDown(new(30 + 15 + 7, 20 + 25 + 9));

        Assert.Multiple(() =>
        {
            Assert.That(route.Location, Is.EqualTo(new Point(7, 9)), "the point must be translated into the target's space");
            Assert.That(inner.MouseDowns, Is.EqualTo(new[] { new Point(7, 9) }));
            Assert.That(outer.MouseDowns, Is.Empty);
        });
    }

    [Test]
    public void A_click_over_a_hidden_page_lands_on_the_page_that_is_showing()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 400, 300) };
        var tabs = new TabControl { Bounds = new(0, 0, 400, 260) };
        var first = new TabPage("First");
        var second = new TabPage("Second");
        var onFirst = new RecordingPanel { Bounds = new(10, 10, 200, 100) };
        var onSecond = new RecordingPanel { Bounds = new(10, 10, 200, 100) };
        first.Controls.Add(onFirst);
        second.Controls.Add(onSecond);
        tabs.TabPages.Add(first);
        tabs.TabPages.Add(second);
        form.Controls.Add(tabs);
        Application.Run(form, backend);
        tabs.SelectedIndex = 1;

        var window = backend.Created.OfType<HeadlessWindowPeer>().Single();
        var origin = ClientOffset(onSecond);
        window.RouteMouseDown(new(origin.X + 5, origin.Y + 5));

        Assert.Multiple(() =>
        {
            Assert.That(onSecond.MouseDowns, Is.EqualTo(new[] { new Point(5, 5) }));
            Assert.That(onFirst.MouseDowns, Is.Empty, "the hidden page must not receive input");
        });
    }

    [Test]
    public void PointToScreen_of_a_nested_control_accumulates_every_ancestor_offset()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 500, 400) };
        var tabs = new TabControl { Bounds = new(7, 3, 480, 380) };
        var page = new TabPage("Page");
        tabs.TabPages.Add(page);
        var outer = new Panel { Bounds = new(37, 23, 300, 250) };
        var inner = new Panel { Bounds = new(19, 41, 200, 150) };
        var picture = new PictureBox { Bounds = new(13, 29, 100, 80) };
        inner.Controls.Add(picture);
        outer.Controls.Add(inner);
        page.Controls.Add(outer);
        form.Controls.Add(tabs);
        Application.Run(form, backend);

        var window = backend.Created.OfType<HeadlessWindowPeer>().Single();
        window.ScreenOrigin = new(640, 480);
        var expected = ClientOffset(picture);

        Assert.Multiple(() =>
        {
            Assert.That(
                picture.PointToScreen(Point.Empty),
                Is.EqualTo(new Point(640 + expected.X, 480 + expected.Y)));
            Assert.That(
                picture.PointToScreen(new(6, 4)),
                Is.EqualTo(new Point(640 + expected.X + 6, 480 + expected.Y + 4)));
            Assert.That(
                picture.PointToScreen(Point.Empty),
                Is.Not.EqualTo(outer.PointToScreen(Point.Empty)),
                "nesting must move the screen origin, or placement bugs stay invisible");
        });
    }

    [Test]
    public void A_context_menu_on_a_container_does_not_open_from_a_click_on_its_child()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 400, 300) };
        var panel = new Panel { Bounds = new(60, 40, 300, 200) };
        var picture = new PictureBox { Bounds = new(120, 90, 120, 80) };
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Reset"));
        panel.ContextMenuStrip = menu;
        panel.Controls.Add(picture);
        form.Controls.Add(panel);
        Application.Run(form, backend);

        var window = backend.Created.OfType<HeadlessWindowPeer>().Single();
        var origin = ClientOffset(picture);
        window.RouteMouseDown(new(origin.X + 30, origin.Y + 20), MouseButtons.Right);

        Assert.That(
            menu.IsOpen,
            Is.False,
            "the container must not open its menu from a child's click — it would place it at the child's coordinates");
    }
}
