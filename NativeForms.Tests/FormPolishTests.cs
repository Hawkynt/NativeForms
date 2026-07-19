using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class FormPolishTests
{
    private static (HeadlessBackend Backend, HeadlessWindowPeer Peer) Realize(Form form)
    {
        var backend = new HeadlessBackend();
        Application.Run(form, backend);
        return (backend, backend.Created.OfType<HeadlessWindowPeer>().Single());
    }

    [Test]
    public void StartPosition_defaults_to_Manual_and_leaves_the_bounds_alone()
    {
        var form = new Form { Bounds = new(30, 40, 200, 100) };

        var (_, peer) = Realize(form);

        Assert.Multiple(() =>
        {
            Assert.That(form.StartPosition, Is.EqualTo(FormStartPosition.Manual));
            Assert.That(peer.Bounds, Is.EqualTo(new Rectangle(30, 40, 200, 100)));
        });
    }

    [Test]
    public void CenterScreen_centers_the_form_on_the_screen_at_show_time()
    {
        var backend = new HeadlessBackend { ScreenSize = new(1000, 800) };
        var form = new Form { Bounds = new(0, 0, 200, 100), StartPosition = FormStartPosition.CenterScreen };

        Application.Run(form, backend);

        var peer = backend.Created.OfType<HeadlessWindowPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(form.Bounds, Is.EqualTo(new Rectangle(400, 350, 200, 100)));
            Assert.That(peer.Bounds, Is.EqualTo(new Rectangle(400, 350, 200, 100)));
        });
    }

    [Test]
    public void CenterParent_centers_the_dialog_within_its_owner()
    {
        var backend = new HeadlessBackend { ModalAction = static window => window.Close() };
        var owner = new Form { Bounds = new(100, 100, 400, 300) };
        Application.Run(owner, backend);

        var dialog = new Form { Bounds = new(0, 0, 200, 100), StartPosition = FormStartPosition.CenterParent };
        dialog.ShowDialog(owner, backend);

        Assert.That(dialog.Bounds, Is.EqualTo(new Rectangle(200, 200, 200, 100)));
    }

    [Test]
    public void CenterParent_without_an_owner_centers_on_the_screen()
    {
        var backend = new HeadlessBackend
        {
            ScreenSize = new(1000, 800),
            ModalAction = static window => window.Close(),
        };
        var dialog = new Form { Bounds = new(0, 0, 200, 100), StartPosition = FormStartPosition.CenterParent };

        dialog.ShowDialog(null, backend);

        Assert.That(dialog.Bounds, Is.EqualTo(new Rectangle(400, 350, 200, 100)));
    }

    [Test]
    public void Border_style_buffers_then_flushes_and_forwards_live()
    {
        var form = new Form { FormBorderStyle = FormBorderStyle.FixedDialog };

        var (_, peer) = Realize(form);
        Assert.That(peer.BorderStyle, Is.EqualTo(FormBorderStyle.FixedDialog));

        form.FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Assert.That(peer.BorderStyle, Is.EqualTo(FormBorderStyle.FixedToolWindow));
    }

    [Test]
    public void Window_state_buffers_then_flushes_and_forwards_live()
    {
        var form = new Form { WindowState = FormWindowState.Maximized };

        var (_, peer) = Realize(form);
        Assert.That(peer.WindowState, Is.EqualTo(FormWindowState.Maximized));

        form.WindowState = FormWindowState.Normal;
        Assert.That(peer.WindowState, Is.EqualTo(FormWindowState.Normal));
    }

    [Test]
    public void Peer_state_change_syncs_WindowState_and_raises_Resize_without_echo()
    {
        var form = new Form();
        var (_, peer) = Realize(form);
        var stateCalls = peer.Calls.Count(static c => c.StartsWith("state="));
        var resized = 0;
        form.Resize += (_, _) => ++resized;

        peer.FireWindowStateChanged(FormWindowState.Maximized);

        Assert.Multiple(() =>
        {
            Assert.That(form.WindowState, Is.EqualTo(FormWindowState.Maximized));
            Assert.That(resized, Is.EqualTo(1));
            Assert.That(peer.Calls.Count(static c => c.StartsWith("state=")), Is.EqualTo(stateCalls));
        });
    }

    [Test]
    public void Minimize_and_maximize_boxes_buffer_then_flush_and_forward_live()
    {
        var form = new Form { MinimizeBox = false };

        var (_, peer) = Realize(form);
        Assert.Multiple(() =>
        {
            Assert.That(peer.MinimizeBox, Is.False);
            Assert.That(peer.MaximizeBox, Is.True);
        });

        form.MaximizeBox = false;
        Assert.That(peer.MaximizeBox, Is.False);
    }

    [Test]
    public void Size_limits_flush_to_the_peer_and_clamp_the_form()
    {
        var form = new Form
        {
            Bounds = new(0, 0, 500, 400),
            MinimumSize = new(100, 80),
            MaximumSize = new(300, 200),
        };

        var (_, peer) = Realize(form);

        Assert.Multiple(() =>
        {
            Assert.That(form.Size, Is.EqualTo(new Size(300, 200)));
            Assert.That(peer.MinimumSize, Is.EqualTo(new Size(100, 80)));
            Assert.That(peer.MaximumSize, Is.EqualTo(new Size(300, 200)));
        });
    }

    [Test]
    public void Minimum_size_clamps_a_live_form_and_reaches_the_peer()
    {
        var form = new Form { Bounds = new(0, 0, 50, 40) };
        var (_, peer) = Realize(form);

        form.MinimumSize = new(200, 150);

        Assert.Multiple(() =>
        {
            Assert.That(form.Size, Is.EqualTo(new Size(200, 150)));
            Assert.That(peer.MinimumSize, Is.EqualTo(new Size(200, 150)));
            Assert.That(peer.Bounds.Size, Is.EqualTo(new Size(200, 150)));
        });
    }

    [Test]
    public void Peer_driven_resize_updates_Bounds_and_raises_Resize_without_echo()
    {
        var form = new Form { Bounds = new(10, 10, 200, 100) };
        var (_, peer) = Realize(form);
        var resized = 0;
        var sizeChanged = 0;
        form.Resize += (_, _) => ++resized;
        form.SizeChanged += (_, _) => ++sizeChanged;

        peer.FireBoundsChanged(new(5, 6, 300, 200));

        Assert.Multiple(() =>
        {
            Assert.That(form.Bounds, Is.EqualTo(new Rectangle(5, 6, 300, 200)));
            Assert.That(resized, Is.EqualTo(1));
            Assert.That(sizeChanged, Is.EqualTo(1));

            // No echo: the peer's recorded bounds still hold what the core last pushed.
            Assert.That(peer.Bounds, Is.EqualTo(new Rectangle(10, 10, 200, 100)));
        });
    }

    [Test]
    public void Peer_driven_move_updates_Bounds_without_raising_Resize()
    {
        var form = new Form { Bounds = new(10, 10, 200, 100) };
        var (_, peer) = Realize(form);
        var resized = 0;
        form.Resize += (_, _) => ++resized;

        peer.FireBoundsChanged(new(70, 80, 200, 100));

        Assert.Multiple(() =>
        {
            Assert.That(form.Bounds, Is.EqualTo(new Rectangle(70, 80, 200, 100)));
            Assert.That(resized, Is.Zero);
        });
    }

    [Test]
    public void Programmatic_resize_raises_Resize_and_SizeChanged()
    {
        var form = new Form { Bounds = new(0, 0, 200, 100) };
        Realize(form);
        var events = new List<string>();
        form.Resize += (_, _) => events.Add("resize");
        form.SizeChanged += (_, _) => events.Add("sizeChanged");

        form.Size = new(300, 150);

        Assert.That(events, Is.EqualTo(new[] { "resize", "sizeChanged" }));
    }

    [Test]
    public void Icon_pixels_buffer_then_flush_and_forward_live()
    {
        var form = new Form();
        form.SetIcon(2, 1, [unchecked((int)0xFF112233), unchecked((int)0xFF445566)]);

        var (_, peer) = Realize(form);
        Assert.Multiple(() =>
        {
            Assert.That(peer.IconWidth, Is.EqualTo(2));
            Assert.That(peer.IconHeight, Is.EqualTo(1));
            Assert.That(peer.IconPixels, Is.EqualTo(new[] { unchecked((int)0xFF112233), unchecked((int)0xFF445566) }));
        });

        form.SetIcon(1, 1, [unchecked((int)0xFF778899)]);
        Assert.That(peer.IconPixels, Is.EqualTo(new[] { unchecked((int)0xFF778899) }));
    }

    [Test]
    public void Icon_with_a_wrong_pixel_count_throws()
        => Assert.Throws<ArgumentException>(() => new Form().SetIcon(2, 2, [1, 2, 3]));

    [Test]
    public void TopMost_and_Opacity_buffer_then_flush_and_forward_live()
    {
        var form = new Form { TopMost = true, Opacity = 0.5 };

        var (_, peer) = Realize(form);
        Assert.Multiple(() =>
        {
            Assert.That(peer.TopMost, Is.True);
            Assert.That(peer.Opacity, Is.EqualTo(0.5));
        });

        form.TopMost = false;
        form.Opacity = 0.75;
        Assert.Multiple(() =>
        {
            Assert.That(peer.TopMost, Is.False);
            Assert.That(peer.Opacity, Is.EqualTo(0.75));
        });
    }

    [Test]
    public void Opacity_is_clamped_to_the_unit_interval()
    {
        var form = new Form();

        form.Opacity = 2.5;
        Assert.That(form.Opacity, Is.EqualTo(1d));

        form.Opacity = -1;
        Assert.That(form.Opacity, Is.Zero);
    }

    [Test]
    public void Window_management_defaults_match_WinForms()
    {
        var form = new Form();

        Assert.Multiple(() =>
        {
            Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
            Assert.That(form.WindowState, Is.EqualTo(FormWindowState.Normal));
            Assert.That(form.MinimizeBox, Is.True);
            Assert.That(form.MaximizeBox, Is.True);
            Assert.That(form.MinimumSize, Is.EqualTo(Size.Empty));
            Assert.That(form.MaximumSize, Is.EqualTo(Size.Empty));
            Assert.That(form.TopMost, Is.False);
            Assert.That(form.Opacity, Is.EqualTo(1d));
        });
    }
}
