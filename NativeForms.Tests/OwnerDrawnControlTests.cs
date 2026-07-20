using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class OwnerDrawnControlTests
{
    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    [Test]
    public void Panel_paints_background_and_border()
    {
        var panel = new Panel { Bounds = new(0, 0, 50, 40), BorderStyle = BorderStyle.FixedSingle };
        var canvas = Realize(panel);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("fill ")), Is.True, "fills background");
            Assert.That(g.Operations.Exists(o => o.StartsWith("rect ")), Is.True, "draws border");
        });
    }

    [Test]
    public void CheckBox_toggles_on_click_and_raises_events()
    {
        var check = new CheckBox { Text = "Enable", Bounds = new(0, 0, 120, 20) };
        var changes = 0;
        check.CheckedChanged += (_, _) => ++changes;
        var canvas = Realize(check);

        canvas.RaiseMouseUp(5, 10);
        Assert.That(check.Checked, Is.True);

        canvas.RaiseMouseUp(5, 10);
        Assert.Multiple(() =>
        {
            Assert.That(check.Checked, Is.False);
            Assert.That(changes, Is.EqualTo(2));
        });
    }

    [Test]
    public void CheckBox_toggles_on_space_key_release()
    {
        var check = new CheckBox { Bounds = new(0, 0, 120, 20) };
        var canvas = Realize(check);

        // Like the WinForms button base, Space acts on key-up — a held key must not auto-repeat.
        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(check.Checked, Is.False, "key-down alone must not toggle");

        canvas.RaiseKeyUp(Keys.Space);

        Assert.That(check.Checked, Is.True);
    }

    [Test]
    public void Disabled_control_receives_no_mouse_or_key_input()
    {
        var check = new CheckBox { Bounds = new(0, 0, 120, 20), Enabled = false };
        var canvas = Realize(check);

        canvas.RaiseMouseDown(5, 10);
        canvas.RaiseMouseUp(5, 10);
        canvas.RaiseKeyDown(Keys.Space);
        canvas.RaiseKeyUp(Keys.Space);

        Assert.That(check.Checked, Is.False, "the central gate must block every input path");
    }

    [Test]
    public void Control_inside_a_disabled_container_receives_no_input()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var panel = new Panel();
        var check = new CheckBox { Bounds = new(0, 0, 120, 20) };
        panel.Controls.Add(check);
        form.Controls.Add(panel);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Last();

        panel.Enabled = false;
        canvas.RaiseMouseUp(5, 10);
        canvas.RaiseKeyUp(Keys.Space);

        Assert.That(check.Checked, Is.False, "the gate honors the effective Enabled");
    }

    [Test]
    public void Mouse_down_focuses_a_focusable_owner_drawn_control()
    {
        var check = new CheckBox { Bounds = new(0, 0, 120, 20) };
        var canvas = Realize(check);

        canvas.RaiseMouseDown(5, 10);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.FocusRequested, Is.True, "click-focus happens centrally");
            Assert.That(check.Focused, Is.True);
        });
    }

    [Test]
    public void PerformClick_toggles_a_CheckBox_with_CheckedChanged_before_Click()
    {
        var check = new CheckBox();
        var sequence = new List<string>();
        check.CheckedChanged += (_, _) => sequence.Add($"changed:{check.Checked}");
        check.Click += (_, _) => sequence.Add("click");

        check.PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(check.Checked, Is.True, "PerformClick routes through the toggling OnClick chain");
            Assert.That(sequence, Is.EqualTo(new[] { "changed:True", "click" }), "CheckedChanged precedes Click");
        });

        check.Enabled = false;
        check.PerformClick();
        Assert.That(check.Checked, Is.True, "a disabled box never toggles");
    }

    [Test]
    public void PerformClick_toggles_a_ToggleSwitch_and_checks_a_RadioButton()
    {
        var toggle = new ToggleSwitch();
        toggle.PerformClick();
        Assert.That(toggle.Checked, Is.True);

        var radio = new RadioButton();
        radio.PerformClick();
        Assert.That(radio.Checked, Is.True);
    }

    [Test]
    public void CheckBox_paints_label_text()
    {
        var check = new CheckBox { Text = "Remember me", Bounds = new(0, 0, 160, 20) };
        var canvas = Realize(check);

        var g = canvas.RaisePaint();

        Assert.That(g.DrewText("Remember me"), Is.True);
    }

    [Test]
    public void ListBox_selects_row_under_click()
    {
        var list = new ListBox { Bounds = new(0, 0, 120, 88) }; // 4 rows at 22px
        list.Items.AddRange(["a", "b", "c", "d", "e", "f"]);
        var selections = 0;
        list.SelectedIndexChanged += (_, _) => ++selections;
        var canvas = Realize(list);

        canvas.RaiseMouseDown(10, 25); // second row (22..44)

        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndex, Is.EqualTo(1));
            Assert.That(list.SelectedItem, Is.EqualTo("b"));
            Assert.That(selections, Is.EqualTo(1));
        });
    }

    [Test]
    public void ListBox_arrow_keys_move_selection()
    {
        var list = new ListBox { Bounds = new(0, 0, 120, 88) };
        list.Items.AddRange(["a", "b", "c"]);
        var canvas = Realize(list);

        canvas.RaiseKeyDown(Keys.Down); // -1 -> 0
        canvas.RaiseKeyDown(Keys.Down); // 0 -> 1
        canvas.RaiseKeyDown(Keys.Down); // 1 -> 2
        canvas.RaiseKeyDown(Keys.Up);   // 2 -> 1

        Assert.That(list.SelectedIndex, Is.EqualTo(1));
    }

    [Test]
    public void ListBox_wheel_scrolls_and_clamps()
    {
        var list = new ListBox { Bounds = new(0, 0, 120, 44) }; // 2 visible rows
        list.Items.AddRange(["1", "2", "3", "4", "5", "6"]);
        var canvas = Realize(list);

        canvas.RaiseMouseWheel(-120); // scroll down 3 rows
        Assert.That(list.TopIndex, Is.EqualTo(3));

        canvas.RaiseMouseWheel(-120); // clamp at max (6 - 2 = 4)
        Assert.That(list.TopIndex, Is.EqualTo(4));

        canvas.RaiseMouseWheel(120); // back up
        Assert.That(list.TopIndex, Is.EqualTo(1));
    }

    [Test]
    public void ListBox_datasource_replaces_items_and_paints_them()
    {
        var list = new ListBox { Bounds = new(0, 0, 120, 88) };
        var canvas = Realize(list);

        list.DataSource = new[] { "Alpha", "Beta" };

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(list.Items, Has.Count.EqualTo(2));
            Assert.That(g.DrewText("Alpha"), Is.True);
            Assert.That(g.DrewText("Beta"), Is.True);
        });
    }

    [Test]
    public void ListBox_removing_selected_item_clamps_selection()
    {
        var list = new ListBox { Bounds = new(0, 0, 120, 88) };
        list.Items.AddRange(["a", "b", "c"]);
        list.SelectedIndex = 2;
        var canvas = Realize(list);

        list.Items.RemoveAt(2);

        Assert.That(list.SelectedIndex, Is.EqualTo(1));
    }
}
