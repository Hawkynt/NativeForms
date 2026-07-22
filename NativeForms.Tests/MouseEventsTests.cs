using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The public Windows Forms mouse-event surface on <see cref="Control"/>: MouseMove/MouseEnter/
/// MouseLeave over the shared pointer channel (native widgets and owner-drawn surfaces alike),
/// MouseDown/MouseUp/MouseWheel and double-click detection on owner-drawn controls.
/// </summary>
[TestFixture]
internal sealed class MouseEventsTests
{
    private static HeadlessCanvasPeer RealizeOwnerDrawn(Control control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return (HeadlessCanvasPeer)control.Peer!;
    }

    [Test]
    public void MouseDown_Up_and_Wheel_fire_on_an_owner_drawn_control()
    {
        var box = new CheckBox { Bounds = new(0, 0, 80, 24) };
        var canvas = RealizeOwnerDrawn(box);
        MouseEventArgs? down = null, up = null, wheel = null;
        box.MouseDown += (_, e) => down = e;
        box.MouseUp += (_, e) => up = e;
        box.MouseWheel += (_, e) => wheel = e;

        canvas.RaiseMouseDown(10, 5);
        canvas.RaiseMouseUp(12, 6);
        canvas.RaiseMouseWheel(120, 10, 5);

        Assert.Multiple(() =>
        {
            Assert.That(down?.Location, Is.EqualTo(new Point(10, 5)));
            Assert.That(up?.Location, Is.EqualTo(new Point(12, 6)));
            Assert.That(wheel?.Delta, Is.EqualTo(120));
        });
    }

    [Test]
    public void MouseMove_raises_enter_once_then_leave_via_the_pointer_channel()
    {
        var box = new CheckBox { Bounds = new(0, 0, 80, 24) };
        var canvas = RealizeOwnerDrawn(box);
        var enters = 0;
        var leaves = 0;
        MouseEventArgs? lastMove = null;
        box.MouseEnter += (_, _) => ++enters;
        box.MouseLeave += (_, _) => ++leaves;
        box.MouseMove += (_, e) => lastMove = e;

        canvas.RaiseMouseMove(5, 5); // enter + move
        canvas.RaiseMouseMove(6, 6); // move only
        canvas.RaiseMouseLeave();    // leave
        canvas.RaiseMouseMove(7, 7); // enter again

        Assert.Multiple(() =>
        {
            Assert.That(enters, Is.EqualTo(2), "enter fires on the first move after each leave");
            Assert.That(leaves, Is.EqualTo(1));
            Assert.That(lastMove?.Location, Is.EqualTo(new Point(7, 7)));
        });
    }

    [Test]
    public void Two_quick_left_presses_raise_DoubleClick()
    {
        var box = new CheckBox { Bounds = new(0, 0, 80, 24) };
        var canvas = RealizeOwnerDrawn(box);
        var doubles = 0;
        MouseEventArgs? doubleArgs = null;
        box.DoubleClick += (_, _) => ++doubles;
        box.MouseDoubleClick += (_, e) => doubleArgs = e;

        canvas.RaiseMouseDown(10, 5);
        canvas.RaiseMouseUp(10, 5);
        canvas.RaiseMouseDown(11, 5); // within the time window and the pixel slop
        canvas.RaiseMouseUp(11, 5);

        Assert.Multiple(() =>
        {
            Assert.That(doubles, Is.EqualTo(1));
            Assert.That(doubleArgs?.Location, Is.EqualTo(new Point(11, 5)));
        });
    }

    [Test]
    public void A_far_second_press_is_not_a_double_click()
    {
        var box = new CheckBox { Bounds = new(0, 0, 200, 24) };
        var canvas = RealizeOwnerDrawn(box);
        var doubles = 0;
        box.DoubleClick += (_, _) => ++doubles;

        canvas.RaiseMouseDown(10, 5);
        canvas.RaiseMouseDown(120, 5); // same time window, but far outside the slop

        Assert.That(doubles, Is.Zero);
    }

    [Test]
    public void Native_widgets_surface_MouseMove_and_enter_leave_too()
    {
        var button = new Button { Bounds = new(0, 0, 80, 24) };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(button);
        Application.Run(form, backend);
        var peer = (HeadlessPeer)button.Peer!;
        var enters = 0;
        var moves = 0;
        var leaves = 0;
        button.MouseEnter += (_, _) => ++enters;
        button.MouseMove += (_, _) => ++moves;
        button.MouseLeave += (_, _) => ++leaves;

        peer.RaisePointerMove(3, 3);
        peer.RaisePointerMove(4, 4);
        peer.RaisePointerLeave();

        Assert.Multiple(() =>
        {
            Assert.That(enters, Is.EqualTo(1));
            Assert.That(moves, Is.EqualTo(2));
            Assert.That(leaves, Is.EqualTo(1));
        });
    }
}
