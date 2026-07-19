using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class NotifyIconTests
{
    [Test]
    public void Showing_realizes_the_peer_and_flushes_the_buffered_state()
    {
        var backend = new HeadlessBackend();
        using var icon = new NotifyIcon(backend) { Text = "Sync running" };
        icon.SetIcon(2, 2, new[] { 1, 2, 3, 4 });
        Assert.That(backend.NotifyIcons, Is.Empty, "nothing native exists while hidden");

        icon.Visible = true;

        var peer = backend.NotifyIcons.Single();
        Assert.Multiple(() =>
        {
            Assert.That(peer.Visible, Is.True);
            Assert.That(peer.ToolTip, Is.EqualTo("Sync running"));
            Assert.That((peer.IconWidth, peer.IconHeight), Is.EqualTo((2, 2)));
            Assert.That(peer.IconPixels, Is.EqualTo(new[] { 1, 2, 3, 4 }));
        });
    }

    [Test]
    public void State_changes_forward_to_the_realized_peer()
    {
        var backend = new HeadlessBackend();
        using var icon = new NotifyIcon(backend) { Visible = true };
        var peer = backend.NotifyIcons.Single();

        icon.Text = "Done";
        icon.SetIcon(1, 1, new[] { 42 });
        icon.Visible = false;

        Assert.Multiple(() =>
        {
            Assert.That(peer.ToolTip, Is.EqualTo("Done"));
            Assert.That(peer.IconPixels, Is.EqualTo(new[] { 42 }));
            Assert.That(peer.Visible, Is.False);
            Assert.That(backend.NotifyIcons, Has.Count.EqualTo(1), "hiding keeps the peer for the next show");
        });
    }

    [Test]
    public void SetIcon_validates_the_pixel_count()
    {
        using var icon = new NotifyIcon(new HeadlessBackend());
        Assert.Throws<ArgumentException>(() => icon.SetIcon(2, 2, new[] { 1, 2, 3 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => icon.SetIcon(0, 2, Array.Empty<int>()));
    }

    [Test]
    public void Clicks_forward_from_the_shell()
    {
        var backend = new HeadlessBackend();
        using var icon = new NotifyIcon(backend) { Visible = true };
        var clicks = 0;
        var doubleClicks = 0;
        icon.Click += (sender, e) =>
        {
            ++clicks;
            Assert.That(sender, Is.SameAs(icon));
            Assert.That(e, Is.SameAs(EventArgs.Empty));
        };
        icon.DoubleClick += (_, _) => ++doubleClicks;
        var peer = backend.NotifyIcons.Single();

        peer.FireClick();
        peer.FireClick();
        peer.FireDoubleClick();

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(2));
            Assert.That(doubleClicks, Is.EqualTo(1));
        });
    }

    [Test]
    public void Showing_without_a_running_loop_keeps_the_wish()
    {
        var icon = new NotifyIcon { Text = "Later" };

        icon.Visible = true; // no application loop: nothing to realize against yet

        Assert.That(icon.Visible, Is.True);
        icon.Dispose();
    }

    [Test]
    public void Dispose_removes_the_icon_and_releases_the_peer()
    {
        var backend = new HeadlessBackend();
        var icon = new NotifyIcon(backend) { Visible = true };
        var peer = backend.NotifyIcons.Single();

        icon.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(peer.Disposed, Is.True);
            Assert.That(peer.Visible, Is.False);
            Assert.That(icon.Visible, Is.False);
        });
    }
}
