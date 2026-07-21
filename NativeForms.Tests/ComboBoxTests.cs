using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class ComboBoxTests
{
    private static HeadlessBackend Realize(ComboBox combo)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(combo);
        Application.Run(form, backend);
        return backend;
    }

    private static ComboBox CreateCombo(out HeadlessCanvasPeer canvas, out HeadlessBackend backend, params string[] items)
    {
        var combo = new ComboBox { Bounds = new(10, 10, 120, 24) };
        combo.Items.AddRange(items);
        backend = Realize(combo);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return combo;
    }

    private static HeadlessPopupPeer PopupOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessPopupPeer>().Single();

    [Test]
    public void Defaults_are_drop_down_list_with_eight_visible_items()
    {
        var combo = new ComboBox();
        Assert.Multiple(() =>
        {
            Assert.That(combo.DropDownStyle, Is.EqualTo(ComboBoxStyle.DropDownList));
            Assert.That(combo.MaxDropDownItems, Is.EqualTo(8));
            Assert.That(combo.SelectedIndex, Is.EqualTo(-1));
            Assert.That(combo.DroppedDown, Is.False);
        });
    }

    [Test]
    public void Closed_field_paints_the_selected_text_and_the_arrow_zone()
    {
        var combo = CreateCombo(out var canvas, out _, "apple", "banana");
        combo.SelectedIndex = 1;

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("banana"), Is.True, "selected display text");
            // The arrow glyph is stacked lines confined to the button zone at the right edge.
            Assert.That(g.Operations.Exists(o => o.StartsWith("line ") && o.Contains($"{combo.Width - 12},")), Is.True, "arrow glyph in the button zone");
        });
    }

    [Test]
    public void Closed_field_paints_the_placeholder_in_disabled_color_while_nothing_is_selected()
    {
        var combo = CreateCombo(out var canvas, out _, "apple", "banana");
        combo.PlaceholderText = "pick one";

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("text \"pick one\"") && o.Contains("#FF9A9A9A")), Is.True, "placeholder in DisabledText");
    }

    [Test]
    public void Closed_field_paints_the_selected_item_icon_when_a_selector_is_present()
    {
        var combo = CreateCombo(out var canvas, out var backend, "apple", "banana");
        var icon = backend.CreateImage(16, 16, new int[256]);
        combo.ImageSelector = _ => icon;
        combo.SelectedIndex = 0;

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("image 16x16")), Is.True);
    }

    [Test]
    public void Opening_shows_the_popup_below_the_field_at_field_width()
    {
        var combo = CreateCombo(out var canvas, out var backend, "a", "b", "c");
        canvas.ScreenOrigin = new(300, 400);

        canvas.RaiseMouseDown(115, 12); // the arrow zone

        var popup = PopupOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(combo.DroppedDown, Is.True);
            Assert.That(popup.ShowCalls, Is.EqualTo(new[] { (new Point(300, 424), new Size(120, 3 * 22)) }));
        });
    }

    [Test]
    public void MaxDropDownItems_caps_the_popup_height()
    {
        var combo = CreateCombo(out var canvas, out var backend, "a", "b", "c", "d", "e", "f")!;
        combo.MaxDropDownItems = 4;
        canvas.ScreenOrigin = new(0, 0);

        canvas.RaiseMouseDown(5, 5);

        Assert.That(PopupOf(backend).ShowCalls[0].Size, Is.EqualTo(new Size(120, 4 * 22)));
    }

    [Test]
    public void Hovered_row_is_committed_by_click_and_raises_SelectedIndexChanged_once()
    {
        var combo = CreateCombo(out var canvas, out var backend, "apple", "banana", "cherry");
        var changes = 0;
        combo.SelectedIndexChanged += (_, _) => ++changes;
        canvas.RaiseMouseDown(5, 5);
        var popup = PopupOf(backend);

        popup.RaiseMouseMove(10, 47); // row 2
        popup.RaiseMouseDown(10, 47);

        Assert.Multiple(() =>
        {
            Assert.That(combo.SelectedIndex, Is.EqualTo(2));
            Assert.That(combo.SelectedItem, Is.EqualTo("cherry"));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(combo.DroppedDown, Is.False);
            Assert.That(popup.IsShown, Is.False);
        });
    }

    [Test]
    public void Popup_paints_rows_with_hover_highlight_and_icons()
    {
        var combo = CreateCombo(out var canvas, out var backend, "apple", "banana");
        var icon = backend.CreateImage(16, 16, new int[256]);
        combo.ImageSelector = _ => icon;
        canvas.RaiseMouseDown(5, 5);
        var popup = PopupOf(backend);
        popup.RaiseMouseMove(10, 25); // hover row 1

        var g = popup.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("apple"), Is.True);
            Assert.That(g.DrewText("banana"), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("fill #FF0078D4 0,22,120,22")), Is.True, "hover row in selection color");
            Assert.That(g.Operations.FindAll(o => o.StartsWith("image 16x16")), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void Escape_and_light_dismiss_close_without_changing_the_selection()
    {
        var combo = CreateCombo(out var canvas, out var backend, "apple", "banana", "cherry");
        combo.SelectedIndex = 0;
        var changes = 0;
        combo.SelectedIndexChanged += (_, _) => ++changes;
        canvas.RaiseMouseDown(5, 5);
        var popup = PopupOf(backend);
        popup.RaiseMouseMove(10, 47); // hover row 2, not committed

        popup.FireDismiss();

        Assert.Multiple(() =>
        {
            Assert.That(combo.DroppedDown, Is.False);
            Assert.That(combo.SelectedIndex, Is.Zero);
            Assert.That(changes, Is.Zero);
        });

        canvas.RaiseMouseDown(5, 5);
        canvas.RaiseKeyDown(Keys.Escape);

        Assert.Multiple(() =>
        {
            Assert.That(combo.DroppedDown, Is.False);
            Assert.That(popup.IsShown, Is.False);
            Assert.That(combo.SelectedIndex, Is.Zero);
        });
    }

    [Test]
    public void Alt_Down_and_F4_open_the_drop_down()
    {
        var combo = CreateCombo(out var canvas, out _, "a", "b");

        canvas.RaiseKeyDown(Keys.Down, KeyModifiers.Alt);
        Assert.That(combo.DroppedDown, Is.True);

        canvas.RaiseKeyDown(Keys.Escape);
        Assert.That(combo.DroppedDown, Is.False);

        canvas.RaiseKeyDown(Keys.F4);
        Assert.That(combo.DroppedDown, Is.True);
    }

    [Test]
    public void Closed_arrows_change_the_selection_directly()
    {
        var combo = CreateCombo(out var canvas, out _, "a", "b", "c");

        canvas.RaiseKeyDown(Keys.Down);
        canvas.RaiseKeyDown(Keys.Down);
        Assert.That(combo.SelectedIndex, Is.EqualTo(1));

        canvas.RaiseKeyDown(Keys.Up);
        Assert.That(combo.SelectedIndex, Is.Zero);
    }

    [Test]
    public void Closed_typing_cycles_through_the_prefix_matches()
    {
        var combo = CreateCombo(out var canvas, out _, "apple", "banana", "blueberry", "cherry");

        canvas.RaiseKeyPress('b');
        Assert.That(combo.SelectedItem, Is.EqualTo("banana"));

        canvas.RaiseKeyPress('b');
        Assert.That(combo.SelectedItem, Is.EqualTo("blueberry"));

        canvas.RaiseKeyPress('b');
        Assert.That(combo.SelectedItem, Is.EqualTo("banana"), "cycles back around");
    }

    [Test]
    public void Open_typing_jumps_the_hover_and_Enter_commits_it()
    {
        var combo = CreateCombo(out var canvas, out _, "apple", "banana", "cherry");
        canvas.RaiseMouseDown(5, 5);

        canvas.RaiseKeyPress('c');
        Assert.That(combo.SelectedIndex, Is.EqualTo(-1), "typing only moves the hover");

        canvas.RaiseKeyDown(Keys.Enter);
        Assert.Multiple(() =>
        {
            Assert.That(combo.SelectedItem, Is.EqualTo("cherry"));
            Assert.That(combo.DroppedDown, Is.False);
        });
    }

    [Test]
    public void Open_arrows_move_the_hover_before_committing()
    {
        var combo = CreateCombo(out var canvas, out _, "a", "b", "c");
        combo.SelectedIndex = 0;
        canvas.RaiseMouseDown(5, 5);

        canvas.RaiseKeyDown(Keys.Down);
        canvas.RaiseKeyDown(Keys.Down);
        Assert.That(combo.SelectedIndex, Is.Zero, "arrows while open only move the hover");

        canvas.RaiseKeyDown(Keys.Enter);
        Assert.That(combo.SelectedIndex, Is.EqualTo(2));
    }

    [Test]
    public void Wheel_scrolls_the_popup_when_it_overflows()
    {
        var combo = CreateCombo(out var canvas, out var backend, "a", "b", "c", "d", "e", "f", "g", "h", "i", "j");
        canvas.RaiseMouseDown(5, 5);
        var popup = PopupOf(backend);

        popup.RaiseMouseWheel(-120);

        var g = popup.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("d"), Is.True, "scrolled three rows down");
            Assert.That(g.DrewText("a"), Is.False);
        });
    }

    [Test]
    public void SelectedValue_round_trips_through_the_value_selector()
    {
        var combo = new ComboBox
        {
            ValueSelector = static item => ((Fruit)item!).Id,
            DisplaySelector = static item => ((Fruit)item!).Name,
        };
        combo.Items.AddRange([new Fruit(1, "apple"), new Fruit(2, "banana")]);

        combo.SelectedValue = 2;
        Assert.Multiple(() =>
        {
            Assert.That(combo.SelectedIndex, Is.EqualTo(1));
            Assert.That(combo.SelectedValue, Is.EqualTo(2));
        });

        combo.SelectedValue = 99;
        Assert.That(combo.SelectedIndex, Is.EqualTo(-1), "no item carries that value");
    }

    [Test]
    public void DataSource_snapshots_the_sequence()
    {
        var source = new List<string> { "a", "b" };
        var combo = new ComboBox { DataSource = source };

        source.Add("c");

        Assert.That(combo.Items, Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void Removing_the_selected_item_clears_the_selection()
    {
        var combo = CreateCombo(out _, out _, "a", "b", "c");
        combo.SelectedIndex = 1;
        var changes = 0;
        combo.SelectedIndexChanged += (_, _) => ++changes;

        combo.Items.RemoveAt(1);

        Assert.Multiple(() =>
        {
            Assert.That(combo.SelectedIndex, Is.EqualTo(-1));
            Assert.That(changes, Is.EqualTo(1));
        });

        combo.Items.RemoveAt(0); // removing an unselected item is a no-op for the selection
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void DropDown_style_hosts_an_editor_over_the_field_area()
    {
        var combo = new ComboBox { Bounds = new(10, 10, 120, 24), DropDownStyle = ComboBoxStyle.DropDown };
        combo.Items.AddRange(["apple", "banana"]);
        var backend = Realize(combo);

        var editor = backend.Created.OfType<HeadlessTextBoxPeer>().Single();
        Assert.That(editor.Bounds, Is.EqualTo(new Rectangle(0, 0, 120 - 17, 24)));
    }

    [Test]
    public void DropDown_style_mirrors_text_between_the_editor_and_the_combo()
    {
        var combo = new ComboBox { Bounds = new(10, 10, 120, 24), DropDownStyle = ComboBoxStyle.DropDown };
        combo.Items.AddRange(["apple", "banana"]);
        var backend = Realize(combo);
        var editor = backend.Created.OfType<HeadlessTextBoxPeer>().Single();

        editor.SimulateUserInput("typed");
        Assert.That(combo.Text, Is.EqualTo("typed"));

        combo.SelectedIndex = 1;
        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo("banana"));
            Assert.That(combo.Text, Is.EqualTo("banana"));
        });

        combo.Text = "manual";
        Assert.That(editor.Text, Is.EqualTo("manual"));
    }

    private sealed record Fruit(int Id, string Name);

    [Test]
    public void DropDown_and_DropDownClosed_fire_at_open_and_close()
    {
        var combo = CreateCombo(out _, out var backend, "a", "b");
        var opens = 0;
        var closes = 0;
        combo.DropDown += (_, _) => ++opens;
        combo.DropDownClosed += (_, _) => ++closes;

        combo.OpenDropDown();
        Assert.That((opens, closes), Is.EqualTo((1, 0)));

        combo.CloseDropDown();
        Assert.That((opens, closes), Is.EqualTo((1, 1)));

        combo.OpenDropDown();
        PopupOf(backend).FireDismiss(); // light dismissal closes too
        Assert.That((opens, closes), Is.EqualTo((2, 2)));
    }

    /// <summary>A popup takes a grab, and toolkits report that grab as the field losing focus even
    /// though the user moved focus nowhere. Adopting it would close the surface that had just opened,
    /// so an open drop-down ignores the loss — and hears it again as soon as the surface is gone.</summary>
    [Test]
    public void An_open_drop_down_ignores_the_focus_loss_its_own_popup_grab_causes()
    {
        var combo = CreateCombo(out var canvas, out _, "a", "b");
        var losses = 0;
        combo.LostFocus += (_, _) => ++losses;

        combo.OpenDropDown();
        canvas.RaiseLostFocus();
        Assert.Multiple(() =>
        {
            Assert.That(combo.DroppedDown, Is.True, "the grab's focus report closed the drop-down");
            Assert.That(losses, Is.Zero);
        });

        combo.CloseDropDown();
        canvas.RaiseLostFocus();
        Assert.That(losses, Is.EqualTo(1));
    }

    /// <summary>Unrealizing while the drop-down is open tears the surface down with the peer, so the
    /// suppression must not outlive it and deafen the control to every later focus loss.</summary>
    [Test]
    public void Unrealizing_an_open_drop_down_does_not_leave_focus_loss_suppressed()
    {
        var combo = new ComboBox { Bounds = new(10, 10, 120, 24) };
        combo.Items.AddRange(["a", "b"]);
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(combo);
        Application.Run(form, backend);

        combo.OpenDropDown();
        form.Controls.Remove(combo);
        form.Controls.Add(combo);

        var losses = 0;
        combo.LostFocus += (_, _) => ++losses;
        backend.Created.OfType<HeadlessCanvasPeer>().Last().RaiseLostFocus();
        Assert.That(losses, Is.EqualTo(1));
    }
}
