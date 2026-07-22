using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The §7.1 focus/keyboard model: TabIndex/TabStop tab-order traversal, Focus()/Focused/ActiveControl
/// round trips over the headless focus simulation, the WinForms event order (Enter → GotFocus on the
/// gaining control, LostFocus → Leave on the losing one, container crossings in between), and the
/// form's dialog-key chain — Tab navigation, Enter/Escape to AcceptButton/CancelButton, form-wide
/// menu shortcuts and Alt+mnemonics.
/// </summary>
[TestFixture]
internal sealed class FocusModelTests
{
    /// <summary>Realizes a form on a fresh headless backend.</summary>
    private static HeadlessBackend Realize(Form form)
    {
        var backend = new HeadlessBackend();
        Application.Run(form, backend);
        return backend;
    }

    /// <summary>The canvas peer of a realized owner-drawn control — the keyboard entry point.</summary>
    private static HeadlessCanvasPeer CanvasOf(Control control) => (HeadlessCanvasPeer)control.Peer!;

    // --- TabStop defaults -------------------------------------------------------------------------

    [Test]
    public void TabStop_defaults_follow_the_control_kind()
    {
        Assert.Multiple(() =>
        {
            // Interactive kinds are tab stops.
            Assert.That(new Button().TabStop, Is.True);
            Assert.That(new TextBox().TabStop, Is.True);
            Assert.That(new RichTextBox().TabStop, Is.True);
            Assert.That(new CheckBox().TabStop, Is.True);
            Assert.That(new RadioButton().TabStop, Is.True);
            Assert.That(new LinkLabel().TabStop, Is.True);
            Assert.That(new ComboBox().TabStop, Is.True);
            Assert.That(new ListBox().TabStop, Is.True);
            Assert.That(new TabControl().TabStop, Is.True);
            Assert.That(new NumericUpDown().TabStop, Is.True);
            Assert.That(new DataGridView().TabStop, Is.True);

            // Static and container kinds are not.
            Assert.That(new Label().TabStop, Is.False);
            Assert.That(new Panel().TabStop, Is.False);
            Assert.That(new GroupBox().TabStop, Is.False);
            Assert.That(new PictureBox().TabStop, Is.False);
            Assert.That(new ProgressBar().TabStop, Is.False);
            Assert.That(new VScrollBar().TabStop, Is.False);
            Assert.That(new ToolStrip().TabStop, Is.False);
            Assert.That(new StatusStrip().TabStop, Is.False);

            // The menu bar is focusable (Alt reaches it) but not a tab stop, matching WinForms.
            Assert.That(new MenuStrip().TabStop, Is.False);
        });
    }

    [Test]
    public void TabStop_assignment_overrides_the_default()
    {
        var label = new Label { TabStop = true };
        var button = new Button { TabStop = false };

        Assert.Multiple(() =>
        {
            Assert.That(label.TabStop, Is.True);
            Assert.That(button.TabStop, Is.False);
        });
    }

    // --- Focus(), Focused, CanFocus, ActiveControl ------------------------------------------------

    [Test]
    public void CanFocus_requires_realization_visibility_enabled_and_a_focusable_kind()
    {
        var button = new Button();
        Assert.That(button.CanFocus, Is.False, "unrealized");

        var form = new Form();
        var label = new Label();
        form.Controls.Add(button);
        form.Controls.Add(label);
        Realize(form);

        Assert.Multiple(() =>
        {
            Assert.That(button.CanFocus, Is.True);
            Assert.That(label.CanFocus, Is.False, "labels never take focus");
        });

        button.Enabled = false;
        Assert.That(button.CanFocus, Is.False, "disabled");

        button.Enabled = true;
        button.Visible = false;
        Assert.That(button.CanFocus, Is.False, "invisible");
    }

    [Test]
    public void Focus_routes_to_the_peer_and_Focused_tracks_the_peer_events()
    {
        var form = new Form();
        var first = new CheckBox();
        var second = new TextBox();
        form.Controls.Add(first);
        form.Controls.Add(second);
        var backend = Realize(form);

        second.Focus();
        Assert.Multiple(() =>
        {
            Assert.That(second.Focused, Is.True);
            Assert.That(first.Focused, Is.False);
            Assert.That(((HeadlessPeer)second.Peer!).FocusRequested, Is.True);
            Assert.That(backend.FocusedPeer, Is.SameAs(second.Peer));
            Assert.That(form.ActiveControl, Is.SameAs(second));
        });

        first.Focus();
        Assert.Multiple(() =>
        {
            Assert.That(first.Focused, Is.True);
            Assert.That(second.Focused, Is.False);
            Assert.That(form.ActiveControl, Is.SameAs(first));
        });
    }

    [Test]
    public void Hiding_the_focused_control_surrenders_focus_to_the_next_tab_stop()
    {
        var form = new Form();
        var first = new TextBox();
        var second = new TextBox();
        form.Controls.Add(first);
        form.Controls.Add(second);
        Realize(form);

        first.Focus();
        Assert.That(form.ActiveControl, Is.SameAs(first));

        first.Visible = false;

        Assert.Multiple(() =>
        {
            Assert.That(first.Focused, Is.False, "the hidden control must not keep focus");
            Assert.That(second.Focused, Is.True, "focus moves to the next focusable tab stop");
            Assert.That(form.ActiveControl, Is.SameAs(second));
        });
    }

    [Test]
    public void Hiding_the_page_that_holds_focus_surrenders_it_outside_the_page()
    {
        var form = new Form();
        var outside = new TextBox();
        var tabs = new TabControl();
        var pageA = new TabPage();
        var pageB = new TabPage();
        var onA = new TextBox();
        pageA.Controls.Add(onA);
        tabs.TabPages.Add(pageA);
        tabs.TabPages.Add(pageB);
        form.Controls.Add(outside);
        form.Controls.Add(tabs);
        Realize(form);

        onA.Focus();
        Assert.That(form.ActiveControl, Is.SameAs(onA));

        tabs.SelectedIndex = 1; // hides page A, and the focused control with it

        Assert.Multiple(() =>
        {
            Assert.That(onA.Focused, Is.False, "a control on a switched-away page must not keep focus");
            Assert.That(form.ActiveControl, Is.Not.SameAs(onA));
            Assert.That(form.ActiveControl, Is.Not.Null, "focus lands on a still-visible tab stop");
        });
    }

    [Test]
    public void Hiding_the_only_focusable_control_drops_the_active_record()
    {
        var form = new Form();
        var only = new TextBox();
        form.Controls.Add(only);
        Realize(form);

        only.Focus();
        Assert.That(form.ActiveControl, Is.SameAs(only));

        only.Visible = false;

        Assert.Multiple(() =>
        {
            Assert.That(only.Focused, Is.False);
            Assert.That(form.ActiveControl, Is.Null, "nothing else can hold focus, so the record clears");
        });
    }

    [Test]
    public void Setting_ActiveControl_focuses_the_control()
    {
        var form = new Form();
        var first = new CheckBox();
        var second = new CheckBox();
        form.Controls.Add(first);
        form.Controls.Add(second);
        Realize(form);

        form.ActiveControl = second;

        Assert.That(second.Focused, Is.True);
    }

    // --- Event order ------------------------------------------------------------------------------

    [Test]
    public void Focus_change_raises_the_events_in_WinForms_order()
    {
        var form = new Form();
        var first = new CheckBox();
        var second = new CheckBox();
        form.Controls.Add(first);
        form.Controls.Add(second);
        Realize(form); // initial focus lands on `first`

        var log = new List<string>();
        first.LostFocus += (_, _) => log.Add("first.LostFocus");
        first.Leave += (_, _) => log.Add("first.Leave");
        second.Enter += (_, _) => log.Add("second.Enter");
        second.GotFocus += (_, _) => log.Add("second.GotFocus");

        second.Focus();

        Assert.That(log, Is.EqualTo(new[] { "first.LostFocus", "first.Leave", "second.Enter", "second.GotFocus" }));
    }

    [Test]
    public void Enter_and_Leave_fire_on_containers_crossed_by_the_focus_change()
    {
        var form = new Form();
        var outside = new CheckBox();
        var panel = new Panel { Bounds = new(0, 30, 200, 100) };
        var inside = new CheckBox();
        panel.Controls.Add(inside);
        form.Controls.Add(outside);
        form.Controls.Add(panel);
        Realize(form); // initial focus lands on `outside`

        var log = new List<string>();
        panel.Enter += (_, _) => log.Add("panel.Enter");
        panel.Leave += (_, _) => log.Add("panel.Leave");
        inside.Enter += (_, _) => log.Add("inside.Enter");
        inside.Leave += (_, _) => log.Add("inside.Leave");

        inside.Focus();
        Assert.That(log, Is.EqualTo(new[] { "panel.Enter", "inside.Enter" }), "crossing into the panel");

        log.Clear();
        outside.Focus();
        Assert.That(log, Is.EqualTo(new[] { "inside.Leave", "panel.Leave" }), "crossing out of the panel");
    }

    [Test]
    public void Focus_moves_within_one_container_leave_the_container_alone()
    {
        var form = new Form();
        var panel = new Panel();
        var first = new CheckBox();
        var second = new CheckBox();
        panel.Controls.Add(first);
        panel.Controls.Add(second);
        form.Controls.Add(panel);
        Realize(form);

        var crossings = 0;
        panel.Enter += (_, _) => ++crossings;
        panel.Leave += (_, _) => ++crossings;

        second.Focus();
        first.Focus();

        Assert.That(crossings, Is.Zero);
    }

    // --- Initial focus ----------------------------------------------------------------------------

    [Test]
    public void Show_focuses_the_first_control_in_tab_order()
    {
        var form = new Form();
        var label = new Label { Text = "Caption" };
        var box = new CheckBox();
        form.Controls.Add(label);
        form.Controls.Add(box);

        Realize(form);

        Assert.Multiple(() =>
        {
            Assert.That(box.Focused, Is.True);
            Assert.That(form.ActiveControl, Is.SameAs(box));
        });
    }

    [Test]
    public void ActiveControl_assigned_before_show_becomes_the_initial_focus()
    {
        var form = new Form();
        var first = new CheckBox();
        var second = new CheckBox();
        form.Controls.Add(first);
        form.Controls.Add(second);
        form.ActiveControl = second;

        Realize(form);

        Assert.That(second.Focused, Is.True);
    }

    // --- Tab traversal ----------------------------------------------------------------------------

    [Test]
    public void Tab_follows_TabIndex_order_and_wraps()
    {
        var form = new Form();
        var third = new CheckBox { TabIndex = 2 };
        var first = new CheckBox { TabIndex = 0 };
        var second = new CheckBox { TabIndex = 1 };
        form.Controls.Add(third);
        form.Controls.Add(first);
        form.Controls.Add(second);
        Realize(form);
        Assert.That(first.Focused, Is.True, "initial focus honors TabIndex, not insertion order");

        CanvasOf(first).RaiseKeyDown(Keys.Tab);
        Assert.That(second.Focused, Is.True);

        CanvasOf(second).RaiseKeyDown(Keys.Tab);
        Assert.That(third.Focused, Is.True);

        CanvasOf(third).RaiseKeyDown(Keys.Tab);
        Assert.That(first.Focused, Is.True, "wraps to the first stop");
    }

    [Test]
    public void Shift_Tab_walks_backwards_and_wraps()
    {
        var form = new Form();
        var first = new CheckBox { TabIndex = 0 };
        var second = new CheckBox { TabIndex = 1 };
        form.Controls.Add(first);
        form.Controls.Add(second);
        Realize(form);

        CanvasOf(first).RaiseKeyDown(Keys.Tab, KeyModifiers.Shift);
        Assert.That(second.Focused, Is.True, "backwards from the first stop wraps to the last");

        CanvasOf(second).RaiseKeyDown(Keys.Tab, KeyModifiers.Shift);
        Assert.That(first.Focused, Is.True);
    }

    [Test]
    public void Tab_descends_into_nested_containers_depth_first()
    {
        var form = new Form();
        var first = new CheckBox { TabIndex = 0 };
        var panel = new Panel { TabIndex = 1 };
        var insideFirst = new CheckBox { TabIndex = 0 };
        var insideSecond = new CheckBox { TabIndex = 1 };
        var last = new CheckBox { TabIndex = 2 };
        panel.Controls.Add(insideFirst);
        panel.Controls.Add(insideSecond);
        form.Controls.Add(first);
        form.Controls.Add(panel);
        form.Controls.Add(last);
        Realize(form);

        CanvasOf(first).RaiseKeyDown(Keys.Tab);
        Assert.That(insideFirst.Focused, Is.True);

        CanvasOf(insideFirst).RaiseKeyDown(Keys.Tab);
        Assert.That(insideSecond.Focused, Is.True);

        CanvasOf(insideSecond).RaiseKeyDown(Keys.Tab);
        Assert.That(last.Focused, Is.True);
    }

    [Test]
    public void Tab_skips_non_stops_disabled_and_invisible_controls()
    {
        var form = new Form();
        var first = new CheckBox { TabIndex = 0 };
        var optedOut = new CheckBox { TabIndex = 1, TabStop = false };
        var disabled = new CheckBox { TabIndex = 2, Enabled = false };
        var hidden = new CheckBox { TabIndex = 3, Visible = false };
        var hiddenPanel = new Panel { TabIndex = 4, Visible = false };
        var insideHidden = new CheckBox();
        hiddenPanel.Controls.Add(insideHidden);
        var reachable = new CheckBox { TabIndex = 5 };
        form.Controls.Add(first);
        form.Controls.Add(optedOut);
        form.Controls.Add(disabled);
        form.Controls.Add(hidden);
        form.Controls.Add(hiddenPanel);
        form.Controls.Add(reachable);
        Realize(form);

        CanvasOf(first).RaiseKeyDown(Keys.Tab);

        Assert.Multiple(() =>
        {
            Assert.That(reachable.Focused, Is.True);
            Assert.That(optedOut.Focused, Is.False);
            Assert.That(disabled.Focused, Is.False);
            Assert.That(hidden.Focused, Is.False);
            Assert.That(insideHidden.Focused, Is.False, "children of a hidden container are skipped wholesale");
        });
    }

    // --- Enter/Escape routing ---------------------------------------------------------------------

    [Test]
    public void The_AcceptButton_is_marked_default_on_its_peer_and_the_previous_one_is_cleared()
    {
        var form = new Form();
        var ok = new Button { Text = "OK" };
        var apply = new Button { Text = "Apply" };
        form.Controls.Add(ok);
        form.Controls.Add(apply);
        Realize(form);

        form.AcceptButton = ok;
        Assert.That(((HeadlessButtonPeer)ok.Peer!).IsDefault, Is.True);

        form.AcceptButton = apply;
        Assert.Multiple(() =>
        {
            Assert.That(((HeadlessButtonPeer)ok.Peer!).IsDefault, Is.False, "the previous default is cleared");
            Assert.That(((HeadlessButtonPeer)apply.Peer!).IsDefault, Is.True);
        });
    }

    [Test]
    public void An_AcceptButton_assigned_before_realization_is_default_afterwards()
    {
        var form = new Form();
        var ok = new Button { Text = "OK" };
        form.Controls.Add(ok);
        form.AcceptButton = ok; // buffered before the peer exists
        Realize(form);

        Assert.That(((HeadlessButtonPeer)ok.Peer!).IsDefault, Is.True);
    }

    [Test]
    public void Enter_clicks_the_AcceptButton()
    {
        var form = new Form();
        var box = new CheckBox();
        var ok = new Button { Text = "OK" };
        var clicks = 0;
        ok.Click += (_, _) => ++clicks;
        form.Controls.Add(box);
        form.Controls.Add(ok);
        form.AcceptButton = ok;
        Realize(form);
        box.Focus();

        CanvasOf(box).RaiseKeyDown(Keys.Enter);

        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void Escape_clicks_the_CancelButton()
    {
        var form = new Form();
        var box = new CheckBox();
        var cancel = new Button { Text = "Cancel" };
        var clicks = 0;
        cancel.Click += (_, _) => ++clicks;
        form.Controls.Add(box);
        form.Controls.Add(cancel);
        form.CancelButton = cancel;
        Realize(form);
        box.Focus();

        CanvasOf(box).RaiseKeyDown(Keys.Escape);

        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void Enter_stays_with_a_control_that_claims_it()
    {
        var form = new Form();
        var search = new SearchBox { Bounds = new(0, 0, 120, 24) };
        var ok = new Button { Text = "OK" };
        var accepted = 0;
        var committed = 0;
        ok.Click += (_, _) => ++accepted;
        search.SearchCommitted += (_, _) => ++committed;
        form.Controls.Add(search);
        form.Controls.Add(ok);
        form.AcceptButton = ok;
        Realize(form);
        search.Focus();

        CanvasOf(search).RaiseKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(committed, Is.EqualTo(1), "the search box handles its own Enter");
            Assert.That(accepted, Is.Zero, "the AcceptButton stays untouched");
        });
    }

    [Test]
    public void Escape_stays_with_an_open_drop_down()
    {
        var form = new Form();
        var combo = new ComboBox { Bounds = new(0, 0, 120, 24) };
        combo.Items.Add("one");
        var cancel = new Button { Text = "Cancel" };
        var cancelled = 0;
        cancel.Click += (_, _) => ++cancelled;
        form.Controls.Add(combo);
        form.Controls.Add(cancel);
        form.CancelButton = cancel;
        Realize(form);
        combo.Focus();
        combo.DroppedDown = true;

        CanvasOf(combo).RaiseKeyDown(Keys.Escape);

        Assert.Multiple(() =>
        {
            Assert.That(combo.DroppedDown, Is.False, "Escape closes the drop-down instead");
            Assert.That(cancelled, Is.Zero);
        });
    }

    // --- Form-wide menu shortcuts and mnemonics ---------------------------------------------------

    [Test]
    public void Menu_shortcut_chords_dispatch_form_wide_from_the_focused_control()
    {
        var form = new Form();
        var strip = new MenuStrip { Bounds = new(0, 0, 300, 24) };
        var file = new ToolStripMenuItem("&File");
        var save = new ToolStripMenuItem("Save") { ShortcutKeys = Keys.Control | Keys.S };
        var saves = 0;
        save.Click += (_, _) => ++saves;
        file.DropDownItems.Add(save);
        strip.Items.Add(file);
        var box = new CheckBox { Bounds = new(0, 30, 100, 20) };
        form.Controls.Add(strip);
        form.Controls.Add(box);
        Realize(form);
        box.Focus();

        CanvasOf(box).RaiseKeyDown(Keys.S, KeyModifiers.Control);

        Assert.That(saves, Is.EqualTo(1));
    }

    [Test]
    public void Alt_mnemonic_opens_the_matching_top_level_menu()
    {
        var form = new Form();
        var strip = new MenuStrip { Bounds = new(0, 0, 300, 24) };
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("Open"));
        strip.Items.Add(file);
        var box = new CheckBox { Bounds = new(0, 30, 100, 20) };
        form.Controls.Add(strip);
        form.Controls.Add(box);
        Realize(form);
        box.Focus();

        CanvasOf(box).RaiseKeyDown(Keys.F, KeyModifiers.Alt);

        Assert.Multiple(() =>
        {
            Assert.That(strip.OpenIndex, Is.EqualTo(0));
            Assert.That(strip.Focused, Is.True, "the bar takes focus so the menu keeps receiving keys");
        });
    }

    [Test]
    public void Label_mnemonic_focuses_the_next_control_in_tab_order()
    {
        var form = new Form();
        var box = new CheckBox { TabIndex = 0 };
        var label = new Label { Text = "&Name", TabIndex = 1 };
        var name = new TextBox { TabIndex = 2 };
        form.Controls.Add(box);
        form.Controls.Add(label);
        form.Controls.Add(name);
        Realize(form);
        box.Focus();

        CanvasOf(box).RaiseKeyDown(Keys.N, KeyModifiers.Alt);

        Assert.Multiple(() =>
        {
            Assert.That(name.Focused, Is.True);
            Assert.That(form.ActiveControl, Is.SameAs(name));
        });
    }

    [Test]
    public void Label_double_ampersand_is_no_mnemonic()
    {
        var form = new Form();
        var box = new CheckBox { TabIndex = 0 };
        var label = new Label { Text = "Salt && Pepper", TabIndex = 1 };
        var name = new TextBox { TabIndex = 2 };
        form.Controls.Add(box);
        form.Controls.Add(label);
        form.Controls.Add(name);
        Realize(form);
        box.Focus();

        CanvasOf(box).RaiseKeyDown(Keys.P, KeyModifiers.Alt);

        Assert.That(name.Focused, Is.False);
    }

    // --- Modal initial focus ----------------------------------------------------------------------

    [Test]
    public void ShowDialog_applies_the_initial_focus_before_the_modal_loop()
    {
        var focusedInLoop = false;
        var form = new Form();
        var box = new CheckBox();
        form.Controls.Add(box);
        var backend = new HeadlessBackend();
        backend.ModalAction = window =>
        {
            focusedInLoop = box.Focused;
            window.Close();
        };

        form.ShowDialog(null, backend);

        Assert.That(focusedInLoop, Is.True);
    }

    [Test]
    public void Tab_navigation_reuses_one_scratch_list_and_allocates_nothing()
    {
        var form = new Form();
        var first = new Button();
        var second = new Button();
        form.Controls.AddRange(first, second);
        Realize(form);

        // Warm up: the first pass allocates the scratch list once; later passes must reuse it.
        form.MoveFocus(null, forward: true);
        form.MoveFocus(first, forward: true);

        var before = GC.GetAllocatedBytesForCurrentThread();
        form.MoveFocus(second, forward: true);
        form.MoveFocus(first, forward: true);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(bytes, Is.Zero, $"{bytes} bytes for two keyboard focus moves");
    }
}
