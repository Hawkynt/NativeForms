using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Accordion"/> must stack its <see cref="AccordionPane"/>s as real nested children, keep
/// the headers put while the open panes share the leftover height, apply
/// <see cref="AccordionExpandMode"/> on every expansion, and hide a closed pane's whole peer subtree
/// without clobbering the children's own <see cref="Control.Visible"/> flags.
/// </summary>
[TestFixture]
internal sealed class AccordionTests
{
    private const int _HeaderHeight = 22; // DefaultTheme.RowHeight

    private static HeadlessCanvasPeer Realize(Accordion accordion, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 400, 400) };
        form.Controls.Add(accordion);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    private static Accordion ThreePanes(out AccordionPane one, out AccordionPane two, out AccordionPane three)
    {
        var accordion = new Accordion { Bounds = new(0, 0, 200, 300) };
        one = new AccordionPane("Mail");
        two = new AccordionPane("Calendar");
        three = new AccordionPane("Contacts");
        accordion.Panes.AddRange(one, two, three);
        return accordion;
    }

    [Test]
    public void Adding_panes_parents_them_and_opens_only_the_first()
    {
        var accordion = ThreePanes(out var one, out var two, out var three);
        one.Controls.Add(new Button { Text = "A", Bounds = new(10, 10, 60, 24) });

        var canvas = Realize(accordion, out var backend);
        var paneCanvases = backend.Created.OfType<HeadlessCanvasPeer>().Skip(1).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(accordion.SelectedIndex, Is.Zero);
            Assert.That(accordion.SelectedPane, Is.SameAs(one));
            Assert.That(canvas.Children, Has.Count.EqualTo(3), "every pane is a real nested child");
            Assert.That(one.Expanded, Is.True);
            Assert.That(two.Expanded, Is.False);
            Assert.That(three.Expanded, Is.False);
            Assert.That(paneCanvases[0].Visible, Is.True);
            Assert.That(paneCanvases[1].Visible, Is.False);
            Assert.That(paneCanvases[2].Visible, Is.False);
        });
    }

    [Test]
    public void Open_pane_takes_the_height_the_headers_leave_and_headers_stay_put()
    {
        var accordion = ThreePanes(out var one, out var two, out var three);
        Realize(accordion, out _);

        // 300 px minus three 22 px headers leaves 234 px for the single open pane.
        Assert.Multiple(() =>
        {
            Assert.That(one.Bounds, Is.EqualTo(new Rectangle(0, _HeaderHeight, 200, 234)));
            Assert.That(two.Bounds.Height, Is.Zero);
            Assert.That(three.Bounds.Height, Is.Zero);
            Assert.That(accordion.GetHeaderBounds(0), Is.EqualTo(new Rectangle(0, 0, 200, _HeaderHeight)));
            Assert.That(accordion.GetHeaderBounds(1), Is.EqualTo(new Rectangle(0, 256, 200, _HeaderHeight)));
            Assert.That(accordion.GetHeaderBounds(2), Is.EqualTo(new Rectangle(0, 278, 200, _HeaderHeight)));
        });
    }

    [Test]
    public void Single_mode_expanding_one_pane_collapses_the_others()
    {
        var accordion = ThreePanes(out var one, out var two, out _);
        var changes = 0;
        accordion.SelectedIndexChanged += (_, _) => ++changes;
        Realize(accordion, out _);

        two.Expanded = true;

        Assert.Multiple(() =>
        {
            Assert.That(one.Expanded, Is.False, "Outlook-style: opening a drawer closes the previous one");
            Assert.That(two.Expanded, Is.True);
            Assert.That(accordion.SelectedIndex, Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(two.Bounds, Is.EqualTo(new Rectangle(0, 2 * _HeaderHeight, 200, 234)));
        });
    }

    [Test]
    public void Multiple_mode_keeps_both_open_and_splits_the_body_between_them()
    {
        var accordion = ThreePanes(out var one, out var two, out _);
        accordion.ExpandMode = AccordionExpandMode.Multiple;
        Realize(accordion, out _);

        two.Expanded = true;

        Assert.Multiple(() =>
        {
            Assert.That(one.Expanded, Is.True);
            Assert.That(two.Expanded, Is.True);
            Assert.That(one.Bounds, Is.EqualTo(new Rectangle(0, _HeaderHeight, 200, 117)));
            Assert.That(two.Bounds, Is.EqualTo(new Rectangle(0, 161, 200, 117)));
        });
    }

    [Test]
    public void Switching_back_to_single_mode_folds_down_to_the_selected_pane()
    {
        var accordion = ThreePanes(out var one, out var two, out _);
        accordion.ExpandMode = AccordionExpandMode.Multiple;
        Realize(accordion, out _);
        two.Expanded = true;

        accordion.ExpandMode = AccordionExpandMode.Single;

        Assert.Multiple(() =>
        {
            Assert.That(two.Expanded, Is.True, "the selected pane survives the fold");
            Assert.That(one.Expanded, Is.False);
        });
    }

    [Test]
    public void PaneExpanding_can_veto_the_expansion()
    {
        var accordion = ThreePanes(out var one, out var two, out _);
        Realize(accordion, out _);
        accordion.PaneExpanding += (_, e) => e.Cancel = e.Index == 1;

        two.Expanded = true;

        Assert.Multiple(() =>
        {
            Assert.That(two.Expanded, Is.False, "the veto held");
            Assert.That(one.Expanded, Is.True, "a cancelled expansion collapses nothing on its behalf");
            Assert.That(accordion.SelectedIndex, Is.Zero);
        });
    }

    [Test]
    public void PaneExpanded_and_PaneCollapsed_report_the_pane_and_its_index()
    {
        var accordion = ThreePanes(out _, out var two, out _);
        Realize(accordion, out _);
        var expanded = new List<int>();
        var collapsed = new List<int>();
        accordion.PaneExpanded += (_, e) => expanded.Add(e.Index);
        accordion.PaneCollapsed += (_, e) => collapsed.Add(e.Index);

        two.Expanded = true;

        Assert.Multiple(() =>
        {
            Assert.That(expanded, Is.EqualTo(new[] { 1 }));
            Assert.That(collapsed, Is.EqualTo(new[] { 0 }), "the pane Single mode closed reports too");
        });
    }

    [Test]
    public void Clicking_a_header_opens_that_pane()
    {
        var accordion = ThreePanes(out var one, out var two, out _);
        var canvas = Realize(accordion, out _);
        var header = accordion.GetHeaderBounds(1);

        canvas.RaiseMouseDown(10, header.Y + (header.Height / 2));
        canvas.RaiseMouseUp(10, header.Y + (header.Height / 2));

        Assert.Multiple(() =>
        {
            Assert.That(two.Expanded, Is.True);
            Assert.That(one.Expanded, Is.False);
        });
    }

    [Test]
    public void Clicking_the_open_header_in_single_mode_keeps_it_open()
    {
        var accordion = ThreePanes(out var one, out _, out _);
        var canvas = Realize(accordion, out _);

        canvas.RaiseMouseDown(10, _HeaderHeight / 2);
        canvas.RaiseMouseUp(10, _HeaderHeight / 2);

        Assert.That(one.Expanded, Is.True, "an Outlook stack never closes its last drawer by a click");
    }

    [Test]
    public void Clicking_the_open_header_in_multiple_mode_closes_it()
    {
        var accordion = ThreePanes(out var one, out _, out _);
        accordion.ExpandMode = AccordionExpandMode.Multiple;
        var canvas = Realize(accordion, out _);

        canvas.RaiseMouseDown(10, _HeaderHeight / 2);
        canvas.RaiseMouseUp(10, _HeaderHeight / 2);

        Assert.That(one.Expanded, Is.False);
    }

    [Test]
    public void Arrow_keys_move_the_header_focus_and_enter_toggles_it()
    {
        var accordion = ThreePanes(out var one, out var two, out _);
        var canvas = Realize(accordion, out _);

        canvas.RaiseKeyDown(Keys.Down);
        canvas.RaiseKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(accordion.FocusedIndex, Is.EqualTo(1));
            Assert.That(two.Expanded, Is.True);
            Assert.That(one.Expanded, Is.False);
        });
    }

    [Test]
    public void Space_toggles_the_focused_header()
    {
        var accordion = ThreePanes(out _, out _, out var three);
        var canvas = Realize(accordion, out _);

        canvas.RaiseKeyDown(Keys.End);
        canvas.RaiseKeyDown(Keys.Space);

        Assert.That(three.Expanded, Is.True);
    }

    [Test]
    public void A_closed_pane_hides_its_body_and_expanding_brings_the_whole_subtree_back()
    {
        // The veto lands on the pane — the accordion's direct child — and the native nesting takes
        // the rest of the subtree with it, which is exactly the contract Expander established. What
        // must hold at every depth is the *effective* Visible and the untouched own flags, so that
        // reopening restores precisely the body that was there.
        var accordion = ThreePanes(out var one, out var two, out _);
        var inner = new Panel { Bounds = new(0, 0, 100, 60) };
        var deep = new Button { Text = "Deep", Bounds = new(4, 4, 60, 24) };
        inner.Controls.Add(deep);
        one.Controls.Add(inner);
        Realize(accordion, out var backend);
        var panePeers = backend.Created.OfType<HeadlessCanvasPeer>().Skip(1).ToArray();

        two.Expanded = true; // closes pane one

        Assert.Multiple(() =>
        {
            Assert.That(panePeers[0].Visible, Is.False, "the closed pane's own peer went down");
            Assert.That(inner.Visible, Is.False, "Visible is effective all the way down");
            Assert.That(deep.Visible, Is.False);
            Assert.That(inner.IsVisibleLocal, Is.True, "the children's own flags were never touched");
            Assert.That(deep.IsVisibleLocal, Is.True);
        });

        one.Expanded = true;

        Assert.Multiple(() =>
        {
            Assert.That(panePeers[0].Visible, Is.True, "and came back with it");
            Assert.That(inner.Visible, Is.True);
            Assert.That(deep.Visible, Is.True);
        });
    }

    [Test]
    public void A_child_hidden_before_the_pane_closed_stays_hidden_after_it_reopens()
    {
        // Reopening restores what was there, not everything: a child the caller had hidden itself
        // must not be resurrected by the pane coming back.
        var accordion = ThreePanes(out var one, out var two, out _);
        var shown = new Button { Text = "Shown", Bounds = new(4, 4, 60, 24) };
        var hidden = new Button { Text = "Hidden", Bounds = new(4, 34, 60, 24), Visible = false };
        one.Controls.Add(shown);
        one.Controls.Add(hidden);
        Realize(accordion, out _);

        two.Expanded = true;
        one.Expanded = true;

        Assert.Multiple(() =>
        {
            Assert.That(shown.Visible, Is.True);
            Assert.That(hidden.Visible, Is.False, "the caller's own hide survived the round trip");
        });
    }

    [Test]
    public void A_pane_on_an_unselected_tab_page_stays_hidden_and_reappears_with_its_page()
    {
        var accordion = ThreePanes(out var one, out _, out _);
        var first = new TabPage("First");
        var second = new TabPage("Second");
        second.Controls.Add(accordion);
        var tabs = new TabControl { Bounds = new(0, 0, 300, 400) };
        tabs.TabPages.AddRange(first, second);

        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 400, 500) };
        form.Controls.Add(tabs);
        Application.Run(form, backend);

        Assert.That(one.Visible, Is.False, "an open pane on an unselected page is still not on screen");

        tabs.SelectedIndex = 1;

        Assert.Multiple(() =>
        {
            Assert.That(one.Visible, Is.True, "selecting the page brings the open pane back");
            Assert.That(accordion.Panes[1].Visible, Is.False, "a closed pane stays closed across the switch");
        });
    }

    [Test]
    public void A_child_added_to_a_closed_pane_realizes_off_screen()
    {
        var accordion = ThreePanes(out _, out var two, out _);
        Realize(accordion, out _);

        var late = new Button { Text = "Late", Bounds = new(4, 4, 60, 24) };
        two.Controls.Add(late);

        Assert.Multiple(() =>
        {
            Assert.That(late.Visible, Is.False, "it joined a closed drawer, so it is not on screen");
            Assert.That(late.IsVisibleLocal, Is.True, "and it will appear as soon as the pane opens");
        });
    }

    [Test]
    public void Removing_a_pane_drops_it_and_moves_the_selection()
    {
        var accordion = ThreePanes(out var one, out var two, out _);
        Realize(accordion, out _);
        two.Expanded = true;

        accordion.Panes.Remove(two);

        Assert.Multiple(() =>
        {
            Assert.That(accordion.Panes, Has.Count.EqualTo(2));
            Assert.That(two.Parent, Is.Null);
            Assert.That(accordion.SelectedIndex, Is.EqualTo(-1), "no pane is open once the open one left");
            Assert.That(one.Expanded, Is.False);
        });
    }

    [Test]
    public void Only_accordion_panes_may_be_added_as_children()
    {
        var accordion = new Accordion { Bounds = new(0, 0, 200, 300) };

        Assert.That(
            () => accordion.Controls.Add(new Button()),
            Throws.InvalidOperationException.With.Message.Contains("AccordionPane"));
    }

    [Test]
    public void Designer_style_controls_add_routes_a_pane_into_the_pane_list()
    {
        var accordion = new Accordion { Bounds = new(0, 0, 200, 300) };
        var pane = new AccordionPane("Mail");

        accordion.Controls.Add(pane);

        Assert.Multiple(() =>
        {
            Assert.That(accordion.Panes, Has.Count.EqualTo(1));
            Assert.That(accordion.Panes[0], Is.SameAs(pane));
            Assert.That(pane.Expanded, Is.True);
        });
    }

    [Test]
    public void Headers_paint_their_captions_and_a_toggle_glyph()
    {
        var accordion = ThreePanes(out _, out _, out _);
        var canvas = Realize(accordion, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Mail"), Is.True);
            Assert.That(g.DrewText("Calendar"), Is.True);
            Assert.That(g.DrewText("Contacts"), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("line ")), Is.True, "triangle glyph strokes");
        });
    }
}
