using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class RealizationTests
{
    [Test]
    public void Run_realizes_window_children_and_shows()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Text = "Title", Bounds = new(0, 0, 200, 120) };
        var button = new Button { Text = "OK", Bounds = new(10, 10, 80, 30) };
        var label = new Label { Text = "Hi", Bounds = new(10, 50, 120, 20) };
        form.Controls.AddRange(button, label);

        Application.Run(form, backend);

        var window = backend.Created.OfType<HeadlessWindowPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(backend.DidRun, Is.True);
            Assert.That(window.Shown, Is.True);
            Assert.That(window.Text, Is.EqualTo("Title"));
            Assert.That(window.Children, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void Realized_child_receives_buffered_state()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var button = new Button { Text = "OK", Bounds = new(10, 10, 80, 30), Enabled = false };
        form.Controls.Add(button);

        Application.Run(form, backend);

        var peer = backend.Created.OfType<HeadlessButtonPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(peer.Text, Is.EqualTo("OK"));
            Assert.That(peer.Bounds, Is.EqualTo(new Rectangle(10, 10, 80, 30)));
            Assert.That(peer.Enabled, Is.False);
        });
    }

    [Test]
    public void Property_change_after_realization_forwards_to_peer()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var button = new Button();
        form.Controls.Add(button);
        Application.Run(form, backend);
        var peer = backend.Created.OfType<HeadlessButtonPeer>().Single();

        button.Text = "changed";
        button.Bounds = new(1, 2, 3, 4);

        Assert.Multiple(() =>
        {
            Assert.That(peer.Text, Is.EqualTo("changed"));
            Assert.That(peer.Bounds, Is.EqualTo(new Rectangle(1, 2, 3, 4)));
        });
    }

    [Test]
    public void Native_click_raises_control_click()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var button = new Button();
        form.Controls.Add(button);
        var clicks = 0;
        button.Click += (_, _) => ++clicks;

        Application.Run(form, backend);
        backend.Created.OfType<HeadlessButtonPeer>().Single().RaiseClicked();

        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void Control_inside_panel_realizes_onto_the_panels_canvas()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var panel = new Panel { Bounds = new(10, 10, 150, 100) };
        var button = new Button { Text = "OK", Bounds = new(5, 5, 80, 30) };
        panel.Controls.Add(button);
        form.Controls.Add(panel);

        Application.Run(form, backend);

        var window = backend.Created.OfType<HeadlessWindowPeer>().Single();
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        var buttonPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(window.Children, Is.EqualTo(new[] { canvas }));
            Assert.That(canvas.Children, Is.EqualTo(new[] { buttonPeer }));
            Assert.That(buttonPeer.Text, Is.EqualTo("OK"));
        });
    }

    [Test]
    public void Three_level_nesting_realizes_every_level()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var panel = new Panel { Bounds = new(0, 0, 300, 200) };
        var group = new GroupBox { Text = "Group", Bounds = new(10, 10, 200, 150) };
        var button = new Button { Text = "Deep", Bounds = new(20, 30, 80, 30) };
        group.Controls.Add(button);
        panel.Controls.Add(group);
        form.Controls.Add(panel);

        Application.Run(form, backend);

        // Realization is depth-first, so the panel's canvas is created before the group box's.
        var window = backend.Created.OfType<HeadlessWindowPeer>().Single();
        var canvases = backend.Created.OfType<HeadlessCanvasPeer>().ToArray();
        var buttonPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();
        Assert.That(canvases, Has.Length.EqualTo(2));
        var panelCanvas = canvases[0];
        var groupCanvas = canvases[1];
        Assert.Multiple(() =>
        {
            Assert.That(window.Children, Is.EqualTo(new[] { panelCanvas }));
            Assert.That(panelCanvas.Children, Is.EqualTo(new[] { groupCanvas }));
            Assert.That(groupCanvas.Children, Is.EqualTo(new[] { buttonPeer }));
            Assert.That(groupCanvas.Text, Is.EqualTo("Group"));
        });
    }

    [Test]
    public void Add_after_realization_realizes_the_child_immediately()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var panel = new Panel { Bounds = new(0, 0, 200, 200) };
        form.Controls.Add(panel);
        Application.Run(form, backend);
        var window = backend.Created.OfType<HeadlessWindowPeer>().Single();
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        var late = new Button { Text = "Late", Bounds = new(1, 2, 3, 4) };
        panel.Controls.Add(late);
        var direct = new Label { Text = "Direct" };
        form.Controls.Add(direct);

        var latePeer = backend.Created.OfType<HeadlessButtonPeer>().Single();
        var directPeer = backend.Created.OfType<HeadlessLabelPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(canvas.Children, Is.EqualTo(new[] { latePeer }));
            Assert.That(latePeer.Text, Is.EqualTo("Late"));
            Assert.That(latePeer.Bounds, Is.EqualTo(new Rectangle(1, 2, 3, 4)));
            Assert.That(window.Children, Does.Contain(directPeer));
            Assert.That(directPeer.Text, Is.EqualTo("Direct"));
        });
    }

    [Test]
    public void Remove_drops_the_container_peers_bookkeeping_entry_for_the_child()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var button = new Button { Text = "OK", Bounds = new(5, 5, 80, 30) };
        form.Controls.Add(button);
        Application.Run(form, backend);
        var window = backend.Created.OfType<HeadlessWindowPeer>().Single();
        var buttonPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();
        Assert.That(window.Children, Does.Contain(buttonPeer), "the child peer was added to the container");

        form.Controls.Remove(button);

        Assert.That(window.Children, Does.Not.Contain(buttonPeer),
            "the container must drop the removed child before its peer is disposed");
    }

    [Test]
    public void Remove_drops_a_canvas_container_entry_before_the_canvas_re_realizes()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var panel = new Panel { Bounds = new(10, 10, 150, 100) };
        var button = new Button { Text = "OK", Bounds = new(5, 5, 80, 30) };
        panel.Controls.Add(button);
        form.Controls.Add(panel);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        var buttonPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();
        Assert.That(canvas.Children, Does.Contain(buttonPeer));

        panel.Controls.Remove(button);

        Assert.That(canvas.Children, Does.Not.Contain(buttonPeer),
            "a canvas container must forget a removed child, not re-realize a disposed peer");
    }

    [Test]
    public void Remove_disposes_the_peer_tree_and_the_control_can_realize_again()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var panel = new Panel { Bounds = new(10, 10, 150, 100) };
        var button = new Button { Text = "OK", Bounds = new(5, 5, 80, 30) };
        panel.Controls.Add(button);
        form.Controls.Add(panel);
        Application.Run(form, backend);
        var firstCanvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        var firstButtonPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();

        form.Controls.Remove(panel);

        Assert.Multiple(() =>
        {
            Assert.That(firstCanvas.Disposed, Is.True);
            Assert.That(firstButtonPeer.Disposed, Is.True);
        });

        form.Controls.Add(panel);

        var secondCanvas = backend.Created.OfType<HeadlessCanvasPeer>().Last();
        var secondButtonPeer = backend.Created.OfType<HeadlessButtonPeer>().Last();
        Assert.Multiple(() =>
        {
            Assert.That(secondCanvas, Is.Not.SameAs(firstCanvas));
            Assert.That(secondCanvas.Disposed, Is.False);
            Assert.That(secondCanvas.Bounds, Is.EqualTo(new Rectangle(10, 10, 150, 100)));
            Assert.That(secondCanvas.Children, Is.EqualTo(new[] { secondButtonPeer }));
            Assert.That(secondButtonPeer.Text, Is.EqualTo("OK"));
            Assert.That(secondButtonPeer.Bounds, Is.EqualTo(new Rectangle(5, 5, 80, 30)));
        });
    }

    [Test]
    public void Clear_disposes_every_removed_peer_tree()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var panel = new Panel { Bounds = new(0, 0, 200, 200) };
        var button = new Button();
        var label = new Label();
        panel.Controls.AddRange(button, label);
        form.Controls.Add(panel);
        Application.Run(form, backend);

        form.Controls.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(backend.Created.OfType<HeadlessCanvasPeer>().Single().Disposed, Is.True);
            Assert.That(backend.Created.OfType<HeadlessButtonPeer>().Single().Disposed, Is.True);
            Assert.That(backend.Created.OfType<HeadlessLabelPeer>().Single().Disposed, Is.True);
            Assert.That(panel.Controls, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void Nested_child_bounds_stay_parent_relative()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var panel = new Panel { Bounds = new(50, 40, 200, 150) };
        var button = new Button { Bounds = new(10, 10, 80, 30) };
        panel.Controls.Add(button);
        form.Controls.Add(panel);

        Application.Run(form, backend);

        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        var buttonPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(canvas.Bounds, Is.EqualTo(new Rectangle(50, 40, 200, 150)));
            Assert.That(buttonPeer.Bounds, Is.EqualTo(new Rectangle(10, 10, 80, 30)));
        });
    }

    [Test]
    public void Window_close_raises_FormClosed()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var closed = 0;
        form.FormClosed += (_, _) => ++closed;

        Application.Run(form, backend);
        backend.Created.OfType<HeadlessWindowPeer>().Single().RaiseClosed();

        Assert.That(closed, Is.EqualTo(1));
    }
}
