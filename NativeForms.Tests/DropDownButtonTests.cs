using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A standalone <see cref="DropDownButton"/> must paint a button face with an arrow zone (no
/// separator — the whole surface is one action), open its drop-down below the control on any click
/// and on Down/Enter/Space, and ignore input while disabled.
/// </summary>
[TestFixture]
internal sealed class DropDownButtonTests
{
    /// <summary>Realizes a 100×28 drop-down button with one menu item and returns all the actors.</summary>
    private static DropDownButton CreateButton(out ToolStripMenuItem item, out HeadlessCanvasPeer canvas, out HeadlessBackend backend)
    {
        var button = new DropDownButton { Text = "Open", Bounds = new(0, 0, 100, 28) };
        item = new ToolStripMenuItem("Recent");
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
    public void Paints_face_caption_and_arrow_without_a_separator()
    {
        CreateButton(out _, out var canvas, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FFFDFDFD 0,0,99,27"), "face fill");
            Assert.That(g.Operations, Does.Contain("rect #FFC8C8C8 0,0,99,27"), "face border");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Open\" #FF1A1A1A") && o.EndsWith("@30,6")), Is.True, "caption centered in the content zone");
            Assert.That(g.Operations, Does.Not.Contain("line #FFC8C8C8 88,3-88,24"), "no split separator");
            Assert.That(g.Operations.Exists(o => o.StartsWith("line #FF1A1A1A 91,")), Is.True, "arrow glyph in the arrow zone");
        });
    }

    [Test]
    public void Any_click_opens_the_drop_down_below_the_control()
    {
        var button = CreateButton(out _, out var canvas, out var backend);

        canvas.RaiseMouseDown(30, 10);

        var popup = PopupOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(button.IsDropDownOpen, Is.True);
            Assert.That(popup.ShowCalls.Single().Location, Is.EqualTo(new Point(50, 108)), "anchored at the bottom-left corner");
        });
    }

    [Test]
    public void Arrow_zone_click_opens_as_well()
    {
        var button = CreateButton(out _, out var canvas, out _);

        canvas.RaiseMouseDown(94, 10);

        Assert.That(button.IsDropDownOpen, Is.True);
    }

    [Test]
    public void Drop_down_item_click_commits_and_closes()
    {
        var button = CreateButton(out var item, out var canvas, out var backend);
        var clicks = 0;
        item.Click += (_, _) => ++clicks;
        canvas.RaiseMouseDown(30, 10);

        PopupOf(backend).RaiseMouseDown(30, 10);

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(button.IsDropDownOpen, Is.False);
        });
    }

    [Test]
    public void Down_Enter_and_Space_open_the_drop_down()
    {
        var button = CreateButton(out _, out var canvas, out _);

        canvas.RaiseKeyDown(Keys.Down);
        Assert.That(button.IsDropDownOpen, Is.True, "Down");

        button.CloseDropDown();
        canvas.RaiseKeyDown(Keys.Enter);
        Assert.That(button.IsDropDownOpen, Is.True, "Enter");

        button.CloseDropDown();
        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(button.IsDropDownOpen, Is.True, "Space");
    }

    [Test]
    public void Command_gates_the_drop_down_items_not_the_button()
    {
        var button = CreateButton(out var item, out var canvas, out var backend);
        item.Enabled = false;
        var clicks = 0;
        item.Click += (_, _) => ++clicks;
        canvas.RaiseMouseDown(30, 10);

        PopupOf(backend).RaiseMouseDown(30, 10);

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.Zero, "a disabled item never commits");
            Assert.That(button.IsDropDownOpen, Is.True, "the cascade stays open");
        });
    }

    [Test]
    public void Disabled_button_ignores_input()
    {
        var button = CreateButton(out _, out var canvas, out var backend);
        button.Enabled = false;

        canvas.RaiseMouseDown(30, 10);
        canvas.RaiseKeyDown(Keys.Down);
        canvas.RaiseKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(button.IsDropDownOpen, Is.False);
            Assert.That(backend.Created.OfType<HeadlessPopupPeer>(), Is.Empty);
        });
    }
}
