using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class MenuStripTests
{
    /// <summary>Realizes a menu strip on a fresh form and returns its canvas.</summary>
    private static MenuStrip CreateStrip(out HeadlessCanvasPeer canvas, out HeadlessBackend backend)
    {
        var strip = new MenuStrip { Bounds = new(0, 0, 300, 24) };
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(strip);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        canvas.ScreenOrigin = new(100, 200);
        return strip;
    }

    /// <summary>The canonical File/Edit menu the fixture reuses: File → Open (Ctrl+O), ─, Exit.</summary>
    private static MenuStrip CreateFileMenu(out ToolStripMenuItem open, out ToolStripMenuItem exit, out HeadlessCanvasPeer canvas, out HeadlessBackend backend)
    {
        var strip = CreateStrip(out canvas, out backend);
        var file = new ToolStripMenuItem("&File");
        open = new("Open") { ShortcutKeys = Keys.Control | Keys.O };
        exit = new("Exit");
        file.DropDownItems.Add(open);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(exit);
        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.Add(new ToolStripMenuItem("Undo"));
        strip.Items.AddRange(file, edit);
        return strip;
    }

    private static HeadlessPopupPeer PopupOf(HeadlessBackend backend, int index = 0)
        => backend.Created.OfType<HeadlessPopupPeer>().ElementAt(index);

    // --- Item model -------------------------------------------------------------------------------

    [Test]
    public void Command_gates_Enabled_and_executes_on_click()
    {
        var canRun = false;
        var runs = 0;
        var command = new RelayCommand(() => ++runs, () => canRun);
        var item = new ToolStripMenuItem("Save") { Command = command };
        var clicks = 0;
        item.Click += (_, _) => ++clicks;

        Assert.That(item.Enabled, Is.False, "CanExecute=false must gate Enabled");
        item.PerformClick();
        Assert.Multiple(() =>
        {
            Assert.That(runs, Is.Zero);
            Assert.That(clicks, Is.Zero);
        });

        canRun = true;
        command.RaiseCanExecuteChanged();
        Assert.That(item.Enabled, Is.True);

        item.PerformClick();
        Assert.Multiple(() =>
        {
            Assert.That(runs, Is.EqualTo(1));
            Assert.That(clicks, Is.EqualTo(1));
        });
    }

    [Test]
    public void CanExecuteChanged_repaints_the_hosting_strip()
    {
        var strip = CreateStrip(out var canvas, out _);
        var command = new RelayCommand(static () => { });
        var item = new ToolStripMenuItem("Save") { Command = command };
        strip.Items.Add(item);
        var before = canvas.InvalidateCount;

        command.RaiseCanExecuteChanged();

        Assert.That(canvas.InvalidateCount, Is.GreaterThan(before));
    }

    [Test]
    public void CheckOnClick_toggles_Checked()
    {
        var item = new ToolStripMenuItem("Word wrap") { CheckOnClick = true };

        item.PerformClick();
        Assert.That(item.Checked, Is.True);

        item.PerformClick();
        Assert.That(item.Checked, Is.False);
    }

    [Test]
    public void CheckedGroup_members_are_mutually_exclusive()
    {
        var owner = new ToolStripItemCollection();
        var small = new ToolStripMenuItem("Small") { CheckOnClick = true, CheckedGroup = "size", Checked = true };
        var large = new ToolStripMenuItem("Large") { CheckOnClick = true, CheckedGroup = "size" };
        owner.AddRange(small, large);
        var smallChanges = 0;
        small.CheckedChanged += (_, _) => ++smallChanges;

        large.PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(large.Checked, Is.True);
            Assert.That(small.Checked, Is.False);
            Assert.That(smallChanges, Is.EqualTo(1));
        });

        large.PerformClick(); // radio semantics: clicking the checked member keeps it checked
        Assert.That(large.Checked, Is.True);
    }

    [Test]
    public void Mnemonic_parsing_strips_markers_and_escapes_double_ampersands()
    {
        var item = new ToolStripMenuItem("Save && &Reload");
        Assert.Multiple(() =>
        {
            Assert.That(item.DisplayText, Is.EqualTo("Save & Reload"));
            Assert.That(item.MnemonicIndex, Is.EqualTo(7));
        });
    }

    [Test]
    public void Shortcut_formatting_renders_the_chord()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ToolStripMenuItem.FormatShortcut(Keys.Control | Keys.S), Is.EqualTo("Ctrl+S"));
            Assert.That(ToolStripMenuItem.FormatShortcut(Keys.Control | Keys.Shift | Keys.D1), Is.EqualTo("Ctrl+Shift+1"));
            Assert.That(ToolStripMenuItem.FormatShortcut(Keys.Alt | Keys.F4), Is.EqualTo("Alt+F4"));
            Assert.That(ToolStripMenuItem.FormatShortcut(Keys.None), Is.Empty);
        });
    }

    // --- The bar ----------------------------------------------------------------------------------

    [Test]
    public void Bar_paints_item_captions_without_mnemonic_markers()
    {
        CreateFileMenu(out _, out _, out var canvas, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("File"), Is.True);
            Assert.That(g.DrewText("Edit"), Is.True);
            Assert.That(g.DrewText("&File"), Is.False, "the & marker must not render");
        });
    }

    [Test]
    public void Bar_underlines_the_mnemonic_character()
    {
        CreateFileMenu(out _, out _, out var canvas, out _);

        var g = canvas.RaisePaint();

        // "File" starts at x=8 (item padding); the underline spans the F (7px wide, so 8..14) at the
        // text baseline y = (24 + 16) / 2 - 1 = 19.
        Assert.That(g.Operations, Does.Contain($"line #FF1A1A1A 8,19-14,19"));
    }

    [Test]
    public void Bar_highlights_the_hovered_item()
    {
        CreateFileMenu(out _, out _, out var canvas, out _);
        canvas.RaiseMouseMove(5, 5); // "File" spans x = 0..44 (8 + 4*7 + 8)

        var g = canvas.RaisePaint();

        Assert.That(g.Operations, Does.Contain("fill #FF0078D4 0,0,44,24"));
    }

    // --- Opening and the drop-down ----------------------------------------------------------------

    [Test]
    public void Click_opens_the_drop_down_below_the_item_with_the_computed_size()
    {
        var strip = CreateFileMenu(out _, out _, out var canvas, out var backend);

        canvas.RaiseMouseDown(5, 5);

        var popup = PopupOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(strip.OpenIndex, Is.Zero);
            Assert.That(popup.IsShown, Is.True);

            // Anchored below the bar at the item's left edge: screen (100, 200) + (0, 24).
            // Width: 2 border + 24 icon column + 28 text ("Open") + 16 gap + 42 shortcut ("Ctrl+O")
            // + 16 arrow = 128; height: 2 border + 22 + 5 separator + 22 = 51.
            Assert.That(popup.ShowCalls.Single(), Is.EqualTo((new Point(100, 224), new Size(128, 51))));
        });
    }

    [Test]
    public void Drop_down_paints_text_shortcut_separator_and_hover()
    {
        CreateFileMenu(out _, out _, out var canvas, out var backend);
        canvas.RaiseMouseDown(5, 5);
        var popup = PopupOf(backend);
        popup.RaiseMouseMove(30, 10); // hover the "Open" row (y = 1..22)

        var g = popup.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Open"), Is.True);
            Assert.That(g.DrewText("Ctrl+O"), Is.True);
            Assert.That(g.DrewText("Exit"), Is.True);
            Assert.That(g.Operations, Does.Contain("fill #FF0078D4 1,1,126,22"), "hovered row highlight");
            Assert.That(g.Operations, Does.Contain("line #FFC8C8C8 25,25-126,25"), "separator line after the first row");
        });
    }

    [Test]
    public void Drop_down_paints_icon_check_and_radio_marks()
    {
        var strip = CreateStrip(out var canvas, out var backend);
        var root = new ToolStripMenuItem("Menu");
        var withIcon = new ToolStripMenuItem("Iconed") { Image = backend.CreateImage(16, 16, new int[256]) };
        var checkedItem = new ToolStripMenuItem("Checked") { Checked = true };
        var radioItem = new ToolStripMenuItem("Radio") { CheckedGroup = "g", Checked = true };
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add(new ToolStripMenuItem("Child"));
        root.DropDownItems.AddRange(withIcon, checkedItem, radioItem, parent);
        strip.Items.Add(root);
        canvas.RaiseMouseDown(5, 5);
        var popup = PopupOf(backend);

        var g = popup.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(static o => o.StartsWith("image 16x16")), Is.True, "item icon");
            // The check mark of "Checked" (row 2, y = 23..44): two strokes around the column center.
            Assert.That(g.Operations, Does.Contain("line #FF1A1A1A 9,34-12,37"), "check mark first stroke");
            // The radio bullet of "Radio" (row 3): a 6px filled ellipse centered in the icon column.
            Assert.That(g.Operations, Does.Contain("fillellipse #FF1A1A1A 10,53,6,6"), "radio bullet");
            // The submenu arrow of "More" (row 4, popup width 91): the triangle's leftmost stroke.
            Assert.That(g.Operations, Does.Contain("line #FF1A1A1A 79,74-79,80"), "submenu arrow");
        });
    }

    [Test]
    public void Click_commits_the_item_closes_the_cascade_and_raises_Click_once()
    {
        var strip = CreateFileMenu(out var open, out _, out var canvas, out var backend);
        var clicks = 0;
        open.Click += (_, _) => ++clicks;
        canvas.RaiseMouseDown(5, 5);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(30, 10); // the "Open" row

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(popup.IsShown, Is.False);
            Assert.That(strip.OpenIndex, Is.EqualTo(-1));
        });
    }

    [Test]
    public void Disabled_item_does_not_commit()
    {
        var strip = CreateFileMenu(out var open, out _, out var canvas, out var backend);
        open.Enabled = false;
        var clicks = 0;
        open.Click += (_, _) => ++clicks;
        canvas.RaiseMouseDown(5, 5);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(30, 10);

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.Zero);
            Assert.That(popup.IsShown, Is.True, "the menu stays open");
        });
    }

    [Test]
    public void Hovering_a_submenu_item_cascades_a_child_popup_anchored_right()
    {
        var strip = CreateStrip(out var canvas, out var backend);
        var root = new ToolStripMenuItem("&File");
        var recent = new ToolStripMenuItem("Recent");
        recent.DropDownItems.Add(new ToolStripMenuItem("A.txt"));
        root.DropDownItems.Add(recent);
        strip.Items.Add(root);
        canvas.RaiseMouseDown(5, 5);
        var rootPopup = PopupOf(backend);

        rootPopup.RaiseMouseMove(30, 10); // hover "Recent" (first row, top y = 1)

        var child = PopupOf(backend, 1);
        var rootCall = rootPopup.ShowCalls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(child.IsShown, Is.True);
            Assert.That(child.ShowCalls.Single().Location,
                Is.EqualTo(new Point(rootCall.Location.X + rootCall.Size.Width, rootCall.Location.Y + 1)));
            Assert.That(child.RaisePaint().DrewText("A.txt"), Is.True);
        });
    }

    [Test]
    public void Escape_walks_the_cascade_closed_one_level_at_a_time()
    {
        var strip = CreateStrip(out var canvas, out var backend);
        var root = new ToolStripMenuItem("&File");
        var recent = new ToolStripMenuItem("Recent");
        recent.DropDownItems.Add(new ToolStripMenuItem("A.txt"));
        root.DropDownItems.Add(recent);
        strip.Items.Add(root);
        canvas.RaiseMouseDown(5, 5);
        var rootPopup = PopupOf(backend);
        rootPopup.RaiseMouseMove(30, 10);
        var child = PopupOf(backend, 1);

        canvas.RaiseKeyDown(Keys.Escape);
        Assert.Multiple(() =>
        {
            Assert.That(child.IsShown, Is.False, "first Escape closes the submenu only");
            Assert.That(rootPopup.IsShown, Is.True);
            Assert.That(strip.OpenIndex, Is.Zero);
        });

        canvas.RaiseKeyDown(Keys.Escape);
        Assert.Multiple(() =>
        {
            Assert.That(rootPopup.IsShown, Is.False, "second Escape closes the menu");
            Assert.That(strip.OpenIndex, Is.EqualTo(-1));
        });
    }

    [Test]
    public void Light_dismissal_closes_the_cascade_and_resets_the_bar()
    {
        var strip = CreateFileMenu(out _, out _, out var canvas, out var backend);
        canvas.RaiseMouseDown(5, 5);
        var popup = PopupOf(backend);

        popup.FireDismiss();

        Assert.That(strip.OpenIndex, Is.EqualTo(-1));
    }

    // --- Keyboard ---------------------------------------------------------------------------------

    [Test]
    public void Keyboard_opens_navigates_and_commits()
    {
        var strip = CreateFileMenu(out var open, out _, out var canvas, out var backend);
        var clicks = 0;
        open.Click += (_, _) => ++clicks;

        canvas.RaiseKeyDown(Keys.Right); // hover "File"
        canvas.RaiseKeyDown(Keys.Enter); // open its menu
        Assert.That(strip.OpenIndex, Is.Zero);

        canvas.RaiseKeyDown(Keys.Down);  // hover "Open"
        canvas.RaiseKeyDown(Keys.Enter); // commit it

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(strip.OpenIndex, Is.EqualTo(-1));
            Assert.That(PopupOf(backend).IsShown, Is.False);
        });
    }

    [Test]
    public void Keyboard_navigation_skips_separators()
    {
        var strip = CreateFileMenu(out var open, out var exit, out var canvas, out _);
        canvas.RaiseKeyDown(Keys.Right);
        canvas.RaiseKeyDown(Keys.Enter);
        var clicks = 0;
        exit.Click += (_, _) => ++clicks;

        canvas.RaiseKeyDown(Keys.Down); // "Open"
        canvas.RaiseKeyDown(Keys.Down); // skips the separator, lands on "Exit"
        canvas.RaiseKeyDown(Keys.Enter);

        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void Left_and_Right_move_between_top_level_menus_while_open()
    {
        var strip = CreateFileMenu(out _, out _, out var canvas, out var backend);
        canvas.RaiseMouseDown(5, 5);
        Assert.That(strip.OpenIndex, Is.Zero);

        canvas.RaiseKeyDown(Keys.Right);
        Assert.That(strip.OpenIndex, Is.EqualTo(1), "Right moves to Edit");

        canvas.RaiseKeyDown(Keys.Left);
        Assert.That(strip.OpenIndex, Is.Zero, "Left moves back to File");
    }

    [Test]
    public void Mnemonic_letter_opens_the_matching_top_level_menu()
    {
        var strip = CreateFileMenu(out _, out _, out var canvas, out var backend);

        canvas.RaiseKeyPress('e');

        Assert.Multiple(() =>
        {
            Assert.That(strip.OpenIndex, Is.EqualTo(1));
            Assert.That(PopupOf(backend).IsShown, Is.True);
        });
    }

    // --- Shortcuts --------------------------------------------------------------------------------

    [Test]
    public void ProcessShortcut_fires_the_matching_item()
    {
        var strip = CreateFileMenu(out var open, out _, out _, out _);
        var clicks = 0;
        open.Click += (_, _) => ++clicks;

        Assert.Multiple(() =>
        {
            Assert.That(strip.ProcessShortcut(Keys.Control | Keys.O), Is.True);
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(strip.ProcessShortcut(Keys.Control | Keys.Q), Is.False);
        });
    }

    [Test]
    public void ProcessShortcut_skips_disabled_items()
    {
        var strip = CreateFileMenu(out var open, out _, out _, out _);
        open.Enabled = false;
        var clicks = 0;
        open.Click += (_, _) => ++clicks;

        Assert.Multiple(() =>
        {
            Assert.That(strip.ProcessShortcut(Keys.Control | Keys.O), Is.False);
            Assert.That(clicks, Is.Zero);
        });
    }

    [Test]
    public void Shortcut_dispatches_from_the_key_pipeline()
    {
        CreateFileMenu(out var open, out _, out var canvas, out _);
        var clicks = 0;
        open.Click += (_, _) => ++clicks;

        canvas.RaiseKeyDown(Keys.O, KeyModifiers.Control);

        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void Item_width_cache_refreshes_when_an_item_changes()
    {
        var strip = CreateFileMenu(out _, out _, out var canvas, out _);

        canvas.RaiseMouseDown(50, 10); // x=50 is inside "Edit" while "File" is 44px wide
        Assert.That(strip.OpenIndex, Is.EqualTo(1));
        strip.CloseDropDown();

        strip.Items[0].Text = "&Filesystem"; // grows the first item to 86px
        canvas.RaiseMouseDown(50, 10);
        Assert.That(strip.OpenIndex, Is.Zero, "the refreshed width routes x=50 to the first item");
    }
}
