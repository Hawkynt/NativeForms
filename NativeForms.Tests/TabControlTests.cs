using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="TabControl"/> must host its <see cref="TabPage"/>s as real nested children (one canvas
/// peer per page), show exactly the selected page, paint the header strip itself — labels, active
/// accent, icons, overflow arrows — and switch pages by header click and keyboard.
/// </summary>
[TestFixture]
internal sealed class TabControlTests
{
    // DefaultTheme row height 22 + 6px chrome; RecordingGraphics measures 7px per character.
    private const int _HeaderHeight = 28;
    private const int _TabPadding = 10;
    private const int _CharWidth = 7;

    private static HeadlessCanvasPeer Realize(TabControl tabs, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(tabs);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    private static TabControl TwoPages(out TabPage one, out TabPage two)
    {
        var tabs = new TabControl { Bounds = new(0, 0, 300, 200) };
        one = new TabPage("One");
        two = new TabPage("Two");
        tabs.TabPages.AddRange(one, two);
        return tabs;
    }

    [Test]
    public void Adding_pages_parents_them_and_realizes_only_the_first_as_visible()
    {
        var tabs = TwoPages(out var one, out var two);
        one.Controls.Add(new Button { Text = "A", Bounds = new(10, 10, 60, 24) });
        two.Controls.Add(new Button { Text = "B", Bounds = new(10, 10, 60, 24) });

        var canvas = Realize(tabs, out var backend);

        var pageCanvases = backend.Created.OfType<HeadlessCanvasPeer>().Skip(1).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(tabs.SelectedIndex, Is.Zero);
            Assert.That(tabs.SelectedTab, Is.SameAs(one));
            Assert.That(canvas.Children, Has.Count.EqualTo(2), "both pages are real nested children");
            Assert.That(pageCanvases[0].Visible, Is.True);
            Assert.That(pageCanvases[1].Visible, Is.False, "only the selected page shows");
            Assert.That(pageCanvases[0].Bounds, Is.EqualTo(new Rectangle(0, _HeaderHeight, 300, 200 - _HeaderHeight)));
        });
    }

    [Test]
    public void Switching_pages_toggles_peer_visibility_and_raises_the_event_once()
    {
        var tabs = TwoPages(out _, out var two);
        var changes = 0;
        tabs.SelectedIndexChanged += (_, _) => ++changes;
        Realize(tabs, out var backend);
        var pageCanvases = backend.Created.OfType<HeadlessCanvasPeer>().Skip(1).ToArray();

        tabs.SelectedIndex = 1;

        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(tabs.SelectedTab, Is.SameAs(two));
            Assert.That(pageCanvases[0].Visible, Is.False);
            Assert.That(pageCanvases[1].Visible, Is.True);
            Assert.That(two.Bounds, Is.EqualTo(new Rectangle(0, _HeaderHeight, 300, 200 - _HeaderHeight)));
        });
    }

    [Test]
    public void Selecting_the_current_tab_again_does_not_raise_the_event()
    {
        var tabs = TwoPages(out _, out _);
        var changes = 0;
        tabs.SelectedIndexChanged += (_, _) => ++changes;
        Realize(tabs, out _);

        tabs.SelectedIndex = 0;

        Assert.That(changes, Is.Zero);
    }

    [Test]
    public void Click_on_a_header_selects_its_tab()
    {
        var tabs = TwoPages(out _, out _);
        var canvas = Realize(tabs, out _);
        canvas.RaisePaint(); // measures the tab strip

        // Each tab is 2*10 padding + 3 chars * 7px = 41px wide; x=45 is inside the second tab.
        canvas.RaiseMouseDown((2 * _TabPadding) + (3 * _CharWidth) + 4, _HeaderHeight / 2);

        Assert.That(tabs.SelectedIndex, Is.EqualTo(1));
    }

    [Test]
    public void Click_below_the_header_changes_nothing()
    {
        var tabs = TwoPages(out _, out _);
        var canvas = Realize(tabs, out _);
        canvas.RaisePaint();

        canvas.RaiseMouseDown(50, _HeaderHeight + 10);

        Assert.That(tabs.SelectedIndex, Is.Zero);
    }

    [Test]
    public void Header_paints_labels_and_accent_underline_for_the_active_tab()
    {
        var tabs = TwoPages(out _, out _);
        var canvas = Realize(tabs, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("One"), Is.True);
            Assert.That(g.DrewText("Two"), Is.True);
            Assert.That(g.Operations.Exists(o => o.StartsWith("fill #FF0078D4")), Is.True, "active tab carries the accent underline");
        });
    }

    [Test]
    public void Header_paints_the_page_icon_from_the_ImageList()
    {
        var tabs = TwoPages(out var one, out _);
        using var images = new ImageList(8);
        one.ImageIndex = images.Add(new int[64]);
        tabs.ImageList = images;
        var canvas = Realize(tabs, out _);

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("image 8x8")), Is.True);
    }

    [Test]
    public void Overflowing_tabs_show_scroll_arrows_that_shift_the_strip()
    {
        var tabs = new TabControl { Bounds = new(0, 0, 100, 150) };
        tabs.TabPages.AddRange(new TabPage("Alpha"), new TabPage("Beta"), new TabPage("Gamma"), new TabPage("Delta"));
        var canvas = Realize(tabs, out _);
        canvas.RaisePaint();

        Assert.That(tabs.ShowsOverflowArrows, Is.True);

        canvas.RaiseMouseDown(100 - 8, _HeaderHeight / 2); // right arrow
        Assert.That(tabs.FirstVisibleTab, Is.EqualTo(1));

        canvas.RaiseMouseDown(100 - 24, _HeaderHeight / 2); // left arrow
        Assert.That(tabs.FirstVisibleTab, Is.Zero);
    }

    [Test]
    public void Fitting_tabs_show_no_scroll_arrows()
    {
        var tabs = TwoPages(out _, out _);
        var canvas = Realize(tabs, out _);

        canvas.RaisePaint();

        Assert.That(tabs.ShowsOverflowArrows, Is.False);
    }

    [Test]
    public void Ctrl_Tab_cycles_forward_and_back_with_wraparound()
    {
        var tabs = TwoPages(out _, out _);
        var canvas = Realize(tabs, out _);

        canvas.RaiseKeyDown(Keys.Tab, KeyModifiers.Control);
        Assert.That(tabs.SelectedIndex, Is.EqualTo(1));

        canvas.RaiseKeyDown(Keys.Tab, KeyModifiers.Control);
        Assert.That(tabs.SelectedIndex, Is.Zero, "forward cycling wraps");

        canvas.RaiseKeyDown(Keys.Tab, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.That(tabs.SelectedIndex, Is.EqualTo(1), "backward cycling wraps");
    }

    [Test]
    public void Arrow_keys_move_the_selection_without_wrapping()
    {
        var tabs = TwoPages(out _, out _);
        var canvas = Realize(tabs, out _);

        canvas.RaiseKeyDown(Keys.Right);
        Assert.That(tabs.SelectedIndex, Is.EqualTo(1));

        canvas.RaiseKeyDown(Keys.Right);
        Assert.That(tabs.SelectedIndex, Is.EqualTo(1), "no wrap at the end");

        canvas.RaiseKeyDown(Keys.Left);
        Assert.That(tabs.SelectedIndex, Is.Zero);
    }

    [Test]
    public void Resizing_the_control_reapplies_the_content_area_to_every_page()
    {
        var tabs = TwoPages(out var one, out var two);
        Realize(tabs, out _);

        tabs.Bounds = new(0, 0, 400, 300);

        Assert.Multiple(() =>
        {
            Assert.That(one.Bounds, Is.EqualTo(new Rectangle(0, _HeaderHeight, 400, 300 - _HeaderHeight)));
            Assert.That(two.Bounds, Is.EqualTo(new Rectangle(0, _HeaderHeight, 400, 300 - _HeaderHeight)));
        });
    }

    [Test]
    public void Removing_the_selected_page_disposes_its_peers_and_selects_the_neighbor()
    {
        var tabs = TwoPages(out var one, out var two);
        Realize(tabs, out var backend);
        var pageCanvases = backend.Created.OfType<HeadlessCanvasPeer>().Skip(1).ToArray();

        tabs.TabPages.Remove(one);

        Assert.Multiple(() =>
        {
            Assert.That(pageCanvases[0].Disposed, Is.True);
            Assert.That(tabs.SelectedIndex, Is.Zero);
            Assert.That(tabs.SelectedTab, Is.SameAs(two));
            Assert.That(pageCanvases[1].Visible, Is.True);
        });
    }
}
