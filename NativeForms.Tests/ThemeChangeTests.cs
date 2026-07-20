using System.Drawing;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Live theme-change plumbing: a backend's <see cref="Fakes.HeadlessBackend.FireThemeChanged"/> must
/// make every realized owner-drawn control adopt the backend's fresh theme and repaint, closing the
/// form must unsubscribe (no repaint requests to dead canvases), and the high-contrast flag must
/// surface through <see cref="ITheme.IsHighContrast"/>.
/// </summary>
[TestFixture]
internal sealed class ThemeChangeTests
{
    /// <summary>Realizes a checked check box on a fresh form and returns the backing actors.</summary>
    private static (Form Form, CheckBox Check, HeadlessCanvasPeer Canvas, HeadlessBackend Backend) Realize()
    {
        var check = new CheckBox { Text = "Follow", Bounds = new(0, 0, 120, 20), Checked = true };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(check);
        Application.Run(form, backend);
        return (form, check, backend.Created.OfType<HeadlessCanvasPeer>().Single(), backend);
    }

    [Test]
    public void Theme_change_repaints_realized_owner_drawn_controls()
    {
        var (_, _, canvas, backend) = Realize();
        var before = canvas.InvalidateCount;

        backend.FireThemeChanged();

        Assert.That(canvas.InvalidateCount, Is.GreaterThan(before), "the control must request a repaint");
    }

    [Test]
    public void Theme_change_adopts_the_backends_fresh_theme()
    {
        var (_, _, canvas, backend) = Realize();
        var lime = Color.FromArgb(0xFF, 0x00, 0xFF, 0x00);
        backend.Theme = new StubTheme { Accent = lime };

        backend.FireThemeChanged();

        // The checked box's border paints in the accent — now the swapped theme's lime.
        var g = canvas.RaisePaint();
        Assert.That(g.Operations.Exists(o => o.StartsWith("rect #FF00FF00")), Is.True, "the fresh accent must be painted");
    }

    [Test]
    public void Unrealizing_unsubscribes_from_theme_notifications()
    {
        var check = new CheckBox { Bounds = new(0, 0, 120, 20) };
        var backend = new HeadlessBackend { ModalAction = static window => window.Close() };
        var form = new Form();
        form.Controls.Add(check);
        form.ShowDialog(null, backend); // realizes, then unrealizes the whole tree on return

        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        var after = canvas.InvalidateCount;

        backend.FireThemeChanged();

        Assert.That(canvas.InvalidateCount, Is.EqualTo(after), "an unrealized control must not react");
    }

    [Test]
    public void Re_realized_control_tracks_theme_changes_exactly_once()
    {
        var check = new CheckBox { Bounds = new(0, 0, 120, 20) };
        var backend = new HeadlessBackend { ModalAction = static window => window.Close() };
        var form = new Form();
        form.Controls.Add(check);
        form.ShowDialog(null, backend);

        // The second showing realizes a fresh canvas; the control must re-subscribe exactly once.
        HeadlessCanvasPeer? canvas = null;
        var count = -1;
        backend.ModalAction = w =>
        {
            canvas = backend.Created.OfType<HeadlessCanvasPeer>().Last();
            var before = canvas.InvalidateCount;
            backend.FireThemeChanged();
            count = canvas.InvalidateCount - before;
            w.Close();
        };
        form.ShowDialog(null, backend);

        Assert.That(count, Is.EqualTo(1), "one subscription, one repaint request");
    }

    [Test]
    public void Default_theme_reports_no_high_contrast()
        => Assert.That(DefaultTheme.Instance.IsHighContrast, Is.False);
}
