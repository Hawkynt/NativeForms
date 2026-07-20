using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class ToolStripTests
{
    /// <summary>Realizes a toolbar of the given width on a fresh form and returns its canvas.</summary>
    private static ToolStrip CreateStrip(out HeadlessCanvasPeer canvas, out HeadlessBackend backend, int width = 300)
    {
        var strip = new ToolStrip { Bounds = new(0, 0, width, 28) };
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(strip);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        canvas.ScreenOrigin = new(50, 80);
        return strip;
    }

    private static HeadlessPopupPeer PopupOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessPopupPeer>().Single();

    [Test]
    public void Buttons_paint_icon_and_caption()
    {
        var strip = CreateStrip(out var canvas, out var backend);
        strip.Items.Add(new ToolStripButton("Run") { Image = backend.CreateImage(16, 16, new int[256]) });

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            // Icon at the button padding, vertically centered: (4, (28-16)/2) = (4, 6).
            Assert.That(g.Operations.Exists(o => o.StartsWith("image 16x16 @4,6")), Is.True);
            Assert.That(g.DrewText("Run"), Is.True);
        });
    }

    [Test]
    public void Separator_paints_a_vertical_line()
    {
        var strip = CreateStrip(out var canvas, out _);
        strip.Items.Add(new ToolStripButton("Run")); // width 8 + 21 = 29
        strip.Items.Add(new ToolStripSeparator());   // spans x = 29..36, line at its middle

        var g = canvas.RaisePaint();

        Assert.That(g.Operations, Does.Contain("line #FFC8C8C8 32,3-32,24"));
    }

    [Test]
    public void Hover_and_pressed_states_paint_distinct_fills()
    {
        var strip = CreateStrip(out var canvas, out _);
        strip.Items.Add(new ToolStripButton("Run")); // width 29

        canvas.RaiseMouseMove(5, 5);
        Assert.That(canvas.RaisePaint().Operations, Does.Contain("fill #FFECECEC 0,0,29,28"), "hover fill");

        canvas.RaiseMouseDown(5, 5);
        Assert.That(canvas.RaisePaint().Operations, Does.Contain("fill #FF0078D4 0,0,29,28"), "pressed fill");
    }

    [Test]
    public void Click_commits_on_mouse_up_over_the_same_item_only()
    {
        var strip = CreateStrip(out var canvas, out _);
        var button = new ToolStripButton("Run");
        strip.Items.Add(button);
        var clicks = 0;
        button.Click += (_, _) => ++clicks;

        canvas.RaiseMouseDown(5, 5);
        canvas.RaiseMouseUp(200, 5); // released outside the button
        Assert.That(clicks, Is.Zero);

        canvas.RaiseMouseDown(5, 5);
        canvas.RaiseMouseUp(6, 5);
        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void CheckOnClick_toggles_and_paints_the_checked_frame()
    {
        var strip = CreateStrip(out var canvas, out _);
        var toggle = new ToolStripButton("Run") { CheckOnClick = true };
        strip.Items.Add(toggle);

        canvas.RaiseMouseDown(5, 5);
        canvas.RaiseMouseUp(5, 5);

        Assert.Multiple(() =>
        {
            Assert.That(toggle.Checked, Is.True);
            Assert.That(canvas.RaisePaint().Operations, Does.Contain("rect #FF0078D4 0,0,28,27"), "accent frame");
        });

        canvas.RaiseMouseDown(5, 5);
        canvas.RaiseMouseUp(5, 5);
        Assert.That(toggle.Checked, Is.False);
    }

    [Test]
    public void Command_gates_the_button_and_executes_on_click()
    {
        var strip = CreateStrip(out var canvas, out _);
        var runs = 0;
        var canRun = false;
        var command = new RelayCommand(() => ++runs, () => canRun);
        var button = new ToolStripButton("Run") { Command = command };
        strip.Items.Add(button);

        canvas.RaiseMouseDown(5, 5); // disabled buttons never arm the pressed state
        canvas.RaiseMouseUp(5, 5);
        Assert.That(runs, Is.Zero);

        canRun = true;
        command.RaiseCanExecuteChanged();
        canvas.RaiseMouseDown(5, 5);
        canvas.RaiseMouseUp(5, 5);
        Assert.That(runs, Is.EqualTo(1));
    }

    [Test]
    public void Disabled_button_paints_greyed_text()
    {
        var strip = CreateStrip(out var canvas, out _);
        strip.Items.Add(new ToolStripButton("Run") { Enabled = false });

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(static o => o.StartsWith("text \"Run\"") && o.Contains("#FF9A9A9A")), Is.True);
    }

    [Test]
    public void DropDownButton_opens_its_menu_below_the_bar()
    {
        var strip = CreateStrip(out var canvas, out var backend);
        var dropDown = new ToolStripDropDownButton("Menu"); // width 8 + 28 + 12 = 48
        dropDown.DropDownItems.Add(new ToolStripMenuItem("First"));
        strip.Items.Add(dropDown);

        canvas.RaiseMouseDown(5, 5); // anywhere on the button opens

        var popup = PopupOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(popup.IsShown, Is.True);
            Assert.That(popup.ShowCalls.Single().Location, Is.EqualTo(new Point(50, 108)));
            Assert.That(popup.RaisePaint().DrewText("First"), Is.True);
        });
    }

    [Test]
    public void SplitButton_clicks_in_the_main_zone_and_drops_down_in_the_arrow_zone()
    {
        var strip = CreateStrip(out var canvas, out var backend);
        var split = new ToolStripSplitButton("Split"); // width 8 + 35 + 12 = 55; arrow zone x >= 43
        split.DropDownItems.Add(new ToolStripMenuItem("Variant"));
        strip.Items.Add(split);
        var clicks = 0;
        split.Click += (_, _) => ++clicks;

        canvas.RaiseMouseDown(10, 5);
        canvas.RaiseMouseUp(10, 5);
        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(1), "main zone clicks");
            Assert.That(backend.Created.OfType<HeadlessPopupPeer>(), Is.Empty);
        });

        canvas.RaiseMouseDown(48, 5);
        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(1), "arrow zone must not click");
            Assert.That(PopupOf(backend).IsShown, Is.True);
        });
    }

    [Test]
    public void Overflow_reports_and_paints_the_chevron()
    {
        var strip = CreateStrip(out var canvas, out _, width: 100);
        strip.Items.AddRange(new ToolStripButton("AAAA"), new ToolStripButton("AAAA"), new ToolStripButton("AAAA")); // 3 × 36 > 100

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(strip.HasOverflow, Is.True);
            // The chevron zone starts at 100 - 16 = 84: a bar at the top of a down triangle.
            Assert.That(g.Operations, Does.Contain("line #FF1A1A1A 88,8-95,8"));
        });
    }

    [Test]
    public void Everything_fits_without_a_chevron()
    {
        var strip = CreateStrip(out _, out _, width: 300);
        strip.Items.AddRange(new ToolStripButton("AAAA"), new ToolStripButton("AAAA"), new ToolStripButton("AAAA"));

        Assert.That(strip.HasOverflow, Is.False);
    }

    [Test]
    public void Chevron_click_opens_the_overflowed_items_as_a_popup()
    {
        var strip = CreateStrip(out var canvas, out var backend, width: 100);
        var third = new ToolStripButton("AAAA");
        strip.Items.AddRange(new ToolStripButton("AAAA"), new ToolStripButton("AAAA"), third);
        var clicks = 0;
        third.Click += (_, _) => ++clicks;

        canvas.RaiseMouseDown(90, 5); // inside the chevron zone

        var popup = PopupOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(popup.IsShown, Is.True);
            // One "AAAA" row: width 2 + 24 + 28 + 16 = 70, right-aligned under the chevron.
            Assert.That(popup.ShowCalls.Single(), Is.EqualTo((new Point(50 + 100 - 70, 80 + 28), new Size(70, 24))));
            Assert.That(popup.RaisePaint().DrewText("AAAA"), Is.True);
        });

        popup.RaiseMouseDown(30, 10); // committing the overflowed button clicks it and closes
        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(popup.IsShown, Is.False);
        });
    }

    [Test]
    public void Item_width_cache_refreshes_when_an_item_changes()
    {
        var strip = CreateStrip(out var canvas, out _);
        var first = new ToolStripButton("Run");   // 29px wide
        var second = new ToolStripButton("Stop"); // spans x = 29..65
        var clicks = new List<string>();
        first.Click += (_, _) => clicks.Add("first");
        second.Click += (_, _) => clicks.Add("second");
        strip.Items.AddRange(first, second);

        canvas.RaiseMouseDown(35, 10);
        canvas.RaiseMouseUp(35, 10);
        Assert.That(clicks, Is.EqualTo(new[] { "second" }), "x=35 hits the second button while the first is 29px wide");

        first.Text = "RunAll"; // grows the first button to 50px
        canvas.RaiseMouseDown(35, 10);
        canvas.RaiseMouseUp(35, 10);
        Assert.That(clicks, Is.EqualTo(new[] { "second", "first" }), "the refreshed width routes x=35 to the first button");
    }
}
