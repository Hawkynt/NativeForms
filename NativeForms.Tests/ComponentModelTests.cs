using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The minimal component model: the non-visual building blocks are <see cref="Component"/>s, a
/// <see cref="Container"/> owns them and disposes them (last-added first) with itself,
/// <see cref="Component.Dispose()"/> detaches from the container and raises
/// <see cref="Component.Disposed"/> exactly once, and a removed component survives the container.
/// </summary>
[TestFixture]
internal sealed class ComponentModelTests
{
    [Test]
    public void The_non_visual_building_blocks_are_components()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new Timer(), Is.InstanceOf<Component>());
            Assert.That(new ToolTip(), Is.InstanceOf<Component>());
            Assert.That(new NotifyIcon(), Is.InstanceOf<Component>());
            Assert.That(new ContextMenuStrip(), Is.InstanceOf<Component>());
        });
    }

    [Test]
    public void Adding_a_component_sets_its_container()
    {
        var container = new Container();
        var timer = new Timer();

        container.Add(timer);

        Assert.Multiple(() =>
        {
            Assert.That(timer.Container, Is.SameAs(container));
            Assert.That(container.Components, Is.EqualTo(new Component[] { timer }));
        });
    }

    [Test]
    public void Adding_to_a_second_container_moves_the_component()
    {
        var first = new Container();
        var second = new Container();
        var timer = new Timer();
        first.Add(timer);

        second.Add(timer);

        Assert.Multiple(() =>
        {
            Assert.That(first.Components, Is.Empty);
            Assert.That(timer.Container, Is.SameAs(second));
        });
    }

    [Test]
    public void Disposing_the_container_disposes_its_components_last_added_first()
    {
        var container = new Container();
        var disposed = new List<string>();
        var first = new Timer();
        var second = new Timer();
        first.Disposed += (_, _) => disposed.Add("first");
        second.Disposed += (_, _) => disposed.Add("second");
        container.Add(first);
        container.Add(second);

        container.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(disposed, Is.EqualTo(new[] { "second", "first" }));
            Assert.That(container.Components, Is.Empty);
        });
    }

    [Test]
    public void Container_disposal_releases_native_peers()
    {
        var backend = new HeadlessBackend();
        var container = new Container();
        var timer = new Timer(backend) { Interval = 10 };
        container.Add(timer);
        timer.Start();

        container.Dispose();

        Assert.That(backend.Timers.Single().Disposed, Is.True);
    }

    [Test]
    public void A_removed_component_survives_the_containers_disposal()
    {
        var container = new Container();
        var timer = new Timer();
        var disposed = false;
        timer.Disposed += (_, _) => disposed = true;
        container.Add(timer);

        container.Remove(timer);
        container.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(disposed, Is.False);
            Assert.That(timer.Container, Is.Null);
        });
    }

    [Test]
    public void Disposing_a_component_detaches_it_and_raises_Disposed_once()
    {
        var container = new Container();
        var timer = new Timer();
        var raised = 0;
        timer.Disposed += (_, _) => ++raised;
        container.Add(timer);

        timer.Dispose();
        timer.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(timer.Container, Is.Null);
            Assert.That(container.Components, Is.Empty);
        });
    }

    [Test]
    public void Disposing_a_context_menu_closes_it()
    {
        var backend = new HeadlessBackend();
        var control = new Panel { Bounds = new(0, 0, 100, 100) };
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Item"));
        menu.Show(control, new(10, 10));
        Assert.That(menu.IsOpen, Is.True, "precondition: the menu opened");

        menu.Dispose();

        Assert.That(menu.IsOpen, Is.False);
    }
}
