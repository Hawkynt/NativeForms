using System.Linq;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>A <see cref="Toast"/> adds a severity-tinted <see cref="InfoBar"/> to a form and removes it
/// again when its timer elapses.</summary>
[TestFixture]
internal sealed class ToastTests
{
    [Test]
    public void Show_adds_a_severity_tinted_bar_anchored_to_the_bottom_right()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 600, 400) };
        Application.Run(form, backend);

        Toast.Show(form, "Saved", "All good", InfoBarSeverity.Success, 3000);

        var bar = form.Controls.OfType<InfoBar>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(bar.Severity, Is.EqualTo(InfoBarSeverity.Success));
            Assert.That(bar.Title, Is.EqualTo("Saved"));
            Assert.That(bar.Anchor, Is.EqualTo(AnchorStyles.Bottom | AnchorStyles.Right));
            Assert.That(bar.Bounds.Right, Is.LessThanOrEqualTo(form.ClientSize.Width), "the toast sits inside the form");
            Assert.That(bar.Bounds.Bottom, Is.LessThanOrEqualTo(form.ClientSize.Height));
        });
    }

    [Test]
    public void The_stack_never_climbs_out_of_the_form()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 600, 400) };
        Application.Run(form, backend);

        for (var i = 0; i < 12; ++i) // far more than the 400-px column can hold
            Toast.Show(form, "T" + i, "m", InfoBarSeverity.Info, 3000);

        foreach (var bar in form.Controls.OfType<InfoBar>())
            Assert.That(bar.Bounds.Y, Is.GreaterThanOrEqualTo(0),
                $"toast {bar.Title} sits above the client area at Y={bar.Bounds.Y}");
    }

    [Test]
    public void A_form_too_short_for_a_toast_still_keeps_it_inside()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 300, 40) }; // shorter than one 36-px bar plus margins
        Application.Run(form, backend);

        Toast.Show(form, "Squeezed", "m", InfoBarSeverity.Info, 3000);

        var bar = form.Controls.OfType<InfoBar>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(bar.Bounds.Y, Is.GreaterThanOrEqualTo(0));
            Assert.That(bar.Bounds.Bottom, Is.LessThanOrEqualTo(form.ClientSize.Height),
                "a toast must never hang past the bottom edge — that is what re-grew the form");
        });
    }

    [Test]
    public void Multiple_toasts_stack_upward_without_overlapping()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 600, 400) };
        Application.Run(form, backend);

        Toast.Show(form, "First", "one", InfoBarSeverity.Info, 3000);
        Toast.Show(form, "Second", "two", InfoBarSeverity.Info, 3000);

        var bars = form.Controls.OfType<InfoBar>().OrderBy(b => b.Bounds.Y).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(bars, Has.Count.EqualTo(2), "both toasts are live");
            Assert.That(bars[0].Bounds.Bottom, Is.LessThanOrEqualTo(bars[1].Bounds.Y), "the older toast sits fully above the newer one");
            Assert.That(bars[1].Bounds.Bottom, Is.LessThanOrEqualTo(form.ClientSize.Height));
        });
    }
}
