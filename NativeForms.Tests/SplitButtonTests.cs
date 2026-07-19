using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A standalone <see cref="SplitButton"/> must paint a button face with a separated arrow zone, run
/// its main action (Click + command, gated by <c>CanExecute</c>) from the main zone, open its
/// drop-down from the arrow zone anchored below the control, honor the keyboard split (Enter/Space
/// click, Down opens), and ignore input while disabled.
/// </summary>
[TestFixture]
internal sealed class SplitButtonTests
{
    /// <summary>Realizes a 100×28 split button with one menu item and returns all the actors.</summary>
    private static SplitButton CreateButton(out ToolStripMenuItem item, out HeadlessCanvasPeer canvas, out HeadlessBackend backend)
    {
        var button = new SplitButton { Text = "Save", Bounds = new(0, 0, 100, 28) };
        item = new ToolStripMenuItem("Save As");
        button.DropDownItems.Add(item);
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(button);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        canvas.ScreenOrigin = new(50, 80);
        return button;
    }

    private static HeadlessPopupPeer PopupOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessPopupPeer>().Single();

    [Test]
    public void Paints_face_caption_separator_and_arrow()
    {
        CreateButton(out _, out var canvas, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FFFDFDFD 0,0,99,27"), "face fill");
            Assert.That(g.Operations, Does.Contain("rect #FFC8C8C8 0,0,99,27"), "face border");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Save\" #FF1A1A1A") && o.EndsWith("@30,6")), Is.True, "caption centered in the main zone");
            Assert.That(g.Operations, Does.Contain("line #FFC8C8C8 88,3-88,24"), "separator ahead of the arrow zone");
            Assert.That(g.Operations.Exists(o => o.StartsWith("line #FF1A1A1A 91,")), Is.True, "arrow glyph in the arrow zone");
        });
    }

    [Test]
    public void Image_paints_before_the_caption()
    {
        var button = CreateButton(out _, out var canvas, out _);
        button.Image = new HeadlessImage(16, 16);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("image 16x16 @20,6,16,16"), "icon leads the centered block");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Save\"") && o.EndsWith("@40,6")), Is.True, "caption follows the icon");
        });
    }

    [Test]
    public void Main_zone_click_raises_Click_and_executes_the_command_without_opening_the_menu()
    {
        var button = CreateButton(out _, out var canvas, out var backend);
        var clicks = 0;
        var runs = 0;
        button.Click += (_, _) => ++clicks;
        button.Command = new RelayCommand(() => ++runs);

        canvas.RaiseMouseDown(30, 10);
        canvas.RaiseMouseUp(30, 10);

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(runs, Is.EqualTo(1));
            Assert.That(backend.Created.OfType<HeadlessPopupPeer>(), Is.Empty);
        });
    }

    [Test]
    public void Command_that_cannot_execute_gates_the_main_action()
    {
        var button = CreateButton(out _, out var canvas, out _);
        var clicks = 0;
        var runs = 0;
        button.Click += (_, _) => ++clicks;
        button.Command = new RelayCommand(() => ++runs, () => false);

        canvas.RaiseMouseDown(30, 10);
        canvas.RaiseMouseUp(30, 10);

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.Zero);
            Assert.That(runs, Is.Zero);
        });
    }

    [Test]
    public void Arrow_zone_click_opens_the_drop_down_below_the_control()
    {
        var button = CreateButton(out _, out var canvas, out var backend);
        var clicks = 0;
        button.Click += (_, _) => ++clicks;

        canvas.RaiseMouseDown(94, 10);

        var popup = PopupOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(button.IsDropDownOpen, Is.True);
            Assert.That(popup.ShowCalls.Single().Location, Is.EqualTo(new Point(50, 108)), "anchored at the bottom-left corner");
            Assert.That(clicks, Is.Zero, "the arrow zone must not run the main action");
        });
    }

    [Test]
    public void Drop_down_item_click_commits_and_closes()
    {
        var button = CreateButton(out var item, out var canvas, out var backend);
        var clicks = 0;
        item.Click += (_, _) => ++clicks;
        canvas.RaiseMouseDown(94, 10);

        PopupOf(backend).RaiseMouseDown(30, 10);

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(button.IsDropDownOpen, Is.False);
        });
    }

    [Test]
    public void Enter_and_Space_click_the_main_action()
    {
        var button = CreateButton(out _, out var canvas, out _);
        var clicks = 0;
        button.Click += (_, _) => ++clicks;

        canvas.RaiseKeyDown(Keys.Enter);
        canvas.RaiseKeyDown(Keys.Space);

        Assert.That(clicks, Is.EqualTo(2));
    }

    [Test]
    public void Down_and_Alt_Down_open_the_drop_down()
    {
        var button = CreateButton(out _, out var canvas, out _);

        canvas.RaiseKeyDown(Keys.Down);
        Assert.That(button.IsDropDownOpen, Is.True);

        button.CloseDropDown();
        canvas.RaiseKeyDown(Keys.Down, KeyModifiers.Alt);
        Assert.That(button.IsDropDownOpen, Is.True);
    }

    [Test]
    public void Without_items_the_arrow_does_not_open_anything()
    {
        var button = CreateButton(out var item, out var canvas, out var backend);
        button.DropDownItems.Remove(item);

        canvas.RaiseMouseDown(94, 10);

        Assert.Multiple(() =>
        {
            Assert.That(button.IsDropDownOpen, Is.False);
            Assert.That(backend.Created.OfType<HeadlessPopupPeer>(), Is.Empty);
        });
    }

    [Test]
    public void Disabled_button_ignores_input()
    {
        var button = CreateButton(out _, out var canvas, out var backend);
        var clicks = 0;
        button.Click += (_, _) => ++clicks;
        button.Enabled = false;

        canvas.RaiseMouseDown(30, 10);
        canvas.RaiseMouseUp(30, 10);
        canvas.RaiseMouseDown(94, 10);
        canvas.RaiseKeyDown(Keys.Enter);
        canvas.RaiseKeyDown(Keys.Down);

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.Zero);
            Assert.That(backend.Created.OfType<HeadlessPopupPeer>(), Is.Empty);
        });
    }
}
