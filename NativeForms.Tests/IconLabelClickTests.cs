using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A label with a handler on it is a control, not a picture of one.
/// </summary>
/// <remarks>
/// An owner-drawn control raises <see cref="Control.Click"/> only if it says so, and
/// <see cref="IconLabel"/> never did — so an application using one as a row, a link or a disclosure
/// triangle attached a handler that could never run, with nothing to say why.
/// </remarks>
[TestFixture]
internal sealed class IconLabelClickTests
{
    private static (IconLabel Label, HeadlessCanvasPeer Canvas) Realize()
    {
        var label = new IconLabel { Text = "Go", Bounds = new(0, 0, 80, 24) };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(label);
        Application.Run(form, backend);
        return (label, (HeadlessCanvasPeer)label.Peer!);
    }

    [Test]
    public void A_press_and_release_inside_it_is_a_click()
    {
        var (label, canvas) = Realize();
        var clicks = 0;
        label.Click += (_, _) => ++clicks;

        canvas.RaiseMouseDown(20, 12);
        canvas.RaiseMouseUp(20, 12);

        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void A_release_outside_it_is_not()
    {
        var (label, canvas) = Realize();
        var clicks = 0;
        label.Click += (_, _) => ++clicks;

        canvas.RaiseMouseDown(20, 12);
        canvas.RaiseMouseUp(200, 90);

        Assert.That(clicks, Is.Zero);
    }

    [Test]
    public void A_release_with_no_press_before_it_is_not()
    {
        var (label, canvas) = Realize();
        var clicks = 0;
        label.Click += (_, _) => ++clicks;

        canvas.RaiseMouseUp(20, 12);

        Assert.That(clicks, Is.Zero);
    }

    [Test]
    public void The_right_button_does_not_click_it()
    {
        var (label, canvas) = Realize();
        var clicks = 0;
        label.Click += (_, _) => ++clicks;

        canvas.RaiseMouseDown(20, 12, MouseButtons.Right);
        canvas.RaiseMouseUp(20, 12, MouseButtons.Right);

        Assert.That(clicks, Is.Zero);
    }
}
