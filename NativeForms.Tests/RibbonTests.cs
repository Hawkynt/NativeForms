using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Ribbon"/> must lay its <see cref="RibbonGroup"/>s out side by side under the tab strip —
/// large items filling the group height, small ones stacked three to a column — collapse groups into
/// drop-down buttons once the width runs out, fold the whole group area away when
/// <see cref="Ribbon.Minimized"/>, and host real controls whose peers follow the selected tab.
/// </summary>
[TestFixture]
internal sealed class RibbonTests
{
    // DefaultTheme row height 22 + 4px tab chrome; RecordingGraphics measures 7px per character.
    private const int _TabStrip = 26;
    private const int _GroupTop = _TabStrip;
    private const int _ContentHeight = 70;  // 94 group area - 16 caption strip - 2*4 padding
    private const int _RowHeight = 23;      // 70 / 3 stacked rows

    private static HeadlessCanvasPeer Realize(Ribbon ribbon, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 800, 400) };
        form.Controls.Add(ribbon);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    /// <summary>A Home tab with a Clipboard group (one large + three small) and a Font group (two small).</summary>
    private static Ribbon HomeAndInsert(out RibbonGroup clipboard, out RibbonGroup font, out RibbonTab insert)
    {
        var ribbon = new Ribbon { Bounds = new(0, 0, 600, 120) };
        var home = new RibbonTab("Home");
        clipboard = new RibbonGroup("Clipboard");
        clipboard.Items.AddRange(
            new RibbonButton("Paste"),
            new RibbonButton("Cut", RibbonItemSize.Small),
            new RibbonButton("Copy", RibbonItemSize.Small),
            new RibbonButton("Format", RibbonItemSize.Small));
        font = new RibbonGroup("Font");
        font.Items.AddRange(
            new RibbonToggleButton("Bold", RibbonItemSize.Small),
            new RibbonToggleButton("Italic", RibbonItemSize.Small));
        home.Groups.AddRange(clipboard, font);

        insert = new RibbonTab("Insert");
        var tables = new RibbonGroup("Tables");
        tables.Items.Add(new RibbonButton("Table"));
        insert.Groups.Add(tables);

        ribbon.Tabs.AddRange(home, insert);
        return ribbon;
    }

    [Test]
    public void The_first_tab_added_becomes_the_selected_one()
    {
        var ribbon = HomeAndInsert(out _, out _, out _);
        Realize(ribbon, out _);

        Assert.Multiple(() =>
        {
            Assert.That(ribbon.SelectedIndex, Is.Zero);
            Assert.That(ribbon.SelectedTab!.Text, Is.EqualTo("Home"));
            Assert.That(ribbon.TabStripHeight, Is.EqualTo(_TabStrip));
        });
    }

    [Test]
    public void Groups_are_laid_out_side_by_side_under_the_tab_strip()
    {
        var ribbon = HomeAndInsert(out var clipboard, out var font, out _);
        Realize(ribbon, out _);

        // Clipboard: a 44px large column plus a 70px small column = 114 content + 8 padding.
        // Font: a single 70px small column + 8 padding = 78, starting after a 2px gap.
        Assert.Multiple(() =>
        {
            Assert.That(clipboard.Bounds, Is.EqualTo(new Rectangle(0, _GroupTop, 122, 94)));
            Assert.That(font.Bounds, Is.EqualTo(new Rectangle(124, _GroupTop, 78, 94)));
            Assert.That(clipboard.IsCollapsed, Is.False);
            Assert.That(font.IsCollapsed, Is.False);
        });
    }

    [Test]
    public void A_large_item_fills_the_group_height_and_small_items_stack_three_per_column()
    {
        var ribbon = HomeAndInsert(out var clipboard, out _, out _);
        var canvas = Realize(ribbon, out _);
        var clicked = new List<string>();
        for (var i = 0; i < clipboard.Items.Count; ++i)
        {
            var item = clipboard.Items[i];
            item.Click += (_, _) => clicked.Add(item.Text);
        }

        // The large column spans x 4..48 over the full 70px content height.
        canvas.RaiseMouseDown(20, _GroupTop + 4 + (_ContentHeight / 2));
        canvas.RaiseMouseUp(20, _GroupTop + 4 + (_ContentHeight / 2));

        // The small column starts at x 48; its three rows are 23px each from y 30.
        canvas.RaiseMouseDown(60, _GroupTop + 4 + (_RowHeight / 2));
        canvas.RaiseMouseUp(60, _GroupTop + 4 + (_RowHeight / 2));
        canvas.RaiseMouseDown(60, _GroupTop + 4 + _RowHeight + (_RowHeight / 2));
        canvas.RaiseMouseUp(60, _GroupTop + 4 + _RowHeight + (_RowHeight / 2));
        canvas.RaiseMouseDown(60, _GroupTop + 4 + (2 * _RowHeight) + (_RowHeight / 2));
        canvas.RaiseMouseUp(60, _GroupTop + 4 + (2 * _RowHeight) + (_RowHeight / 2));

        Assert.That(clicked, Is.EqualTo(new[] { "Paste", "Cut", "Copy", "Format" }));
    }

    [Test]
    public void A_ribbon_button_runs_its_command()
    {
        var ribbon = HomeAndInsert(out var clipboard, out _, out _);
        var canvas = Realize(ribbon, out _);
        var runs = 0;
        clipboard.Items[0].Command = new RelayCommand(() => ++runs);

        canvas.RaiseMouseDown(20, _GroupTop + 4 + (_ContentHeight / 2));
        canvas.RaiseMouseUp(20, _GroupTop + 4 + (_ContentHeight / 2));

        Assert.That(runs, Is.EqualTo(1));
    }

    [Test]
    public void A_command_that_cannot_execute_greys_its_item_out_and_swallows_the_click()
    {
        var ribbon = HomeAndInsert(out var clipboard, out _, out _);
        var canvas = Realize(ribbon, out _);
        var runs = 0;
        clipboard.Items[0].Command = new RelayCommand(() => ++runs, () => false);

        canvas.RaiseMouseDown(20, _GroupTop + 4 + (_ContentHeight / 2));
        canvas.RaiseMouseUp(20, _GroupTop + 4 + (_ContentHeight / 2));

        Assert.Multiple(() =>
        {
            Assert.That(clipboard.Items[0].Enabled, Is.False);
            Assert.That(runs, Is.Zero);
        });
    }

    [Test]
    public void A_toggle_button_latches_and_reports_the_change()
    {
        var ribbon = HomeAndInsert(out _, out var font, out _);
        var canvas = Realize(ribbon, out _);
        var bold = (RibbonToggleButton)font.Items[0];
        var changes = 0;
        bold.CheckedChanged += (_, _) => ++changes;

        // Font's first small row sits at x 124..202, y 30..53.
        canvas.RaiseMouseDown(150, _GroupTop + 4 + (_RowHeight / 2));
        canvas.RaiseMouseUp(150, _GroupTop + 4 + (_RowHeight / 2));

        Assert.Multiple(() =>
        {
            Assert.That(bold.Checked, Is.True);
            Assert.That(changes, Is.EqualTo(1));
        });

        canvas.RaiseMouseDown(150, _GroupTop + 4 + (_RowHeight / 2));
        canvas.RaiseMouseUp(150, _GroupTop + 4 + (_RowHeight / 2));

        Assert.That(bold.Checked, Is.False);
    }

    [Test]
    public void Clicking_a_tab_header_switches_tabs_and_raises_the_event_once()
    {
        var ribbon = HomeAndInsert(out _, out _, out var insert);
        var canvas = Realize(ribbon, out _);
        var changes = 0;
        ribbon.SelectedIndexChanged += (_, _) => ++changes;

        // "Home" is 4 chars → 28 + 24 padding = 52px wide, so x = 60 lands on "Insert".
        canvas.RaiseMouseDown(60, _TabStrip / 2);

        Assert.Multiple(() =>
        {
            Assert.That(ribbon.SelectedIndex, Is.EqualTo(1));
            Assert.That(ribbon.SelectedTab, Is.SameAs(insert));
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void Arrow_keys_and_control_tab_switch_tabs()
    {
        var ribbon = HomeAndInsert(out _, out _, out _);
        var canvas = Realize(ribbon, out _);

        canvas.RaiseKeyDown(Keys.Right);
        Assert.That(ribbon.SelectedIndex, Is.EqualTo(1));

        canvas.RaiseKeyDown(Keys.Left);
        Assert.That(ribbon.SelectedIndex, Is.Zero);

        canvas.RaiseKeyDown(Keys.Tab, KeyModifiers.Control);
        Assert.That(ribbon.SelectedIndex, Is.EqualTo(1), "Ctrl+Tab wraps forward");

        canvas.RaiseKeyDown(Keys.Tab, KeyModifiers.Control);
        Assert.That(ribbon.SelectedIndex, Is.Zero, "and wraps around the end");
    }

    [Test]
    public void A_group_that_no_longer_fits_collapses_into_its_drop_down_button()
    {
        var ribbon = HomeAndInsert(out var clipboard, out var font, out _);
        Realize(ribbon, out _);

        // 122 + 2 + 78 + 2 = 204px of groups; 200 forces exactly the rightmost one to fold.
        ribbon.Width = 200;

        Assert.Multiple(() =>
        {
            Assert.That(font.IsCollapsed, Is.True, "Office folds the rightmost group first");
            Assert.That(font.Bounds.Width, Is.EqualTo(68));
            Assert.That(clipboard.IsCollapsed, Is.False, "the leading group still fits");
        });
    }

    [Test]
    public void Widening_the_ribbon_again_unfolds_the_collapsed_group()
    {
        var ribbon = HomeAndInsert(out _, out var font, out _);
        Realize(ribbon, out _);
        ribbon.Width = 200;
        Assert.That(font.IsCollapsed, Is.True);

        ribbon.Width = 600;

        Assert.Multiple(() =>
        {
            Assert.That(font.IsCollapsed, Is.False);
            Assert.That(font.Bounds.Width, Is.EqualTo(78));
        });
    }

    [Test]
    public void Minimizing_folds_the_group_area_away_and_leaves_the_tabs()
    {
        var ribbon = HomeAndInsert(out _, out _, out _);
        var canvas = Realize(ribbon, out _);
        var changes = 0;
        ribbon.MinimizedChanged += (_, _) => ++changes;

        ribbon.Minimized = true;

        Assert.Multiple(() =>
        {
            Assert.That(ribbon.GroupAreaHeight, Is.Zero);
            Assert.That(changes, Is.EqualTo(1));
        });

        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Home"), Is.True, "the tab strip survives");
            Assert.That(g.DrewText("Paste"), Is.False, "the group area does not paint while minimized");
        });
    }

    [Test]
    public void A_hosted_control_is_parented_positioned_and_shown_with_its_tab()
    {
        var ribbon = new Ribbon { Bounds = new(0, 0, 600, 120) };
        var combo = new ComboBox();
        var home = new RibbonTab("Home");
        var group = new RibbonGroup("Styles");
        group.Items.Add(new RibbonHostItem(combo) { HostWidth = 120 });
        home.Groups.Add(group);
        var other = new RibbonTab("Insert");
        other.Groups.Add(new RibbonGroup("Tables"));
        ribbon.Tabs.AddRange(home, other);

        Realize(ribbon, out _);

        Assert.Multiple(() =>
        {
            Assert.That(combo.Parent, Is.SameAs(ribbon), "the ribbon adopts the hosted control");
            Assert.That(combo.Bounds.Width, Is.EqualTo(120));
            Assert.That(combo.Bounds.Y, Is.EqualTo(_GroupTop + 4));
            Assert.That(combo.Visible, Is.True);
        });

        ribbon.SelectedIndex = 1;
        Assert.That(combo.Visible, Is.False, "its tab is no longer selected");

        ribbon.SelectedIndex = 0;
        Assert.That(combo.Visible, Is.True, "and it comes back with its tab");
    }

    [Test]
    public void Minimizing_takes_the_hosted_controls_with_it()
    {
        var ribbon = new Ribbon { Bounds = new(0, 0, 600, 120) };
        var combo = new ComboBox();
        var home = new RibbonTab("Home");
        var group = new RibbonGroup("Styles");
        group.Items.Add(new RibbonHostItem(combo) { HostWidth = 120 });
        home.Groups.Add(group);
        ribbon.Tabs.Add(home);
        Realize(ribbon, out _);

        ribbon.Minimized = true;
        Assert.That(combo.Visible, Is.False);

        ribbon.Minimized = false;
        Assert.That(combo.Visible, Is.True);
    }

    [Test]
    public void A_resize_that_collapses_a_group_takes_its_hosted_control_off_screen()
    {
        // The regression this guards was invisible to a property assertion: Visible asks the veto
        // live and so reported false the moment the group collapsed, while the peer was never told
        // and the widget stayed painted at its old place, spilling outside the narrowed ribbon. The
        // peer's own flag is the only witness that agrees with the pixels.
        var ribbon = new Ribbon { Bounds = new(0, 0, 600, 120) };
        var combo = new ComboBox();
        var home = new RibbonTab("Home");
        var clipboard = new RibbonGroup("Clipboard");
        clipboard.Items.AddRange(
            new RibbonButton("Paste"),
            new RibbonButton("Cut", RibbonItemSize.Small),
            new RibbonButton("Copy", RibbonItemSize.Small),
            new RibbonButton("Format", RibbonItemSize.Small));
        var styles = new RibbonGroup("Styles");
        styles.Items.Add(new RibbonHostItem(combo) { HostWidth = 120 });
        home.Groups.AddRange(clipboard, styles);
        ribbon.Tabs.Add(home);

        Realize(ribbon, out var backend);
        var comboPeer = backend.Created.OfType<HeadlessCanvasPeer>().Last();
        Assert.That(comboPeer.Visible, Is.True, "the hosted combo starts on screen");

        ribbon.Width = 200; // forces the Styles group to fold into its drop-down button

        Assert.Multiple(() =>
        {
            Assert.That(styles.IsCollapsed, Is.True);
            Assert.That(comboPeer.Visible, Is.False, "the peer, not just the property, must go down");
            Assert.That(combo.Visible, Is.False);
        });

        ribbon.Width = 600;

        Assert.Multiple(() =>
        {
            Assert.That(styles.IsCollapsed, Is.False);
            Assert.That(comboPeer.Visible, Is.True, "and must come back with the width");
        });
    }

    [Test]
    public void Item_width_cache_refreshes_when_a_caption_changes()
    {
        var ribbon = HomeAndInsert(out var clipboard, out _, out _);
        var canvas = Realize(ribbon, out _);
        var clicked = new List<string>();
        for (var i = 0; i < clipboard.Items.Count; ++i)
        {
            var item = clipboard.Items[i];
            item.Click += (_, _) => clicked.Add(item.Text);
        }

        // The large "Paste" column is 44px wide, so x = 50 lands in the small column beside it.
        canvas.RaiseMouseDown(50, _GroupTop + 4 + (_RowHeight / 2));
        canvas.RaiseMouseUp(50, _GroupTop + 4 + (_RowHeight / 2));
        Assert.That(clicked, Is.EqualTo(new[] { "Cut" }));

        // Widening the large caption to 12 chars pushes that column out to 92px, so the same x now
        // falls inside the large item — which only happens if the cached width was dropped.
        clipboard.Items[0].Text = "Paste Special";
        canvas.RaiseMouseDown(50, _GroupTop + 4 + (_RowHeight / 2));
        canvas.RaiseMouseUp(50, _GroupTop + 4 + (_RowHeight / 2));

        Assert.That(clicked, Is.EqualTo(new[] { "Cut", "Paste Special" }));
    }

    [Test]
    public void Groups_and_items_paint_their_captions()
    {
        var ribbon = HomeAndInsert(out _, out _, out _);
        var canvas = Realize(ribbon, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Home"), Is.True);
            Assert.That(g.DrewText("Insert"), Is.True);
            Assert.That(g.DrewText("Clipboard"), Is.True, "the group caption strip");
            Assert.That(g.DrewText("Paste"), Is.True);
            Assert.That(g.DrewText("Format"), Is.True);
            Assert.That(g.DrewText("Table"), Is.False, "the unselected tab's items stay unpainted");
        });
    }

    [Test]
    public void Minimizing_shrinks_the_control_to_the_tab_strip_and_restores_it()
    {
        var ribbon = HomeAndInsert(out _, out _, out _);
        Realize(ribbon, out _);
        var heights = new List<int>();
        ribbon.PreferredHeightChanged += (_, _) => heights.Add(ribbon.PreferredHeight);

        ribbon.Minimized = true;
        Assert.Multiple(() =>
        {
            Assert.That(ribbon.Height, Is.EqualTo(_TabStrip), "the control collapses onto its strip");
            Assert.That(ribbon.PreferredHeight, Is.EqualTo(_TabStrip));
        });

        ribbon.Minimized = false;
        Assert.Multiple(() =>
        {
            Assert.That(ribbon.Height, Is.EqualTo(120), "and grows back to the height it was minimized from");
            Assert.That(ribbon.PreferredHeight, Is.EqualTo(120));
            Assert.That(heights, Is.EqualTo(new[] { _TabStrip, 120 }), "each toggle announces the new preferred height");
        });
    }

    [Test]
    public void Double_clicking_a_tab_toggles_minimized()
    {
        var ribbon = HomeAndInsert(out _, out _, out _);
        var canvas = Realize(ribbon, out _);

        // Two quick presses on the same tab header read as a double-click.
        canvas.RaiseMouseDown(20, _TabStrip / 2);
        canvas.RaiseMouseDown(20, _TabStrip / 2);

        Assert.Multiple(() =>
        {
            Assert.That(ribbon.Minimized, Is.True);
            Assert.That(ribbon.Height, Is.EqualTo(_TabStrip));
        });
    }

    [Test]
    public void Double_clicking_a_tab_while_minimized_restores_it()
    {
        var ribbon = HomeAndInsert(out _, out _, out _);
        var canvas = Realize(ribbon, out _);
        ribbon.Minimized = true;

        canvas.RaiseMouseDown(20, _TabStrip / 2);
        canvas.RaiseMouseDown(20, _TabStrip / 2);

        Assert.Multiple(() =>
        {
            Assert.That(ribbon.Minimized, Is.False);
            Assert.That(ribbon.Height, Is.EqualTo(120));
        });
    }

    [Test]
    public void A_tab_click_while_minimized_opens_a_flyout_under_the_strip()
    {
        var ribbon = HomeAndInsert(out _, out _, out _);
        var canvas = Realize(ribbon, out var backend);
        ribbon.Minimized = true;

        // A single press on the Insert header selects it and floats its groups under the strip.
        canvas.RaiseMouseDown(60, _TabStrip / 2);

        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(ribbon.SelectedIndex, Is.EqualTo(1));
            Assert.That(popup.IsShown, Is.True);
            Assert.That(popup.ShowCalls.Single().Location, Is.EqualTo(new Point(0, _TabStrip)), "anchored right under the strip");
            Assert.That(popup.ShowCalls.Single().Size, Is.EqualTo(new Size(600, 120 - _TabStrip)), "full width, a whole group area tall");
        });
    }

    [Test]
    public void Activating_an_item_in_the_flyout_fires_it_and_closes_the_flyout()
    {
        var ribbon = HomeAndInsert(out _, out _, out var insert);
        var canvas = Realize(ribbon, out var backend);
        var fired = 0;
        insert.Groups[0].Items[0].Click += (_, _) => ++fired;

        ribbon.Minimized = true;
        canvas.RaiseMouseDown(60, _TabStrip / 2); // select Insert, open its flyout
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();

        // The large "Table" item fills the first column of the only group.
        popup.RaiseMouseDown(20, _ContentHeight / 2);

        Assert.Multiple(() =>
        {
            Assert.That(fired, Is.EqualTo(1), "the flyout item runs");
            Assert.That(popup.IsShown, Is.False, "and the flyout closes behind it");
        });
    }

    [Test]
    public void A_dismissed_flyout_closes_without_firing_anything()
    {
        var ribbon = HomeAndInsert(out _, out _, out _);
        var canvas = Realize(ribbon, out var backend);
        ribbon.Minimized = true;
        canvas.RaiseMouseDown(60, _TabStrip / 2);
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();

        popup.FireDismiss();

        Assert.That(popup.IsShown, Is.False);
    }

    [Test]
    public void A_ribbon_grid_button_opens_a_picker_and_reports_the_chosen_size()
    {
        var ribbon = new Ribbon { Bounds = new(0, 0, 600, 120) };
        var home = new RibbonTab("Home");
        var group = new RibbonGroup("Tables");
        var table = new RibbonGridButton("Table") { MaxColumns = 10, MaxRows = 8 };
        group.Items.Add(table);
        home.Groups.Add(group);
        ribbon.Tabs.Add(home);
        var canvas = Realize(ribbon, out var backend);
        var committed = default((int Rows, int Columns)?);
        table.RangeSelected += (_, e) => committed = (e.Rows, e.Columns);

        // Click the large Table button; a grid popup opens under its group.
        canvas.RaiseMouseDown(20, _GroupTop + 4 + (_ContentHeight / 2));
        canvas.RaiseMouseUp(20, _GroupTop + 4 + (_ContentHeight / 2));
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        Assert.That(popup.IsShown, Is.True);

        // Grid cell (column 2, row 1): centres at 6 + (n-1)*18 + 9.
        popup.RaiseMouseDown(6 + 18 + 9, 6 + 9);

        Assert.That(committed, Is.EqualTo((1, 2)));
    }

    [Test]
    public void Removing_a_tab_drops_its_hosted_controls_and_moves_the_selection()
    {
        var ribbon = HomeAndInsert(out _, out _, out var insert);
        Realize(ribbon, out _);
        ribbon.SelectedIndex = 1;

        ribbon.Tabs.Remove(insert);

        Assert.Multiple(() =>
        {
            Assert.That(ribbon.Tabs, Has.Count.EqualTo(1));
            Assert.That(ribbon.SelectedIndex, Is.Zero);
        });
    }

    // --- Quick Access Toolbar ----------------------------------------------------------------------

    [Test]
    public void Clicking_a_quick_access_button_at_the_right_of_the_strip_runs_it()
    {
        var ribbon = new Ribbon { Bounds = new(0, 0, 600, 120) };
        ribbon.Tabs.Add(new RibbonTab("Home"));
        var save = new RibbonButton("Save");
        var undo = new RibbonButton("Undo");
        var saves = 0;
        var undos = 0;
        save.Click += (_, _) => ++saves;
        undo.Click += (_, _) => ++undos;
        ribbon.QuickAccessItems.AddRange(save, undo);
        var canvas = Realize(ribbon, out _);
        canvas.RaisePaint();

        // Two 22 px buttons right-aligned in a 600 px strip: Save at [556,578), Undo at [578,600).
        canvas.RaiseMouseDown(567, _TabStrip / 2);
        canvas.RaiseMouseDown(589, _TabStrip / 2);

        Assert.Multiple(() =>
        {
            Assert.That(saves, Is.EqualTo(1));
            Assert.That(undos, Is.EqualTo(1));
        });
    }

    [Test]
    public void A_quick_access_button_paints_its_icon()
    {
        var ribbon = new Ribbon { Bounds = new(0, 0, 600, 120) };
        ribbon.Tabs.Add(new RibbonTab("Home"));
        using var images = new ImageList(16);
        var save = new RibbonButton("Save") { ImageList = images, ImageIndex = images.Add(new int[256]) };
        ribbon.QuickAccessItems.Add(save);
        var canvas = Realize(ribbon, out _);

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("image 16x16")), Is.True);
    }

    [Test]
    public void A_disabled_quick_access_button_ignores_a_click()
    {
        var ribbon = new Ribbon { Bounds = new(0, 0, 600, 120) };
        ribbon.Tabs.Add(new RibbonTab("Home"));
        var save = new RibbonButton("Save") { Enabled = false };
        var clicks = 0;
        save.Click += (_, _) => ++clicks;
        ribbon.QuickAccessItems.Add(save);
        var canvas = Realize(ribbon, out _);
        canvas.RaisePaint();

        canvas.RaiseMouseDown(600 - 11, _TabStrip / 2); // the single button, right-aligned

        Assert.That(clicks, Is.Zero);
    }
}
