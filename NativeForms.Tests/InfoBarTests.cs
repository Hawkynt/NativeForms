using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// An <see cref="InfoBar"/> paints a severity stripe/icon, hides and raises <see cref="InfoBar.Closed"/>
/// when its × is clicked, and raises <see cref="InfoBar.ActionClicked"/> from its action link.
/// </summary>
[TestFixture]
internal sealed class InfoBarTests
{
    private static InfoBar Create(out HeadlessCanvasPeer canvas)
    {
        var bar = new InfoBar { Bounds = new(0, 0, 400, 40), Title = "Saved", Message = "Your changes are saved." };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(bar);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return bar;
    }

    [Test]
    public void Clicking_the_close_button_hides_the_bar_and_raises_Closed()
    {
        var bar = Create(out var canvas);
        var closed = 0;
        bar.Closed += (_, _) => ++closed;

        canvas.RaiseMouseDown(387, 20); // the × zone (last 26 px)

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(bar.Visible, Is.False);
        });
    }

    [Test]
    public void The_error_severity_paints_a_red_stripe()
    {
        var bar = Create(out var canvas);
        bar.Severity = InfoBarSeverity.Error;

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("fill #FFE81123 0,0,4,40")), Is.True, "the leading stripe is the error colour");
    }

    [Test]
    public void Clicking_the_action_raises_ActionClicked()
    {
        var bar = Create(out var canvas);
        bar.ActionText = "Undo";
        var actions = 0;
        bar.ActionClicked += (_, _) => ++actions;

        canvas.RaiseMouseDown(352, 20); // the action zone, before the × button

        Assert.That(actions, Is.EqualTo(1));
    }
}
