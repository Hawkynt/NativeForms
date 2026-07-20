using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="ToggleSwitch"/> must paint a pill-shaped track (grey off, accent on) with the thumb
/// on the matching side, toggle on click and Space raising <see cref="ToggleSwitch.CheckedChanged"/>
/// exactly once, ignore input while disabled, and place its caption beside the track.
/// </summary>
[TestFixture]
internal sealed class ToggleSwitchTests
{
    /// <summary>Realizes a 120×24 switch on a fresh form and returns its canvas.</summary>
    private static ToggleSwitch CreateSwitch(out HeadlessCanvasPeer canvas)
    {
        var toggle = new ToggleSwitch { Bounds = new(0, 0, 120, 24) };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(toggle);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return toggle;
    }

    [Test]
    public void Off_switch_paints_a_grey_pill_with_the_thumb_left()
    {
        CreateSwitch(out var canvas);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            // The pill: one rounded rectangle whose radius is half its height, border-grey while off.
            Assert.That(g.Operations, Does.Contain("fillround #FFC8C8C8 0,4,36,16 r8"), "track pill");
            Assert.That(g.Operations, Does.Contain("fillellipse #FFFFFFFF 2,6,12,12"), "thumb sits at the left end");
        });
    }

    [Test]
    public void On_switch_paints_the_accent_pill_with_the_thumb_right()
    {
        var toggle = CreateSwitch(out var canvas);
        toggle.Checked = true;

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fillround #FF0078D4 0,4,36,16 r8"), "accent track pill");
            Assert.That(g.Operations, Does.Contain("fillellipse #FFFFFFFF 22,6,12,12"), "thumb sits at the right end");
        });
    }

    [Test]
    public void Caption_paints_beside_the_track()
    {
        var toggle = CreateSwitch(out var canvas);
        toggle.Text = "Run";

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Run\" #FF1A1A1A") && o.EndsWith("@42,4")), Is.True, "text starts past the 36px track + 6px gap, vertically centered");
    }

    [Test]
    public void Click_toggles_and_raises_CheckedChanged_once()
    {
        var toggle = CreateSwitch(out var canvas);
        var changes = 0;
        var clicks = 0;
        toggle.CheckedChanged += (_, _) => ++changes;
        toggle.Click += (_, _) => ++clicks;

        canvas.RaiseMouseDown(10, 10);
        canvas.RaiseMouseUp(10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(toggle.Checked, Is.True);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(clicks, Is.EqualTo(1));
        });

        canvas.RaiseMouseDown(10, 10);
        canvas.RaiseMouseUp(10, 10);
        Assert.That(toggle.Checked, Is.False);
        Assert.That(changes, Is.EqualTo(2));
    }

    [Test]
    public void Space_toggles()
    {
        var toggle = CreateSwitch(out var canvas);

        canvas.RaiseKeyDown(Keys.Space);

        Assert.That(toggle.Checked, Is.True);
    }

    [Test]
    public void Other_keys_do_not_toggle()
    {
        var toggle = CreateSwitch(out var canvas);

        canvas.RaiseKeyDown(Keys.Enter);

        Assert.That(toggle.Checked, Is.False);
    }

    [Test]
    public void Assigning_the_same_value_does_not_re_raise()
    {
        var toggle = CreateSwitch(out _);
        var changes = 0;
        toggle.CheckedChanged += (_, _) => ++changes;

        toggle.Checked = true;
        toggle.Checked = true;

        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void Disabled_switch_ignores_input_and_paints_grey()
    {
        var toggle = CreateSwitch(out var canvas);
        toggle.Text = "Run";
        toggle.Checked = true;
        toggle.Enabled = false;

        canvas.RaiseMouseDown(10, 10);
        canvas.RaiseMouseUp(10, 10);
        canvas.RaiseKeyDown(Keys.Space);

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(toggle.Checked, Is.True, "input must be ignored");
            Assert.That(g.Operations, Does.Contain("fillround #FFC8C8C8 0,4,36,16 r8"), "no accent while disabled");
            Assert.That(g.Operations, Does.Contain("fillellipse #FFFFFFFF 22,6,12,12"), "the thumb still shows the on state");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Run\" #FF9A9A9A")), Is.True, "greyed caption");
        });
    }
}
