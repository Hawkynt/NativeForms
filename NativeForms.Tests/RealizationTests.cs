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
